using Matchketing.Api.Comun;
using Matchketing.Campanias.Aplicacion;
using Matchketing.Campanias.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;

namespace Matchketing.Api.Endpoints;

public sealed record PeticionSegmento(
    string? Nombre,
    EstadoBuscado? Estado,
    string? Provincia,
    string? Origen,
    int? MatchMinimo,
    int? SinActividadDias,
    Guid? EtapaId);

public sealed record PeticionCampania(string? Nombre, Guid SegmentoId, Guid PlantillaId);

public static class EndpointsCampanias
{
    public static void MapearCampanias(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        MapearSegmentos(rutas);
        MapearCampaniasPropias(rutas);
    }

    private static void MapearSegmentos(IEndpointRouteBuilder rutas)
    {
        var grupo = rutas.MapGroup("/segmentos").WithTags("Campañas").RequireAuthorization();

        grupo.MapGet(string.Empty, async (
            ServicioCampanias servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.CampaniaLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.SegmentosAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("Los segmentos, con cuántos contactos tiene cada uno **ahora mismo**.");

        grupo.MapGet("/{id:guid}/previa", async (
            Guid id, ServicioCampanias servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.CampaniaLeer))
            {
                return Results.Forbid();
            }

            var r = await servicio.VistaPreviaAsync(id, ct).ConfigureAwait(false);
            return r.Exito ? Results.Ok(r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("A quién le va a llegar: el total y una muestra con nombres. Sin mandar nada.");

        grupo.MapPost(string.Empty, async (
            PeticionSegmento p, ServicioCampanias servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.CampaniaGestionar))
            {
                return Results.Forbid();
            }

            var criterios = CriteriosSegmento.Crear(
                p.Estado, p.Provincia, p.Origen, p.MatchMinimo, p.SinActividadDias, p.EtapaId);

            if (criterios.Fallido)
            {
                return ResultadosHttp.Problema(criterios.Error!);
            }

            var r = await servicio.CrearSegmentoAsync(p.Nombre, criterios.Valor, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/segmentos/{r.Valor.Id}", new { id = r.Valor.Id });
        })
        .WithSummary("Crea un segmento. Sin ningún criterio se rechaza: un segmento vacío sería toda la base de datos.");

        grupo.MapPut("/{id:guid}", async (
            Guid id, PeticionSegmento p, ServicioCampanias servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.CampaniaGestionar))
            {
                return Results.Forbid();
            }

            var criterios = CriteriosSegmento.Crear(
                p.Estado, p.Provincia, p.Origen, p.MatchMinimo, p.SinActividadDias, p.EtapaId);

            if (criterios.Fallido)
            {
                return ResultadosHttp.Problema(criterios.Error!);
            }

            var r = await servicio.CambiarSegmentoAsync(id, p.Nombre, criterios.Valor, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia un segmento. No toca las campañas ya lanzadas: cada una guardó su audiencia.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioCampanias servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.CampaniaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.BorrarSegmentoAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Lo borra, si ninguna campaña lo ha usado.");
    }

    private static void MapearCampaniasPropias(IEndpointRouteBuilder rutas)
    {
        var grupo = rutas.MapGroup("/campanias").WithTags("Campañas").RequireAuthorization();

        grupo.MapGet(string.Empty, async (
            ServicioCampanias servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.CampaniaLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.CampaniasAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("Las campañas, las lanzadas primero.");

        grupo.MapGet("/{id:guid}", async (
            Guid id, ServicioCampanias servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.CampaniaLeer))
            {
                return Results.Forbid();
            }

            var r = await servicio.DetalleAsync(id, ct).ConfigureAwait(false);
            return r.Exito ? Results.Ok(r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("Qué pasó: a cuántos llegó, a cuántos no, y por qué no.");

        grupo.MapPost(string.Empty, async (
            PeticionCampania p, ServicioCampanias servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.CampaniaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.CrearAsync(p.Nombre, p.SegmentoId, p.PlantillaId, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/campanias/{r.Valor.Id}", new { id = r.Valor.Id });
        })
        .WithSummary("Crea la campaña en borrador. La plantilla tiene que ser comercial.");

        grupo.MapPut("/{id:guid}", async (
            Guid id, PeticionCampania p, ServicioCampanias servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.CampaniaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.CambiarAsync(id, p.Nombre, p.SegmentoId, p.PlantillaId, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia un borrador. Una campaña lanzada no se edita.");

        grupo.MapPost("/{id:guid}/lanzar", async (
            Guid id, ServicioCampanias servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.CampaniaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.LanzarAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            // 202, no 200. Lanzar no manda nada: congela a quién se le va a mandar y deja la campaña
            // enviando. Decir 200 sería decir «hecho», y lo que hay es una campaña empezada.
            return Results.Accepted($"/campanias/{id}", new
            {
                estado = "enviando",
                destinatarios = r.Valor.Destinatarios,
            });
        })
        .WithSummary("La lanza: congela la audiencia. Los correos se encolan por lotes, comprobando el permiso de cada uno.");

        grupo.MapPost("/{id:guid}/detener", async (
            Guid id, ServicioCampanias servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.CampaniaGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.DetenerAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Deja de encolar. Lo que ya estaba en el buzón de salida sale igual.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioCampanias servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.CampaniaGestionar))
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
        .WithSummary("Borra un borrador. Una campaña lanzada no se borra: es la prueba de a quién se le escribió.");
    }
}
