using System.Security.Claims;
using Matchketing.Api.Comun;
using Matchketing.Api.Contratos;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Organizacion.Aplicacion;

namespace Matchketing.Api.Endpoints;

public static class EndpointsIdentidad
{
    public static void MapearIdentidad(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/auth").WithTags("Identidad");

        grupo.MapPost("/registro", async (PeticionRegistro p, ServicioIdentidad servicio, CancellationToken ct) =>
        {
            var r = await servicio.RegistrarAsync(p.Email, p.Contrasena, p.Nombre, ct).ConfigureAwait(false);
            return r.Exito ? Results.Created("/auth/yo", r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("Crea una cuenta y devuelve la sesión ya iniciada.");

        grupo.MapPost("/login", async (PeticionLogin p, ServicioIdentidad servicio, CancellationToken ct) =>
        {
            var r = await servicio.IniciarSesionAsync(p.Email, p.Contrasena, ct).ConfigureAwait(false);
            return r.Exito ? Results.Ok(r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .RequireRateLimiting("acceso")
        .WithSummary("Inicia sesión. Limitado a 20 intentos cada cinco minutos por IP.");

        grupo.MapPost("/contrasena", async (
            PeticionCambioContrasena p, ClaimsPrincipal quien, ServicioIdentidad servicio, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(quien);

            var usuarioId = Guid.Parse(quien.FindFirstValue(Claims.UsuarioId)!);
            var r = await servicio.CambiarContrasenaAsync(usuarioId, p.Actual, p.Nueva, ct).ConfigureAwait(false);
            return r.Exito ? Results.NoContent() : ResultadosHttp.Problema(r.Error!);
        })
        .RequireAuthorization()
        // El mismo límite que el acceso: este endpoint también comprueba una contraseña, así que
        // serviría igual de bien para adivinarla si se dejara sin techo.
        .RequireRateLimiting("acceso")
        .WithSummary("Cambia la contraseña. Hay que dar la actual.");

        grupo.MapGet("/yo", async (ClaimsPrincipal quien, ServicioIdentidad identidad, ServicioEmpresas empresas, CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(quien.FindFirstValue(Claims.UsuarioId)!);
            var membresias = await identidad.MembresiasDeAsync(usuarioId, ct).ConfigureAwait(false);
            var fichas = await empresas.DeIdsAsync(membresias.Select(m => m.EmpresaId).ToArray(), ct).ConfigureAwait(false);

            var mias = membresias
                .Join(fichas, m => m.EmpresaId, e => e.Id, (m, e) => new EmpresaDeUsuario(e.Id, e.Nombre, m.Rol))
                .OrderBy(e => e.Nombre)
                .ToArray();

            return Results.Ok(new
            {
                id = usuarioId,
                nombre = quien.FindFirstValue(ClaimTypes.Name),
                email = quien.FindFirstValue("email"),
                empresas = mias,
            });
        })
        .RequireAuthorization()
        .WithSummary("Perfil y empresas donde el usuario tiene membresía activa.");
    }
}
