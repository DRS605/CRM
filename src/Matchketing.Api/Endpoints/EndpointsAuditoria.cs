using Matchketing.Auditoria.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;

namespace Matchketing.Api.Endpoints;

public static class EndpointsAuditoria
{
    public static void MapearAuditoria(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/auditoria").WithTags("Auditoría").RequireAuthorization();

        // Solo lectura, y solo para quien puede tocar los ajustes de la empresa. No hay POST, PUT ni
        // DELETE: el registro se escribe solo desde dentro, como efecto de las operaciones auditadas.
        grupo.MapGet(string.Empty, async (
            int? cuantos, IRegistradorAuditoria auditoria, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            return Results.Ok(await auditoria.UltimosAsync(cuantos ?? 100, ct).ConfigureAwait(false));
        })
        .WithSummary("Últimas acciones críticas de la empresa: quién, qué, cuándo.");
    }
}
