using Matchketing.Api.Comun;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Webhooks.Aplicacion;
using Matchketing.Webhooks.Dominio;

namespace Matchketing.Api.Endpoints;

public sealed record PeticionWebhook(string? Url, string? Descripcion, IReadOnlyList<string>? Eventos);

public sealed record PeticionCambioWebhook(string? Descripcion, IReadOnlyList<string>? Eventos);

public static class EndpointsWebhooks
{
    public static void MapearWebhooks(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        // Todo el grupo pide `empresa.ajustes`. Un webhook saca datos de la empresa hacia fuera: no es
        // una pantalla de consulta, es una decisión de administración.
        var grupo = rutas.MapGroup("/webhooks").WithTags("Webhooks").RequireAuthorization();

        grupo.MapGet("/eventos", (IContextoEmpresa contexto) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            return Results.Ok(TiposEvento.Todos.Select(t => new { nombre = TiposEvento.Texto(t) }));
        })
        .WithSummary("El catálogo de eventos que se pueden escuchar.");

        grupo.MapGet(string.Empty, async (ServicioWebhooks servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.ListarAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("Los webhooks de la empresa. Nunca devuelve el secreto.");

        grupo.MapPost(string.Empty, async (
            PeticionWebhook p, ServicioWebhooks servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.CrearAsync(p.Url, p.Descripcion, p.Eventos, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            // El secreto se devuelve **una sola vez**, aquí. Si se pierde, se rota; no se recupera.
            return Results.Created($"/webhooks/{r.Valor.Suscripcion.Id}", new
            {
                id = r.Valor.Suscripcion.Id,
                secreto = r.Valor.Secreto,
                aviso = "Guarda el secreto ahora: no se puede volver a consultar, solo rotar.",
            });
        })
        .WithSummary("Da de alta un webhook y devuelve su secreto de firma, la única vez que se puede leer.");

        grupo.MapGet("/{id:guid}/entregas", async (
            Guid id, ServicioWebhooks servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.HistorialAsync(id, ct).ConfigureAwait(false);
            return r.Exito ? Results.Ok(r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("Los últimos intentos de este webhook. Es la pantalla que se mira cuando algo no llega.");

        grupo.MapPut("/{id:guid}", async (
            Guid id, PeticionCambioWebhook p, ServicioWebhooks servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.CambiarAsync(id, p.Descripcion, p.Eventos, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia la descripción y los eventos. La dirección no: para eso se crea otro.");

        grupo.MapPost("/{id:guid}/secreto", async (
            Guid id, ServicioWebhooks servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.RotarSecretoAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            // Rotar corta en seco: las entregas que salgan desde ahora van firmadas con el nuevo. Se
            // dice, porque quien lo rota tiene que ir a cambiarlo al otro lado **ya**.
            return Results.Ok(new
            {
                secreto = r.Valor,
                aviso = "Desde ahora se firma con este. Cámbialo en tu servidor: el anterior ya no vale.",
            });
        })
        .WithSummary("Secreto nuevo. El anterior deja de valer al momento.");

        grupo.MapPost("/{id:guid}/reactivar", async (
            Guid id, ServicioWebhooks servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.ReactivarAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Vuelve a encender un webhook que se apagó solo. No se reactiva por su cuenta.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioWebhooks servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.BorrarAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Lo borra. Las entregas que quedaran en cola se abandonan sin intentarse.");
    }
}
