using Matchketing.Api.Comun;
using Matchketing.Api.Contratos;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Tareas.Aplicacion;

namespace Matchketing.Api.Endpoints;

public static class EndpointsTareas
{
    public static void MapearTareas(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        rutas.MapGet("/hoy", async (ServicioTareas servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.HoyAsync(ct).ConfigureAwait(false));
        })
        .WithTags("Hoy").RequireAuthorization()
        .WithSummary("La pila del día: lo vencido, lo parado y lo que no tiene próximo paso.");

        var grupo = rutas.MapGroup("/tareas").WithTags("Tareas").RequireAuthorization();

        grupo.MapGet(string.Empty, async (bool? soloPendientes, ServicioTareas servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.ListarAsync(soloPendientes ?? true, ct).ConfigureAwait(false));
        })
        .WithSummary("Lista de tareas.");

        grupo.MapPost(string.Empty, async (
            PeticionTarea p, ServicioTareas servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaGestionar))
            {
                return Results.Forbid();
            }

            var r = servicio.Crear(p.Titulo, p.ContactoId, p.OportunidadId, p.VenceEl);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/tareas/{r.Valor.Id}", new { id = r.Valor.Id });
        })
        .WithSummary("Crea una tarea. Sin fecha, vence hoy.");

        grupo.MapPost("/{id:guid}/completar", async (
            Guid id, ServicioTareas servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.CompletarAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Marca la tarea como hecha.");

        grupo.MapPost("/{id:guid}/descartar", async (
            Guid id, ServicioTareas servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.DescartarAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Descarta la tarea. Se guarda igual: enseña tanto como hacerla.");

        grupo.MapPost("/{id:guid}/aplazar", async (
            Guid id, PeticionAplazar p, ServicioTareas servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.AplazarAsync(id, p.Hasta, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Aplaza la tarea. Exige fecha, y posterior a hoy.");
    }
}
