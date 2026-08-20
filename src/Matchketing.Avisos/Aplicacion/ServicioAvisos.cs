using Matchketing.Avisos.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Avisos.Aplicacion;

/// <summary>
/// Los avisos al móvil: el empujón del viernes por la tarde.
///
/// Es la última pieza de la tesis del módulo Repaso. El repaso hace que cerrar la semana cueste dos
/// minutos; esto hace que uno **se acuerde** de hacerlo. Sin el aviso, el repaso lo hace quien ya era
/// ordenado, que es justo quien menos lo necesitaba.
///
/// Dos reglas que lo mantienen del lado de las cosas que no molestan:
///
/// * **Solo si hay algo que decidir.** Un aviso que dice «no tienes nada pendiente» es un aviso que
///   enseña a ignorar los avisos. Si la pila está vacía, no se manda nada.
/// * **Uno por semana como mucho.** El control es el último aviso mandado, no el calendario: si el
///   trabajo se ejecuta dos veces —dos instancias, un reintento— no llegan dos avisos.
/// </summary>
public sealed class ServicioAvisos(
    IRepositorioSuscripciones suscripciones,
    IConsultaPendientes pendientes,
    IEmisorAvisos emisor,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    /// <summary>A partir de cuántas decisiones merece la pena molestar a alguien.</summary>
    public const int MinimoParaAvisar = 3;

    public async Task<Resultado<SuscripcionAviso>> SuscribirAsync(
        string? endpoint, string? clavePublica, string? secreto, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId || contexto.UsuarioId is not { } usuarioId)
        {
            return Resultado.Fallo<SuscripcionAviso>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        // El navegador vuelve a mandar la misma suscripción cada vez que se abre la aplicación, y a
        // veces con las claves rotadas. Se actualiza en su sitio en vez de acumular filas: si no, al
        // mes hay diez suscripciones del mismo móvil y le llegan diez avisos.
        var conocida = endpoint is null ? null : await suscripciones.PorEndpointAsync(endpoint, ct).ConfigureAwait(false);
        if (conocida is not null)
        {
            var renovada = conocida.Renovar(clavePublica, secreto);
            return renovada.Fallido ? Resultado.Fallo<SuscripcionAviso>(renovada.Error!) : Resultado.Ok(conocida);
        }

        var creada = SuscripcionAviso.Crear(empresaId, usuarioId, endpoint, clavePublica, secreto, reloj);
        if (creada.Exito)
        {
            suscripciones.Anadir(creada.Valor);
        }

        return creada;
    }

    /// <summary>
    /// Quita un aparato. Es lo que se llama cuando la persona apaga los avisos, y tiene que funcionar
    /// aunque el endpoint ya no exista: quien dice «no quiero» no puede recibir un error.
    /// </summary>
    public async Task<Resultado> DesuscribirAsync(string? endpoint, CancellationToken ct = default)
    {
        var suscripcion = endpoint is null ? null : await suscripciones.PorEndpointAsync(endpoint, ct).ConfigureAwait(false);
        if (suscripcion is not null)
        {
            suscripciones.Quitar(suscripcion);
        }

        return Resultado.Ok();
    }

    public Task<IReadOnlyList<SuscripcionAviso>> MisAparatosAsync(CancellationToken ct = default) =>
        contexto.UsuarioId is { } usuarioId
            ? suscripciones.DeUsuarioAsync(usuarioId, ct)
            : Task.FromResult<IReadOnlyList<SuscripcionAviso>>([]);

    /// <summary>
    /// El aviso semanal de la empresa activa. Recorre a quien tenga decisiones pendientes y le manda
    /// uno a cada aparato suyo.
    ///
    /// Borra las suscripciones que el servicio de push declare muertas. No es limpieza opcional:
    /// insistir contra endpoints caducados es la forma de que un servicio de push empiece a limitar
    /// todo lo que mandamos.
    /// </summary>
    public async Task<ResumenAvisos> AvisarDelRepasoAsync(CancellationToken ct = default)
    {
        var porUsuario = await pendientes.PorUsuarioAsync(ct).ConfigureAwait(false);
        var todas = await suscripciones.DeLaEmpresaAsync(ct).ConfigureAwait(false);
        var ahora = reloj.AhoraUtc;

        int enviados = 0, borrados = 0, fallidos = 0;

        foreach (var suscripcion in todas)
        {
            ct.ThrowIfCancellationRequested();

            if (!porUsuario.TryGetValue(suscripcion.UsuarioId, out var cuantas) || cuantas < MinimoParaAvisar)
            {
                continue;
            }

            if (!suscripcion.LeTocaAviso(ahora))
            {
                continue;
            }

            switch (await emisor.EnviarAsync(suscripcion, Redactar(cuantas), ct).ConfigureAwait(false))
            {
                case ResultadoEnvio.Entregado:
                    suscripcion.Avisada(reloj);
                    enviados++;
                    break;

                case ResultadoEnvio.SuscripcionMuerta:
                    suscripciones.Quitar(suscripcion);
                    borrados++;
                    break;

                default:
                    fallidos++;
                    break;
            }
        }

        return new ResumenAvisos(enviados, borrados, fallidos);
    }

    /// <summary>
    /// El texto del aviso. Dice **cuántas** y **cuánto cuesta**, que son las dos cosas que deciden si
    /// alguien lo abre ahora o lo desliza. «Tienes tareas pendientes» no dice ninguna de las dos.
    /// </summary>
    private static Aviso Redactar(int cuantas)
    {
        var minutos = Math.Max(1, (int)Math.Round(cuantas * 4 / 60.0));
        var cuanto = minutos == 1 ? "un minuto" : $"unos {minutos} minutos";

        return new Aviso(
            "Cierra la semana",
            $"{cuantas} decisiones te separan de tenerlo al día. {char.ToUpperInvariant(cuanto[0])}{cuanto[1..]}.",
            "/?ir=repaso",
            cuantas);
    }
}
