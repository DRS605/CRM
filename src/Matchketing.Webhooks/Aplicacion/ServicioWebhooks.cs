using System.Text.Json;
using System.Text.Json.Serialization;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Webhooks.Dominio;

namespace Matchketing.Webhooks.Aplicacion;

public sealed class ServicioWebhooks(
    IRepositorioWebhooks repositorio, IEnviaWebhook emisor, IContextoEmpresa contexto, IReloj reloj)
{
    /// <summary>Cuántas suscripciones puede tener una empresa. Cien webhooks no es un caso de uso.</summary>
    public const int MaximoPorEmpresa = 20;

    /// <summary>Cuántas entregas se intentan por pasada del trabajo.</summary>
    public const int PorPasada = 200;

    /// <summary>Cuántos intentos se guardan para mirar en la pantalla.</summary>
    public const int HistorialVisible = 20;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---------- Gestión ----------

    public async Task<Resultado<(SuscripcionWebhook Suscripcion, string Secreto)>> CrearAsync(
        string? url, string? descripcion, IReadOnlyCollection<string>? eventos, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<(SuscripcionWebhook, string)>(
                Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var tipos = Interpretar(eventos);
        if (tipos.Fallido)
        {
            return Resultado.Fallo<(SuscripcionWebhook, string)>(tipos.Error!);
        }

        var todas = await repositorio.DeLaEmpresaAsync(ct).ConfigureAwait(false);
        if (todas.Count >= MaximoPorEmpresa)
        {
            return Resultado.Fallo<(SuscripcionWebhook, string)>(Error.Conflicto(
                "webhook.demasiados", $"No se pueden tener más de {MaximoPorEmpresa} webhooks."));
        }

        // Dos suscripciones a la misma URL con los mismos eventos es siempre un error de dedo, y se
        // paga con entregas duplicadas que nadie relaciona con esto.
        if (todas.Any(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase)))
        {
            return Resultado.Fallo<(SuscripcionWebhook, string)>(Error.Conflicto(
                "webhook.repetido", "Ya hay un webhook a esa dirección."));
        }

        var creada = SuscripcionWebhook.Crear(empresaId, url, descripcion, tipos.Valor, reloj);
        if (creada.Fallido)
        {
            return Resultado.Fallo<(SuscripcionWebhook, string)>(creada.Error!);
        }

        repositorio.Anadir(creada.Valor);

        // El secreto se devuelve **aquí y solo aquí**. Después ya no se puede leer, solo rotar.
        return Resultado.Ok((creada.Valor, creada.Valor.Secreto));
    }

    public async Task<IReadOnlyList<FichaSuscripcion>> ListarAsync(CancellationToken ct = default)
    {
        var todas = await repositorio.DeLaEmpresaAsync(ct).ConfigureAwait(false);
        var pendientes = await repositorio.PendientesPorSuscripcionAsync(ct).ConfigureAwait(false);

        return todas
            .OrderBy(s => s.Descripcion, StringComparer.OrdinalIgnoreCase)
            .Select(s => new FichaSuscripcion(
                s.Id, s.Url, s.Descripcion,
                s.Tipos.Select(TiposEvento.Texto).ToArray(),
                s.Activa, s.MotivoApagado, s.CreadaEn, s.UltimaEntregaEn,
                pendientes.TryGetValue(s.Id, out var cuantas) ? cuantas : 0))
            .ToArray();
    }

    public async Task<Resultado<IReadOnlyList<FichaEntrega>>> HistorialAsync(Guid id, CancellationToken ct = default)
    {
        if (await repositorio.PorIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Resultado.Fallo<IReadOnlyList<FichaEntrega>>(
                Error.NoEncontrado("webhook.no_encontrado", "Ese webhook no existe."));
        }

        var entregas = await repositorio.UltimasDeAsync(id, HistorialVisible, ct).ConfigureAwait(false);

        return Resultado.Ok<IReadOnlyList<FichaEntrega>>(entregas
            .Select(e => new FichaEntrega(
                e.Id, TiposEvento.Texto(e.Tipo), Texto(e.Estado), e.Intentos,
                e.CreadaEn, e.ProximoIntentoEn, e.EntregadaEn, e.UltimoCodigo, e.UltimoFallo))
            .ToArray());
    }

    public async Task<Resultado> CambiarAsync(
        Guid id, string? descripcion, IReadOnlyCollection<string>? eventos, CancellationToken ct = default)
    {
        var suscripcion = await repositorio.PorIdAsync(id, ct).ConfigureAwait(false);
        if (suscripcion is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("webhook.no_encontrado", "Ese webhook no existe."));
        }

        var tipos = Interpretar(eventos);
        return tipos.Fallido ? Resultado.Fallo(tipos.Error!) : suscripcion.Cambiar(descripcion, tipos.Valor);
    }

    public async Task<Resultado<string>> RotarSecretoAsync(Guid id, CancellationToken ct = default)
    {
        var suscripcion = await repositorio.PorIdAsync(id, ct).ConfigureAwait(false);
        return suscripcion is null
            ? Resultado.Fallo<string>(Error.NoEncontrado("webhook.no_encontrado", "Ese webhook no existe."))
            : Resultado.Ok(suscripcion.RotarSecreto());
    }

    public async Task<Resultado> ReactivarAsync(Guid id, CancellationToken ct = default)
    {
        var suscripcion = await repositorio.PorIdAsync(id, ct).ConfigureAwait(false);
        if (suscripcion is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("webhook.no_encontrado", "Ese webhook no existe."));
        }

        suscripcion.Reactivar();
        return Resultado.Ok();
    }

    public async Task<Resultado> BorrarAsync(Guid id, CancellationToken ct = default)
    {
        var suscripcion = await repositorio.PorIdAsync(id, ct).ConfigureAwait(false);
        if (suscripcion is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("webhook.no_encontrado", "Ese webhook no existe."));
        }

        repositorio.Quitar(suscripcion);
        return Resultado.Ok();
    }

    // ---------- Emisión ----------

    /// <summary>
    /// Apunta el evento para todas las suscripciones que lo escuchen. **No manda nada**: escribe filas.
    ///
    /// Se llama desde dentro de la operación de negocio y comparte su transacción, que es lo que hace
    /// que «pasó» y «se avisó» no se puedan separar. Y por eso mismo no devuelve
    /// <c>Resultado</c>: si esto pudiera tumbar la operación, una URL mal escrita en Ajustes impediría
    /// ganar una oportunidad. Un webhook es un extra; el negocio no depende de él.
    /// </summary>
    public async Task<int> EncolarAsync(Evento evento, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evento);

        if (contexto.EmpresaId is not { } empresaId)
        {
            return 0;
        }

        var suscripciones = await repositorio.QueEscuchanAsync(evento.Tipo, ct).ConfigureAwait(false);
        if (suscripciones.Count == 0)
        {
            return 0;
        }

        var ahora = reloj.AhoraUtc;

        foreach (var suscripcion in suscripciones)
        {
            // Un identificador por entrega, no por evento: dos suscripciones son dos receptores
            // independientes y cada uno deduplica por su cuenta. El reintento reusa este mismo.
            var id = Guid.NewGuid();
            var cuerpo = JsonSerializer.Serialize(
                new
                {
                    id,
                    tipo = TiposEvento.Texto(evento.Tipo),
                    ocurridoEn = ahora,
                    empresaId,
                    datos = evento.Datos,
                },
                Json);

            repositorio.AnadirEntrega(Entrega.Crear(id, empresaId, suscripcion.Id, evento.Tipo, cuerpo, reloj));
        }

        return suscripciones.Count;
    }

    /// <summary>
    /// Intenta las entregas que les toca. La llama el trabajo periódico.
    ///
    /// Va de una en una a propósito. En paralelo iría más rápido y traería dos problemas de los que se
    /// pagan tarde: un endpoint lento se llevaría por delante el resto de la pasada, y varias entregas
    /// simultáneas al mismo sitio llegarían desordenadas más a menudo de lo necesario.
    /// </summary>
    public async Task<ResumenEntregas> EntregarPendientesAsync(CancellationToken ct = default)
    {
        var pendientes = await repositorio.PendientesAsync(reloj.AhoraUtc, PorPasada, ct).ConfigureAwait(false);
        if (pendientes.Count == 0)
        {
            return new ResumenEntregas(0, 0, 0, 0);
        }

        int salieron = 0, reintentar = 0, agotadas = 0, apagadas = 0;

        foreach (var entrega in pendientes)
        {
            var suscripcion = await repositorio.PorIdAsync(entrega.SuscripcionId, ct).ConfigureAwait(false);

            // La suscripción se borró o se apagó mientras la entrega esperaba. Se da por agotada sin
            // intentarla: mandar a una URL que alguien acaba de quitar es justo lo que no quería.
            if (suscripcion is null || !suscripcion.Activa)
            {
                entrega.Abandonar("La suscripción ya no está activa.", reloj);
                agotadas++;
                continue;
            }

            var r = await emisor.EnviarAsync(suscripcion, entrega, ct).ConfigureAwait(false);

            if (r.Salio)
            {
                entrega.Salio(r.Codigo ?? 200, reloj);
                suscripcion.Entregada(reloj);
                salieron++;
                continue;
            }

            if (entrega.NoSalio(r.Codigo, r.Fallo ?? "Sin respuesta.", reloj))
            {
                agotadas++;
                if (suscripcion.Fallada($"{SuscripcionWebhook.FallosParaDesactivar} entregas seguidas sin llegar. Último fallo: {r.Fallo}"))
                {
                    apagadas++;
                }
            }
            else
            {
                reintentar++;
            }
        }

        return new ResumenEntregas(salieron, reintentar, agotadas, apagadas);
    }

    private static string Texto(EstadoEntrega estado) => estado switch
    {
        EstadoEntrega.Entregada => "entregada",
        EstadoEntrega.Agotada => "agotada",
        _ => "pendiente",
    };

    private static Resultado<IReadOnlyCollection<TipoEvento>> Interpretar(IReadOnlyCollection<string>? eventos)
    {
        if (eventos is null || eventos.Count == 0)
        {
            return Resultado.Fallo<IReadOnlyCollection<TipoEvento>>(Error.Validacion(
                "webhook.sin_eventos", "Elige al menos un evento que escuchar."));
        }

        var tipos = new List<TipoEvento>();

        foreach (var texto in eventos)
        {
            // Un nombre que no existe es un error, no algo que se ignore. Aceptarlo en silencio deja
            // una suscripción que nunca dispara y a alguien mirando por qué.
            if (TiposEvento.De(texto) is not { } tipo)
            {
                return Resultado.Fallo<IReadOnlyCollection<TipoEvento>>(Error.Validacion(
                    "webhook.evento_desconocido", $"El evento «{texto}» no existe."));
            }

            tipos.Add(tipo);
        }

        return Resultado.Ok<IReadOnlyCollection<TipoEvento>>(tipos);
    }
}
