using Matchketing.Api.Comun;
using Matchketing.Avisos.Aplicacion;
using Matchketing.Avisos.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;

namespace Matchketing.Api.Endpoints;

public sealed record PeticionSuscripcion(string? Endpoint, string? ClavePublica, string? Secreto);

public static class EndpointsAvisos
{
    public static void MapearAvisos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/avisos").WithTags("Avisos").RequireAuthorization();

        // La clave pública VAPID. Es pública de verdad —va dentro de cada suscripción— pero se sirve
        // con sesión porque solo la necesita quien va a suscribirse.
        grupo.MapGet("/clave", (ClavesVapid claves) => Results.Ok(new { clave = claves.Publica }))
            .WithSummary("La clave pública con la que el navegador se suscribe.");

        grupo.MapGet("/aparatos", async (ServicioAvisos servicio, CancellationToken ct) =>
        {
            var aparatos = await servicio.MisAparatosAsync(ct).ConfigureAwait(false);

            // El endpoint completo no se devuelve: es la credencial con la que se le puede mandar un
            // aviso a ese aparato, y no hace falta para nada en la pantalla.
            return Results.Ok(aparatos.Select(a => new
            {
                a.Id,
                servicio = new Uri(a.Endpoint).Host,
                a.CreadoEn,
                a.UltimoAvisoEn,
            }));
        })
        .WithSummary("Los aparatos de quien pregunta que tienen los avisos activados.");

        grupo.MapPost("/suscripcion", async (
            PeticionSuscripcion p, ServicioAvisos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaLeer))
            {
                return Results.Forbid();
            }

            var r = await servicio.SuscribirAsync(p.Endpoint, p.ClavePublica, p.Secreto, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Da de alta este aparato para los avisos. Es idempotente: el navegador la reenvía.");

        grupo.MapDelete("/suscripcion", async (
            string? endpoint, ServicioAvisos servicio, IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            // Nunca falla, aunque el endpoint no exista: quien dice «no quiero avisos» no puede recibir
            // un error por respuesta.
            await servicio.DesuscribirAsync(endpoint, ct).ConfigureAwait(false);
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Apaga los avisos en este aparato.");
    }
}
