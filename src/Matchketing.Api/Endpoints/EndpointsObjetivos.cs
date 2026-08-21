using Matchketing.Api.Comun;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Objetivos.Aplicacion;

namespace Matchketing.Api.Endpoints;

public sealed record PeticionObjetivo(Guid UsuarioId, DateOnly Mes, decimal Importe);

public static class EndpointsObjetivos
{
    public static void MapearObjetivos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/objetivos").WithTags("Objetivos").RequireAuthorization();

        // Lo mío. **Sin permiso especial**: cualquiera puede ver su propio objetivo, y de hecho tiene
        // que poder verlo o no serviría de nada. El servicio solo mira el usuario de la sesión, así que
        // no hay forma de pedir el de otro por esta ruta.
        grupo.MapGet("/mio", async (ServicioObjetivos servicio, CancellationToken ct) =>
        {
            var mio = await servicio.MioAsync(ct).ConfigureAwait(false);

            // **204 cuando no hay objetivo**, no 404 y no 200 con el cuerpo vacío.
            //
            // No es una florituta de protocolo, son tres cosas distintas: 404 diría «esa ruta no
            // existe» y obligaría a la pantalla a distinguir «no hay objetivo» de «se ha roto algo»;
            // un 200 con el cuerpo vacío promete una representación y no la manda, que es lo que
            // devolvía `Results.Ok(null)`. 204 dice exactamente lo que pasa: la petición fue bien y no
            // hay nada que enseñar. El ayudante `api()` de la pantalla ya convierte cuerpo vacío en
            // nulo, así que el cliente no necesita saber nada de esto.
            return mio is null ? Results.NoContent() : Results.Ok(mio);
        })
        .WithSummary("Cómo va quien pregunta este mes. Nulo si no tiene objetivo puesto.");

        grupo.MapGet("/equipo", async (
            DateOnly? mes, ServicioObjetivos servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            // Ver los objetivos del equipo entero es mirar el trabajo de los demás, así que pide el
            // mismo permiso que gestionar personas.
            if (!contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.EquipoAsync(mes, ct).ConfigureAwait(false));
        })
        .WithSummary("El mes del equipo: una fila por persona que vende, con o sin objetivo.");

        grupo.MapGet("/personas/{id:guid}/historico", async (
            Guid id, ServicioObjetivos servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            // Cada uno puede ver el suyo; el de otro, solo quien gestiona personas.
            if (contexto.UsuarioId != id && !contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.HistoricoAsync(id, ct).ConfigureAwait(false));
        })
        .WithSummary("Qué se le pidió cada mes y qué hizo. Hasta un año.");

        grupo.MapPut(string.Empty, async (
            PeticionObjetivo p, ServicioObjetivos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            // `PUT` y no `POST`: fijar el objetivo de alguien para un mes es idempotente —el mismo
            // cuerpo dos veces deja lo mismo— y quien rellena la tabla del equipo no sabe ni le importa
            // cuáles existían ya.
            var r = await servicio.FijarAsync(p.UsuarioId, p.Mes, p.Importe, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { id = r.Valor.Id, mes = r.Valor.Mes, importe = r.Valor.Importe });
        })
        .WithSummary("Fija o cambia el objetivo de alguien para un mes. El de un mes pasado no se toca.");

        grupo.MapDelete("/personas/{id:guid}", async (
            Guid id, DateOnly mes, ServicioObjetivos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.QuitarAsync(id, mes, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Le quita el objetivo de ese mes. No es lo mismo que ponerlo a cero.");
    }
}
