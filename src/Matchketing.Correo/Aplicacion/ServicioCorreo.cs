using Matchketing.Correo.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Correo.Aplicacion;

public sealed class ServicioCorreo(
    IRepositorioCorreo repositorio,
    IPermisoDeEnvio permiso,
    IConsultaDatosDelEnvio datos,
    IEnviaCorreo emisor,
    IEnlaceDeBaja enlaces,
    IApuntaEnCronologia cronologia,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    public const int MaximoPlantillas = 40;

    /// <summary>Cuántos correos se intentan por pasada del trabajo.</summary>
    public const int PorPasada = 100;

    /// <summary>Cuántos correos se enseñan en la ficha de un contacto.</summary>
    public const int HistorialVisible = 20;

    // ---------- Plantillas ----------

    public async Task<Resultado<Plantilla>> CrearPlantillaAsync(
        string? nombre, string? asunto, string? cuerpo, ParaQue paraQue, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Plantilla>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var todas = await repositorio.PlantillasAsync(ct).ConfigureAwait(false);
        if (todas.Count >= MaximoPlantillas)
        {
            return Resultado.Fallo<Plantilla>(Error.Conflicto(
                "plantilla.demasiadas", $"No se pueden tener más de {MaximoPlantillas} plantillas."));
        }

        var creada = Plantilla.Crear(empresaId, nombre, asunto, cuerpo, paraQue, reloj);
        if (creada.Fallido)
        {
            return creada;
        }

        repositorio.Anadir(creada.Valor);
        return creada;
    }

    public async Task<IReadOnlyList<FichaPlantilla>> PlantillasAsync(CancellationToken ct = default)
    {
        var todas = await repositorio.PlantillasAsync(ct).ConfigureAwait(false);

        // Por uso y luego por nombre: la que se usa todos los días tiene que estar arriba, y con dos
        // sin usar el orden alfabético es más previsible que el de creación.
        return todas
            .OrderByDescending(p => p.Usos)
            .ThenBy(p => p.Nombre, StringComparer.OrdinalIgnoreCase)
            .Select(p => new FichaPlantilla(
                p.Id, p.Nombre, p.Asunto, p.Cuerpo, Texto(p.ParaQue), p.Usos, p.CreadaEn))
            .ToArray();
    }

    public async Task<Resultado> CambiarPlantillaAsync(
        Guid id, string? nombre, string? asunto, string? cuerpo, ParaQue paraQue, CancellationToken ct = default)
    {
        var plantilla = await repositorio.PlantillaAsync(id, ct).ConfigureAwait(false);
        return plantilla is null
            ? Resultado.Fallo(Error.NoEncontrado("plantilla.no_encontrada", "Esa plantilla no existe."))
            : plantilla.Cambiar(nombre, asunto, cuerpo, paraQue);
    }

    public async Task<Resultado> BorrarPlantillaAsync(Guid id, CancellationToken ct = default)
    {
        var plantilla = await repositorio.PlantillaAsync(id, ct).ConfigureAwait(false);
        if (plantilla is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("plantilla.no_encontrada", "Esa plantilla no existe."));
        }

        // Los correos ya enviados guardan su propio texto, así que borrar la plantilla no borra el
        // historial. `PlantillaId` se queda apuntando a algo que no está, y da igual: solo sirve para
        // contar usos.
        repositorio.Quitar(plantilla);
        return Resultado.Ok();
    }

    // ---------- Escribir ----------

    /// <summary>
    /// Lo que se va a mandar, antes de mandarlo. **Nunca se envía sin pasar por aquí** en la pantalla:
    /// un correo es irreversible y el comercial tiene que ver el texto con los huecos ya rellenos.
    /// </summary>
    public async Task<Resultado<Borrador>> PrepararAsync(Guid contactoId, Guid plantillaId, CancellationToken ct = default)
    {
        if (contexto.UsuarioId is not { } usuarioId)
        {
            return Resultado.Fallo<Borrador>(Error.NoAutorizado("sesion.sin_usuario", "No hay sesión."));
        }

        var plantilla = await repositorio.PlantillaAsync(plantillaId, ct).ConfigureAwait(false);
        if (plantilla is null)
        {
            return Resultado.Fallo<Borrador>(Error.NoEncontrado("plantilla.no_encontrada", "Esa plantilla no existe."));
        }

        var suyos = await datos.DeAsync(contactoId, usuarioId, ct).ConfigureAwait(false);
        if (suyos is null)
        {
            return Resultado.Fallo<Borrador>(Error.NoEncontrado("contacto.no_encontrado", "Ese contacto no existe."));
        }

        var redactado = plantilla.Redactar(suyos);
        if (redactado.Fallido)
        {
            return Resultado.Fallo<Borrador>(redactado.Error!);
        }

        var direccion = await DireccionAsync(contactoId, usuarioId, ct).ConfigureAwait(false);
        var puede = await permiso.PuedeEscribirAsync(contactoId, plantilla.ParaQue, ct).ConfigureAwait(false);

        return Resultado.Ok(new Borrador(
            redactado.Valor.Asunto, redactado.Valor.Cuerpo, direccion ?? "—",
            puede.Exito && direccion is not null,
            direccion is null ? "Este contacto no tiene correo." : puede.Fallido ? puede.Error!.Mensaje : null));
    }

    /// <summary>
    /// Encola el correo. La comprobación de permiso se hace aquí **y otra vez al enviar**: entre lo uno
    /// y lo otro pasan minutos, y en esos minutos alguien puede darse de baja.
    /// </summary>
    public Task<Resultado<Dominio.Correo>> EnviarAsync(
        Guid contactoId, Guid? plantillaId, string? asunto, string? cuerpo, ParaQue paraQue, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId || contexto.UsuarioId is not { } usuarioId)
        {
            return Task.FromResult(Resultado.Fallo<Dominio.Correo>(
                Error.NoAutorizado("sesion.sin_usuario", "No hay sesión.")));
        }

        return EncolarAsync(empresaId, usuarioId, contactoId, plantillaId, asunto, cuerpo, paraQue, ct);
    }

    /// <summary>
    /// Lo mismo, pero firmado por alguien que **no es quien está haciendo la petición**.
    ///
    /// Existe por las campañas y solo por eso. Una campaña la lanza una persona y los correos salen por
    /// lotes minutos u horas después, desde un trabajo de fondo donde no hay sesión de nadie. Sin esto,
    /// el trabajo tendría que inventarse un remitente, y el hueco `{{comercial}}` saldría vacío o con el
    /// nombre de la empresa: un correo comercial que no lo firma nadie es un correo al que no se puede
    /// contestar.
    ///
    /// Quién firma es un **parámetro explícito** y no estado ambiente a propósito: es la diferencia entre
    /// «el sistema mandó esto» y «esto lo mandó Marta», y esa diferencia tiene que estar escrita en la
    /// llamada. Todo lo demás —el permiso, la plantilla, el buzón de salida— es idéntico: no hay un
    /// camino rápido para campañas.
    /// </summary>
    public Task<Resultado<Dominio.Correo>> EnviarEnNombreDeAsync(
        Guid usuarioId, Guid contactoId, Guid plantillaId, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Task.FromResult(Resultado.Fallo<Dominio.Correo>(
                Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa.")));
        }

        if (usuarioId == Guid.Empty)
        {
            return Task.FromResult(Resultado.Fallo<Dominio.Correo>(
                Error.NoAutorizado("correo.sin_firma", "Un correo lo tiene que firmar alguien.")));
        }

        return EncolarAsync(empresaId, usuarioId, contactoId, plantillaId, null, null, ParaQue.Comercial, ct);
    }

    private async Task<Resultado<Dominio.Correo>> EncolarAsync(
        Guid empresaId, Guid usuarioId, Guid contactoId, Guid? plantillaId,
        string? asunto, string? cuerpo, ParaQue paraQue, CancellationToken ct)
    {
        Plantilla? plantilla = null;
        if (plantillaId is { } id)
        {
            plantilla = await repositorio.PlantillaAsync(id, ct).ConfigureAwait(false);
            if (plantilla is null)
            {
                return Resultado.Fallo<Dominio.Correo>(
                    Error.NoEncontrado("plantilla.no_encontrada", "Esa plantilla no existe."));
            }

            // El «para qué» sale de la plantilla, no del cliente. Si viniera por parámetro, bastaría con
            // mandar `AtenderSolicitud` para saltarse el consentimiento comercial.
            paraQue = plantilla.ParaQue;

            var suyos = await datos.DeAsync(contactoId, usuarioId, ct).ConfigureAwait(false);
            if (suyos is null)
            {
                return Resultado.Fallo<Dominio.Correo>(
                    Error.NoEncontrado("contacto.no_encontrado", "Ese contacto no existe."));
            }

            var redactado = plantilla.Redactar(suyos);
            if (redactado.Fallido)
            {
                return Resultado.Fallo<Dominio.Correo>(redactado.Error!);
            }

            // Lo de la plantilla manda sobre lo que llegue por parámetro: la pantalla enseña el borrador
            // y envía; si dejáramos editar el texto por aquí, lo que se ve y lo que sale podrían
            // separarse sin que nadie lo note.
            asunto = redactado.Valor.Asunto;
            cuerpo = redactado.Valor.Cuerpo;
        }

        var puede = await permiso.PuedeEscribirAsync(contactoId, paraQue, ct).ConfigureAwait(false);
        if (puede.Fallido)
        {
            return Resultado.Fallo<Dominio.Correo>(puede.Error!);
        }

        var direccion = await DireccionAsync(contactoId, usuarioId, ct).ConfigureAwait(false);

        var correo = Dominio.Correo.Crear(
            empresaId, contactoId, usuarioId, direccion, asunto, cuerpo, paraQue, plantillaId, reloj);

        if (correo.Fallido)
        {
            return correo;
        }

        repositorio.AnadirCorreo(correo.Valor);
        plantilla?.Usada();

        // La cronología se apunta al **encolar**, no al enviar, y con el texto: si se apuntara al salir,
        // el comercial vería su ficha sin rastro del correo que acaba de mandar y volvería a mandarlo.
        // Si luego no sale, el estado del correo lo dice.
        await cronologia.ApuntarCorreoAsync(contactoId, $"«{asunto}»", ct).ConfigureAwait(false);

        return correo;
    }

    // ---------- Enviar de verdad ----------

    /// <summary>
    /// Manda los que les toca. La llama el trabajo periódico.
    ///
    /// El permiso se vuelve a comprobar aquí para cada uno, y **esta es la comprobación que cuenta**.
    /// Un correo comercial a quien se dio de baja hace diez minutos no es un fallo técnico.
    /// </summary>
    public async Task<ResumenEnvios> EnviarPendientesAsync(string? baseUrlPixel, CancellationToken ct = default)
    {
        var pendientes = await repositorio.PendientesAsync(reloj.AhoraUtc, PorPasada, ct).ConfigureAwait(false);
        if (pendientes.Count == 0)
        {
            return new ResumenEnvios(0, 0, 0, 0);
        }

        int salieron = 0, reintentar = 0, fallidos = 0, cancelados = 0;

        foreach (var correo in pendientes)
        {
            var puede = await permiso.PuedeEscribirAsync(correo.ContactoId, correo.ParaQue, ct).ConfigureAwait(false);
            if (puede.Fallido)
            {
                correo.Cancelar(puede.Error!.Mensaje, reloj);
                cancelados++;
                continue;
            }

            var url = baseUrlPixel is null ? null : $"{baseUrlPixel.TrimEnd('/')}/e/{correo.TokenApertura}.gif";

            // **El enlace de baja va solo en los comerciales**, y esa distinción es la que importa. En
            // una comunicación comercial es obligatorio y además es lo que piden Gmail y Outlook para
            // no tratar los envíos como no deseados. En una respuesta a lo que la persona ha
            // preguntado sería absurdo: no se ha apuntado a nada de lo que darse de baja.
            var baja = correo.ParaQue == Dominio.ParaQue.Comercial ? enlaces.De(correo.ContactoId) : null;

            var r = await emisor.EnviarAsync(correo, url, baja, ct).ConfigureAwait(false);

            if (r.Salio)
            {
                correo.Salio(reloj);
                salieron++;
                continue;
            }

            if (correo.NoSalio(r.Fallo ?? "Sin respuesta del servidor de correo.", r.Definitivo, reloj))
            {
                fallidos++;
            }
            else
            {
                reintentar++;
            }
        }

        return new ResumenEnvios(salieron, reintentar, fallidos, cancelados);
    }

    // ---------- Apertura ----------

    /// <summary>
    /// Alguien ha pedido el píxel de un correo. Devuelve falso si el token no existe, y aun así la
    /// pantalla tiene que responder con la imagen: contestar 404 a un token inventado le confirmaría a
    /// quien lo probara que los demás sí existen.
    /// </summary>
    public async Task<bool> AnotarAperturaAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var correo = await repositorio.PorTokenAsync(token, ct).ConfigureAwait(false);
        if (correo is null || !correo.Abierto(reloj))
        {
            return false;
        }

        // Solo la primera. Cinco líneas de «ha abierto el correo» no dicen más que una, y el recuento
        // queda en el propio correo para quien quiera mirarlo.
        await cronologia.ApuntarAperturaAsync(
            correo.ContactoId, $"Ha abierto «{correo.Asunto}»", ct).ConfigureAwait(false);

        return true;
    }

    public async Task<IReadOnlyList<FichaCorreo>> DeContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var correos = await repositorio.DeContactoAsync(contactoId, HistorialVisible, ct).ConfigureAwait(false);

        return correos
            .Select(c => new FichaCorreo(
                c.Id, c.Para, c.Asunto, c.Cuerpo, Texto(c.Estado), c.CreadoEn, c.EnviadoEn,
                c.UltimoFallo, c.Aperturas, c.PrimeraAperturaEn))
            .ToArray();
    }

    /// <summary>
    /// La dirección del contacto. <paramref name="usuarioId"/> **se pasa** y no se lee del contexto, y
    /// esto ya rompió una vez: leyéndolo del contexto, un envío desde un trabajo de fondo —donde no hay
    /// sesión— devolvía nulo, y el correo se rechazaba con «ese contacto no tiene una dirección válida».
    /// El contacto sí la tenía; lo que faltaba era la sesión. Un mensaje de error que acusa al dato
    /// equivocado cuesta más de encontrar que uno que no diga nada.
    /// </summary>
    private async Task<string?> DireccionAsync(Guid contactoId, Guid usuarioId, CancellationToken ct)
    {
        var suyos = await datos.DeAsync(contactoId, usuarioId, ct).ConfigureAwait(false);
        return suyos?.Correo;
    }

    private static string Texto(ParaQue paraQue) =>
        paraQue == ParaQue.Comercial ? "comercial" : "atender una solicitud";

    private static string Texto(EstadoCorreo estado) => estado switch
    {
        EstadoCorreo.Enviado => "enviado",
        EstadoCorreo.Fallido => "fallido",
        EstadoCorreo.Cancelado => "cancelado",
        _ => "en cola",
    };
}
