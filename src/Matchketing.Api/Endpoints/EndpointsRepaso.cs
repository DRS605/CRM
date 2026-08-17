using Matchketing.Api.Comun;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Repaso.Aplicacion;
using Matchketing.Repaso.Dominio;

namespace Matchketing.Api.Endpoints;

public sealed record PeticionRespuesta(string? Clave, Respuesta Respuesta, int? Motivo);

public static class EndpointsRepaso
{
    public static void MapearRepaso(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/repaso").WithTags("Repaso").RequireAuthorization();

        grupo.MapGet(string.Empty, async (ServicioRepaso servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.PilaAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("La pila del repaso: qué hay que decidir, con las respuestas ya escritas.");

        grupo.MapPost("/responder", async (
            PeticionRespuesta p, ServicioRepaso servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.ResponderAsync(p.Clave, p.Respuesta, p.Motivo, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            // Un solo guardado para todo: el efecto de la respuesta y el apunte de que la pregunta queda
            // aparcada. Si fueran dos, un fallo entre medias dejaría la tarea cerrada y la pregunta
            // viva, o al contrario.
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(r.Valor);
        })
        .WithSummary("Contesta una pregunta. Hace todo lo que implica y la quita de la pila.");

        grupo.MapGet("/resumen", async (
            int? dias, ServicioRepaso servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.TareaLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.ResumenAsync(dias ?? 7, ct).ConfigureAwait(false));
        })
        .WithSummary("La semana de quien pregunta, contada para él. No es un cuadro de mando.");
    }
}
