using Matchketing.Api.Comun;
using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Match.Aplicacion;
using Matchketing.Nucleo.Comun;
using Matchketing.Tareas.Aplicacion;

namespace Matchketing.Api.Endpoints;

public static class EndpointsMatch
{
    public static void MapearMatch(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/match").WithTags("Match").RequireAuthorization();

        grupo.MapGet("/contactos/{id:guid}", async (
            Guid id, ServicioMatch servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            var r = await servicio.ObtenerAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                contactoId = r.Valor.ContactoId,
                match = r.Valor.Match,
                encaje = r.Valor.Encaje,
                momento = r.Valor.Momento,
                motivos = r.Valor.Motivos,
                explicacion = r.Valor.Explicacion,
                sinHistorico = r.Valor.SinHistorico,
            });
        })
        .WithSummary("Puntuación de un contacto, siempre con sus motivos. Sin motivos no hay número.");

        grupo.MapPost("/recalcular", async (
            ServicioMatch servicio, IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var cuantos = await servicio.RecalcularTodosAsync(ct).ConfigureAwait(false);
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { recalculados = cuantos });
        })
        .WithSummary("Recalcula toda la empresa. Lo mismo que hace el barrido nocturno.");

        grupo.MapGet("/contactos/{id:guid}/comercial", async (
            Guid id, ServicioMatch servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            var r = await servicio.ProponerComercialAsync(id, ct).ConfigureAwait(false);
            return r.Exito
                ? Results.Ok(new { usuarioId = r.Valor.UsuarioId, nombre = r.Valor.Nombre, puntos = r.Valor.Puntos, motivos = r.Valor.Motivos })
                : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("Qué comercial encaja mejor con este lead, y por qué.");

        grupo.MapPost("/contactos/{id:guid}/asignar", async (
            Guid id, ServicioMatch match, ServicioContactos contactos, ServicioTareas tareas,
            IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var propuesta = await match.ProponerComercialAsync(id, ct).ConfigureAwait(false);
            if (propuesta.Fallido)
            {
                return ResultadosHttp.Problema(propuesta.Error!);
            }

            var ficha = await contactos.AsignarPropietarioAsync(id, propuesta.Valor.UsuarioId, ct).ConfigureAwait(false);
            if (ficha.Fallido)
            {
                return ResultadosHttp.Problema(ficha.Error!);
            }

            // Queda escrito por qué le tocó a esa persona: una asignación sin explicación parece una
            // lotería, y entonces nadie se fía del reparto.
            await contactos.RegistrarActividadAsync(
                id, TipoActividad.Sistema, SentidoActividad.Interna,
                $"Asignado a {propuesta.Valor.Nombre}: {string.Join(", ", propuesta.Valor.Motivos)}.", null, ct).ConfigureAwait(false);

            // Y con la primera acción ya puesta: un lead asignado sin próximo paso no sirve de nada.
            tareas.Crear($"Primera llamada a {ficha.Valor.Nombre}", id, null, null, Tareas.Dominio.OrigenTarea.Automatica);

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { asignadoA = propuesta.Valor.Nombre, motivos = propuesta.Valor.Motivos });
        })
        .WithSummary("Asigna el contacto al comercial con mejor match y le crea la primera llamada.");
    }
}
