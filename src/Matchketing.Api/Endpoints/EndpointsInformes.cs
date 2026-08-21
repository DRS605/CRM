using Matchketing.Identidad.Dominio;
using Matchketing.Informes.Aplicacion;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Api.Endpoints;

public static class EndpointsInformes
{
    /// <summary>Atajos de periodo en lenguaje de persona, que es como se piden los informes.</summary>
    private static Periodo Resolver(string? periodo, DateOnly? desde, DateOnly? hasta, IReloj reloj)
    {
        var hoy = HorasLaborables.DiaDeTrabajo(reloj.AhoraUtc);

        return periodo?.ToLowerInvariant() switch
        {
            "mes" => Periodo.UltimosDias(30, hoy),
            "trimestre" => Periodo.UltimosDias(90, hoy),
            "año" or "ano" => Periodo.UltimosDias(365, hoy),
            "todo" => Periodo.Todo,
            _ => new Periodo(desde, hasta),
        };
    }

    public static void MapearInformes(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/informes").WithTags("Informes").RequireAuthorization();

        grupo.MapGet("/embudo", async (
            string? periodo, DateOnly? desde, DateOnly? hasta,
            ServicioInformes servicio, IContextoEmpresa contexto, IReloj reloj, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.InformeLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.EmbudoAsync(Resolver(periodo, desde, hasta, reloj), ct).ConfigureAwait(false));
        })
        .WithSummary("Embudo: qué hay por etapa, conversión, previsión, ganado y perdido.");

        grupo.MapGet("/motivos-perdida", async (
            string? periodo, DateOnly? desde, DateOnly? hasta,
            ServicioInformes servicio, IContextoEmpresa contexto, IReloj reloj, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.InformeLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.MotivosAsync(Resolver(periodo, desde, hasta, reloj), ct).ConfigureAwait(false));
        })
        .WithSummary("Por qué se pierde, en orden. Con el total ganado para comparar.");

        grupo.MapGet("/embudo.csv", async (
            string? periodo, DateOnly? desde, DateOnly? hasta,
            ServicioInformes servicio, IContextoEmpresa contexto, IReloj reloj, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.DatosExportar))
            {
                return Results.Forbid();
            }

            var csv = await servicio.EmbudoCsvAsync(Resolver(periodo, desde, hasta, reloj), ct).ConfigureAwait(false);
            return Descargar(csv, "embudo.csv");
        })
        .WithSummary("El informe de embudo en CSV, listo para Excel en español.");

        grupo.MapGet("/motivos-perdida.csv", async (
            string? periodo, DateOnly? desde, DateOnly? hasta,
            ServicioInformes servicio, IContextoEmpresa contexto, IReloj reloj, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.DatosExportar))
            {
                return Results.Forbid();
            }

            var csv = await servicio.MotivosCsvAsync(Resolver(periodo, desde, hasta, reloj), ct).ConfigureAwait(false);
            return Descargar(csv, "motivos-perdida.csv");
        })
        .WithSummary("Los motivos de pérdida en CSV.");
    }

    /// <summary>
    /// Con BOM: sin él, Excel en Windows se come los acentos y el cliente ve «Hosteler¡a».
    /// </summary>
    private static IResult Descargar(string csv, string nombre)
    {
        var bytes = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(csv))
            .ToArray();

        return Results.File(bytes, "text/csv; charset=utf-8", nombre);
    }
}
