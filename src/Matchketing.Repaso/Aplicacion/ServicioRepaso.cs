using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Repaso.Dominio;

namespace Matchketing.Repaso.Aplicacion;

/// <summary>
/// El repaso semanal: la respuesta de match.keting a por qué los comerciales no usan los CRM.
///
/// La idea es una inversión. Un CRM normal dice «cuéntame qué has hecho» y espera que alguien escriba;
/// escribir una nota son cuarenta segundos y nadie tiene cuarenta segundos veinte veces. Aquí el
/// sistema mira lo que ya sabe, **deduce qué debería haber pasado y pregunta**, con la respuesta
/// probable ya puesta delante. Contestar son tres segundos.
///
/// Dos reglas de diseño que no se negocian:
///
/// * **Cero texto libre.** En cuanto una respuesta obligue a escribir, el repaso deja de durar
///   minutos. Todo son opciones cerradas; el único dato extra que se pide en todo el módulo es el
///   motivo de una pérdida, y también es una lista.
/// * **Se puede vaciar.** Toda respuesta —incluidas «sigue viva» y «ahora no»— quita la pregunta de la
///   pila. Una pantalla que nunca llega a cero se abandona en dos semanas.
/// </summary>
public sealed class ServicioRepaso(
    IConsultaRepaso consulta,
    IRepositorioPospuestas pospuestas,
    IAccionesRepaso acciones,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    /// <summary>
    /// Cuántas preguntas se sirven de una vez. Con más de treinta, la promesa de «unos minutos» sería
    /// mentira; es mejor decir cuántas quedan y que se sigan mañana.
    /// </summary>
    public const int TopePila = 30;

    /// <summary>Lo que se tarda en contestar una tarjeta, medido a ojo pero honestamente.</summary>
    public const int SegundosPorPregunta = 4;

    public async Task<PilaRepaso> PilaAsync(CancellationToken ct = default)
    {
        var hoy = Hoy();
        var hallazgos = await consulta.HallazgosAsync(ct).ConfigureAwait(false);
        var aparcadas = await pospuestas.VigentesAsync(hoy, ct).ConfigureAwait(false);

        var preguntas = hallazgos
            .Select(h => new { Hallazgo = h, Clave = new ClavePregunta(h.Tipo, h.ReferenciaId) })
            .Where(x => !aparcadas.Contains(x.Clave.ToString()))
            // Primero el tipo (el orden del enum es el orden de prioridad) y, dentro, lo más atrasado.
            .OrderBy(x => (int)x.Hallazgo.Tipo)
            .ThenByDescending(x => x.Hallazgo.Dias)
            .ThenByDescending(x => x.Hallazgo.Importe ?? 0m)
            .ToList();

        var pila = preguntas
            .Take(TopePila)
            .Select(x => Redactar(x.Clave, x.Hallazgo))
            .ToList();

        return new PilaRepaso(pila, preguntas.Count, preguntas.Count * SegundosPorPregunta);
    }

    public Task<ResumenSemana> ResumenAsync(int dias = 7, CancellationToken ct = default) =>
        consulta.ResumenAsync(Math.Clamp(dias, 1, 90), ct);

    /// <summary>
    /// Contesta una pregunta. Hace todo lo que la respuesta implica —cerrar la tarea, registrar la
    /// llamada, ganar la oportunidad— y quita la pregunta de la pila.
    /// </summary>
    public async Task<Resultado<Resuelta>> ResponderAsync(
        string? clave, Respuesta respuesta, int? motivo = null, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Resuelta>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var interpretada = ClavePregunta.Interpretar(clave);
        if (interpretada.Fallido)
        {
            return Resultado.Fallo<Resuelta>(interpretada.Error!);
        }

        var pregunta = interpretada.Valor;

        // R1: la respuesta tiene que pertenecer a la pregunta.
        var opcion = Opciones.Comprobar(pregunta.Tipo, respuesta);
        if (opcion.Fallido)
        {
            return Resultado.Fallo<Resuelta>(opcion.Error!);
        }

        // R2: lo irreversible pide su dato. Sin esto, un toque de más cierra una venta como perdida.
        if (opcion.Valor.PideMotivo && motivo is null)
        {
            return Resultado.Fallo<Resuelta>(Error.Validacion(
                "repaso.falta_motivo", "Para dar una oportunidad por perdida hay que decir por qué."));
        }

        var efecto = await AplicarAsync(pregunta, respuesta, motivo, ct).ConfigureAwait(false);
        if (efecto.Fallido)
        {
            return Resultado.Fallo<Resuelta>(efecto.Error!);
        }

        // Toda respuesta aparca la pregunta. Las que cambian el mundo dejan de cumplirse por sí solas
        // —una tarea cerrada ya no está vencida—, pero aparcarlas igual cuesta una fila y evita el
        // parpadeo de que reaparezcan si algo se recalcula a medias.
        pospuestas.Anadir(Pospuesta.Crear(
            empresaId, pregunta, contexto.UsuarioId, Pospuesta.DiasPara(pregunta.Tipo, respuesta), reloj));

        return Resultado.Ok(new Resuelta(pregunta.ToString(), efecto.Valor));
    }

    private async Task<Resultado<string>> AplicarAsync(ClavePregunta pregunta, Respuesta respuesta, int? motivo, CancellationToken ct)
    {
        if (respuesta == Respuesta.Saltar)
        {
            return Resultado.Ok("Aparcada unos días.");
        }

        return pregunta.Tipo switch
        {
            TipoPregunta.TareaVencida => await SobreTareaAsync(pregunta.ReferenciaId, respuesta, ct).ConfigureAwait(false),
            TipoPregunta.LeadSinTocar => await SobreLeadAsync(pregunta.ReferenciaId, respuesta, ct).ConfigureAwait(false),
            TipoPregunta.CierrePasado or TipoPregunta.OportunidadEstancada =>
                await SobreOportunidadAsync(pregunta.ReferenciaId, respuesta, motivo, ct).ConfigureAwait(false),
            TipoPregunta.SilencioCaliente => await SobreSilencioAsync(pregunta.ReferenciaId, respuesta, ct).ConfigureAwait(false),
            TipoPregunta.ClienteSinSiguientePaso => await SobreClienteAsync(pregunta.ReferenciaId, respuesta, ct).ConfigureAwait(false),
            _ => Resultado.Fallo<string>(Error.Validacion("repaso.tipo_desconocido", "Esa pregunta no existe.")),
        };
    }

    private async Task<Resultado<string>> SobreTareaAsync(Guid tareaId, Respuesta respuesta, CancellationToken ct)
    {
        var hecho = respuesta switch
        {
            Respuesta.Hecha => await acciones.CompletarTareaAsync(tareaId, ct).ConfigureAwait(false),

            // «Aún no» la manda al siguiente día laborable, no a mañana a secas: si se contesta un
            // viernes, aparecería el sábado en una pantalla que nadie abre y volvería vencida el lunes.
            Respuesta.AunNo => await acciones.AplazarTareaAsync(tareaId, SiguienteDiaLaborable(), ct).ConfigureAwait(false),
            Respuesta.YaNoHaceFalta => await acciones.DescartarTareaAsync(tareaId, ct).ConfigureAwait(false),
            _ => false,
        };

        if (!hecho)
        {
            return Resultado.Fallo<string>(Error.NoEncontrado("tarea.no_encontrada", "Esa tarea ya no está pendiente."));
        }

        return Resultado.Ok(respuesta switch
        {
            Respuesta.Hecha => "Tarea cerrada.",
            Respuesta.AunNo => $"La verás otra vez el {SiguienteDiaLaborable():dd/MM}.",
            _ => "Descartada. Queda constancia de que se decidió no hacerla.",
        });
    }

    private async Task<Resultado<string>> SobreLeadAsync(Guid contactoId, Respuesta respuesta, CancellationToken ct)
    {
        var llamada = respuesta switch
        {
            Respuesta.Contactado => ResultadoDeLlamada.Contactado,
            Respuesta.NoContesta => ResultadoDeLlamada.NoContesta,
            _ => ResultadoDeLlamada.NoInteresa,
        };

        if (!await acciones.RegistrarLlamadaAsync(contactoId, llamada, ct).ConfigureAwait(false))
        {
            return Resultado.Fallo<string>(Error.NoEncontrado("contacto.no_encontrado", "Ese contacto ya no existe."));
        }

        // «No le interesa» también cambia el estado: dejarlo como lead haría que volviera a salir en
        // Hoy la semana que viene, y esa es exactamente la clase de cosa que hace desconfiar del CRM.
        if (respuesta == Respuesta.NoLeInteresa)
        {
            await acciones.DescartarContactoAsync(contactoId, ct).ConfigureAwait(false);
            return Resultado.Ok("Apuntado en su ficha y marcado como descartado.");
        }

        return Resultado.Ok(respuesta == Respuesta.Contactado
            ? "Llamada apuntada en su ficha."
            : "Apuntada, y con recordatorio para volver a intentarlo.");
    }

    private async Task<Resultado<string>> SobreOportunidadAsync(Guid oportunidadId, Respuesta respuesta, int? motivo, CancellationToken ct)
    {
        switch (respuesta)
        {
            case Respuesta.SigueViva:
                // No se toca nada del embudo a propósito. Mover la fecha de entrada en la etapa para
                // que dejara de contar como estancada sería falsear el histórico para arreglar un
                // problema de pantalla. Lo que se guarda es que alguien la revisó.
                return Resultado.Ok("Anotado que la has revisado. Vuelve en una semana.");

            case Respuesta.Ganada:
                var importe = await acciones.GanarOportunidadAsync(oportunidadId, ct).ConfigureAwait(false);
                return importe is null
                    ? Resultado.Fallo<string>(Error.NoEncontrado("oportunidad.no_encontrada", "Esa oportunidad ya está cerrada."))
                    : Resultado.Ok($"Ganada: {Castellano.Euros(importe.Value)}. Enhorabuena.");

            case Respuesta.Perdida:
                return await acciones.PerderOportunidadAsync(oportunidadId, motivo!.Value, ct).ConfigureAwait(false)
                    ? Resultado.Ok("Perdida, con su motivo. Sale en el informe de por qué se pierde.")
                    : Resultado.Fallo<string>(Error.NoEncontrado("oportunidad.no_encontrada", "Esa oportunidad ya está cerrada."));

            case Respuesta.OtraFecha:
                var nueva = Hoy().AddDays(14);
                return await acciones.MoverCierreAsync(oportunidadId, nueva, ct).ConfigureAwait(false)
                    ? Resultado.Ok($"Nueva fecha: {nueva:dd/MM}.")
                    : Resultado.Fallo<string>(Error.NoEncontrado("oportunidad.no_encontrada", "Esa oportunidad ya está cerrada."));

            default:
                return Resultado.Fallo<string>(Error.Validacion("repaso.respuesta_no_valida", "Esa respuesta no vale aquí."));
        }
    }

    private async Task<Resultado<string>> SobreSilencioAsync(Guid contactoId, Respuesta respuesta, CancellationToken ct)
    {
        if (respuesta == Respuesta.DejarloEstar)
        {
            return Resultado.Ok("Vale. No insistiremos en un mes.");
        }

        return await acciones.CrearTareaAsync(contactoId, "Llamar: lleva tiempo sin novedades", Hoy(), ct).ConfigureAwait(false)
            ? Resultado.Ok("Está en tu lista de hoy.")
            : Resultado.Fallo<string>(Error.NoEncontrado("contacto.no_encontrado", "Ese contacto ya no existe."));
    }

    private async Task<Resultado<string>> SobreClienteAsync(Guid contactoId, Respuesta respuesta, CancellationToken ct)
    {
        if (respuesta == Respuesta.DejarloEstar)
        {
            return Resultado.Ok("Vale. No volveremos a sacarlo en tres meses.");
        }

        return await acciones.CrearTareaAsync(contactoId, "Pedirle que nos recomiende", Hoy(), ct).ConfigureAwait(false)
            ? Resultado.Ok("Está en tu lista de hoy.")
            : Resultado.Fallo<string>(Error.NoEncontrado("contacto.no_encontrado", "Ese contacto ya no existe."));
    }

    private DateOnly Hoy() => DateOnly.FromDateTime(reloj.AhoraUtc.UtcDateTime);

    private DateOnly SiguienteDiaLaborable()
    {
        var dia = Hoy().AddDays(1);
        while (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            dia = dia.AddDays(1);
        }

        return dia;
    }

    /// <summary>
    /// La pregunta, redactada. Se escribe **aquí** y no en el cliente para que la frase sea la misma en
    /// la web, en el móvil y en cualquier integración futura, y para que el motivo se pueda probar: una
    /// tarjeta sin motivo no se enseña, igual que en Hoy.
    /// </summary>
    private Pregunta Redactar(ClavePregunta clave, Hallazgo h)
    {
        var quien = h.NombreContacto ?? "este contacto";

        var (titular, detalle) = h.Tipo switch
        {
            TipoPregunta.TareaVencida => (
                $"«{h.Titulo}»",
                h.Dias == 0
                    ? "Vencía hoy. ¿La has hecho?"
                    : $"Vencía hace {Dias(h.Dias)}. ¿La has hecho?"),

            TipoPregunta.LeadSinTocar => (
                quien,
                $"Entró hace {Dias(h.Dias)} y no consta que hayas hablado con él."),

            TipoPregunta.CierrePasado => (
                $"{h.Titulo} · {Castellano.Euros(h.Importe ?? 0m)}",
                $"Dijiste que cerraría el {h.Fecha:dd/MM} y sigue abierta."),

            TipoPregunta.OportunidadEstancada => (
                $"{h.Titulo} · {Castellano.Euros(h.Importe ?? 0m)}",
                $"Lleva {Dias(h.Dias)} en la misma etapa."),

            TipoPregunta.SilencioCaliente => (
                quien,
                $"Match {h.Match}, y {Dias(h.Dias)} sin saber nada de él."),

            TipoPregunta.ClienteSinSiguientePaso => (
                quien,
                $"Te compró hace {Dias(h.Dias)} y no hay nada previsto con él."),

            _ => (quien, string.Empty),
        };

        return new Pregunta(
            clave.ToString(), h.Tipo, titular, detalle,
            h.ContactoId, h.NombreContacto, h.Telefono, h.OportunidadId, h.TareaId,
            h.Importe, h.Match, Opciones.ConSaltar(h.Tipo));
    }

    private static string Dias(int dias) => dias switch
    {
        0 => "hoy",
        1 => "un día",
        < 14 => $"{dias} días",
        < 60 => $"{dias / 7} semanas",
        _ => $"{dias / 30} meses",
    };
}
