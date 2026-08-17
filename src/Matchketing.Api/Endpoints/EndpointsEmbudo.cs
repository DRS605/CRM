using Matchketing.Api.Comun;
using Matchketing.Api.Contratos;
using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Embudo.Aplicacion;
using Matchketing.Embudo.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Match.Aplicacion;
using Matchketing.Nucleo.Comun;

namespace Matchketing.Api.Endpoints;

public static class EndpointsEmbudo
{
    /// <summary>Texto legible del motivo, para la cronología del contacto.</summary>
    private static string Explicar(MotivoPerdida motivo) => motivo switch
    {
        MotivoPerdida.Precio => "por precio",
        MotivoPerdida.Plazo => "por plazo",
        MotivoPerdida.Competencia => "se la lleva la competencia",
        MotivoPerdida.NoEraElMomento => "no era el momento",
        MotivoPerdida.NoContesta => "no contesta",
        _ => "por otro motivo",
    };

    public static void MapearEmbudo(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var tablero = rutas.MapGroup("/embudo").WithTags("Embudo").RequireAuthorization();

        tablero.MapGet("/tablero", async (Guid? embudoId, ServicioEmbudo servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.OportunidadLeer))
            {
                return Results.Forbid();
            }

            var t = await servicio.TableroAsync(embudoId, ct).ConfigureAwait(false);
            return t is null
                ? Results.NotFound(new { codigo = "embudo.no_encontrado", mensaje = "Esta empresa no tiene embudo." })
                : Results.Ok(t);
        })
        .WithSummary("Tablero con las columnas, sus sumas, la previsión ponderada y las estancadas.");

        var grupo = rutas.MapGroup("/oportunidades").WithTags("Embudo").RequireAuthorization();

        grupo.MapPost(string.Empty, async (
            PeticionOportunidad p, ServicioEmbudo servicio, ServicioContactos contactos,
            ServicioMatch match, IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.OportunidadGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.CrearAsync(p.ContactoId, p.CuentaId, p.Titulo, p.Importe, p.EtapaId, p.PrevistaCierre, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            // La oportunidad también es algo que le ha pasado al contacto: va a su cronología.
            await contactos.RegistrarActividadAsync(
                p.ContactoId, TipoActividad.Sistema, SentidoActividad.Interna,
                $"Nueva oportunidad: «{r.Valor.Titulo}» por {Castellano.Euros(r.Valor.Importe)}.", null, ct).ConfigureAwait(false);

            await match.RegistrarSenalAsync(p.ContactoId, Match.Dominio.TipoSenal.OportunidadCreada, ct).ConfigureAwait(false);

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/oportunidades/{r.Valor.Id}", new { id = r.Valor.Id });
        })
        .WithSummary("Crea una oportunidad en la primera etapa del embudo.");

        grupo.MapPut("/{id:guid}", async (
            Guid id, PeticionActualizarOportunidad p, ServicioEmbudo servicio,
            IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.OportunidadGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.ActualizarAsync(id, p.Titulo, p.Importe, p.PrevistaCierre, p.PropietarioId, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Actualiza una oportunidad abierta.");

        grupo.MapPost("/{id:guid}/mover", async (
            Guid id, PeticionMover p, ServicioEmbudo servicio,
            IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.OportunidadGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.MoverAsync(id, p.EtapaId, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia de etapa y reinicia el contador de estancamiento.");

        grupo.MapPost("/{id:guid}/ganar", async (
            Guid id, ServicioEmbudo servicio, ServicioContactos contactos,
            IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.OportunidadGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.GanarAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await contactos.RegistrarActividadAsync(
                r.Valor.ContactoId, TipoActividad.Sistema, SentidoActividad.Interna,
                $"Oportunidad ganada: «{r.Valor.Titulo}» por {Castellano.Euros(r.Valor.Importe)}.", null, ct).ConfigureAwait(false);

            // Quien compra deja de ser un lead. Con ALXOR Core conectado, aquí nacería el presupuesto.
            await contactos.CambiarEstadoAsync(r.Valor.ContactoId, EstadoContacto.Cliente, ct).ConfigureAwait(false);

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Da la oportunidad por ganada y marca al contacto como cliente.");

        grupo.MapPost("/{id:guid}/perder", async (
            Guid id, PeticionPerder p, ServicioEmbudo servicio, ServicioContactos contactos,
            IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.OportunidadGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.PerderAsync(id, p.Motivo, p.Detalle, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await contactos.RegistrarActividadAsync(
                r.Valor.ContactoId, TipoActividad.Sistema, SentidoActividad.Interna,
                $"Oportunidad perdida: «{r.Valor.Titulo}», {Explicar(p.Motivo!.Value)}.", null, ct).ConfigureAwait(false);

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Da la oportunidad por perdida. El motivo es obligatorio.");
    }
}
