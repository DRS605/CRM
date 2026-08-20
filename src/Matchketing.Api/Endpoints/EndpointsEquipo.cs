using Matchketing.Api.Comun;
using Matchketing.Api.Contratos;
using Matchketing.Auditoria.Aplicacion;
using Matchketing.Auditoria.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Organizacion.Aplicacion;
using Matchketing.Persistencia;

namespace Matchketing.Api.Endpoints;

public static class EndpointsEquipo
{
    public static void MapearEquipo(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        // Todo el grupo pide `usuario.gestionar`. Es el permiso que llevaba once módulos existiendo sin
        // que nadie lo comprobara, porque no había ningún endpoint que gestionara usuarios.
        var grupo = rutas.MapGroup("/equipo").WithTags("Equipo").RequireAuthorization();

        grupo.MapGet(string.Empty, async (
            ServicioEquipo servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (contexto.EmpresaId is not { } empresaId)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            // Ver el equipo no pide `usuario.gestionar`: un comercial necesita saber a quién puede
            // asignarle un lead y quién lleva su zona. Lo que pide permiso es **cambiarlo**.
            var equipo = await servicio.EquipoAsync(empresaId, ct).ConfigureAwait(false);
            var puedeGestionar = contexto.Tiene(Permisos.UsuarioGestionar);

            return Results.Ok(new
            {
                yo = contexto.UsuarioId,
                puedeGestionar,
                miembros = equipo.Select(m => new
                {
                    id = m.MembresiaId,
                    usuarioId = m.UsuarioId,
                    m.Nombre,
                    m.Email,
                    rol = (int)m.Rol,
                    rolTexto = TextosRol.De(m.Rol),
                    m.Activa,
                    m.Zonas,
                    m.UltimoAccesoEn,
                }),

                // Las invitaciones pendientes solo las ve quien puede gestionarlas: son direcciones de
                // correo de gente que todavía no ha entrado.
                invitaciones = puedeGestionar
                    ? (await servicio.PendientesAsync(empresaId, ct).ConfigureAwait(false))
                        .Select(i => new { i.Id, i.Email, rolTexto = TextosRol.De(i.Rol), i.CaducaEn })
                    : [],
            });
        })
        .WithSummary("El equipo de la empresa, con sus roles y sus zonas.");

        grupo.MapPost("/invitaciones", async (
            PeticionInvitacion p, ServicioEquipo servicio, IRegistradorAuditoria auditoria,
            IContextoEmpresa contexto, IUnidadDeTrabajo unidad, IConfiguration config, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            if (contexto.EmpresaId is not { } empresaId || contexto.UsuarioId is not { } quien)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            var r = await servicio.InvitarAsync(empresaId, p.Email, p.Rol, quien, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            // Se audita **qué rol** se ofreció, no a quién: el correo de la persona invitada es un dato
            // personal y el registro no los guarda.
            auditoria.Registrar("invitacion", r.Valor.Invitacion.Id, Acciones.EquipoInvitado, new { rol = TextosRol.De(p.Rol) });
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            // El enlace se devuelve **una sola vez**, como el secreto de un webhook: de la invitación
            // solo queda guardada su huella, así que esto no se puede volver a preguntar.
            var baseUrl = config["Baja:UrlBase"]?.TrimEnd('/');
            return Results.Ok(new
            {
                r.Valor.Invitacion.Id,
                r.Valor.Invitacion.Email,
                rolTexto = TextosRol.De(r.Valor.Invitacion.Rol),
                r.Valor.Invitacion.CaducaEn,
                enlace = $"{baseUrl ?? string.Empty}/?invitacion={r.Valor.Token}",
                token = r.Valor.Token,
            });
        })
        .WithSummary("Invita a alguien y devuelve el enlace. El enlace solo se puede ver aquí, una vez.");

        grupo.MapDelete("/invitaciones/{id:guid}", async (
            Guid id, ServicioEquipo servicio, IContextoEmpresa contexto, IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            if (contexto.EmpresaId is not { } empresaId)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            var r = await servicio.RetirarInvitacionAsync(empresaId, id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Retira una invitación que aún no se ha usado. El enlace deja de valer.");

        grupo.MapPut("/{id:guid}/rol", async (
            Guid id, PeticionRol p, ServicioEquipo servicio, IRegistradorAuditoria auditoria,
            IContextoEmpresa contexto, IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            if (contexto.EmpresaId is not { } empresaId || contexto.UsuarioId is not { } quien)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            var r = await servicio.CambiarRolAsync(empresaId, id, p.Rol, quien, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            auditoria.Registrar("membresia", id, Acciones.EquipoRolCambiado, new { rol = TextosRol.De(p.Rol) });
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia el rol de alguien. No el propio, y nunca deja la empresa sin propietario.");

        grupo.MapPut("/{id:guid}/zonas", async (
            Guid id, PeticionZonas p, ServicioEquipo servicio, IContextoEmpresa contexto,
            IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            if (contexto.EmpresaId is not { } empresaId)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            var r = await servicio.FijarZonasAsync(empresaId, id, p.Zonas, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Las provincias que cubre esa persona. Es el primer factor del reparto de leads.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioEquipo servicio, IRegistradorAuditoria auditoria, IContextoEmpresa contexto,
            IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.UsuarioGestionar))
            {
                return Results.Forbid();
            }

            if (contexto.EmpresaId is not { } empresaId || contexto.UsuarioId is not { } quien)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            var r = await servicio.QuitarAsync(empresaId, id, quien, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            auditoria.Registrar("membresia", id, Acciones.EquipoAccesoRetirado, null);
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Le quita el acceso a la empresa. No borra a la persona ni sus datos.");

        MapearInvitacionesPublicas(rutas);
    }

    /// <summary>
    /// El otro lado del enlace, sin sesión: quien lo abre todavía no está dentro de la empresa —puede
    /// que ni tenga cuenta—, así que estos dos endpoints no pueden pedir autenticación.
    ///
    /// La empresa la dice el propio token, igual que en el enlace de baja y en el píxel de apertura, y
    /// se fija **antes** de tocar la base de datos: sin eso la RLS de PostgreSQL no devuelve la fila y
    /// la invitación no se encontraría nunca.
    /// </summary>
    private static void MapearInvitacionesPublicas(IEndpointRouteBuilder rutas)
    {
        var publico = rutas.MapGroup("/invitaciones").WithTags("Invitaciones (público)");

        publico.MapGet("/{token}", async (
            string token, ServicioEquipo servicio, ServicioEmpresas empresas, ContextoMatchketing bd,
            IContextoEmpresaPublico contextoPublico, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(bd);
            ArgumentNullException.ThrowIfNull(contextoPublico);
            ArgumentNullException.ThrowIfNull(empresas);

            if (!await FijarEmpresaDelTokenAsync(token, bd, contextoPublico, ct).ConfigureAwait(false))
            {
                return NoVale();
            }

            var abierta = await servicio.AbrirAsync(token, ct).ConfigureAwait(false);
            if (abierta.Fallido)
            {
                return ResultadosHttp.Problema(abierta.Error!);
            }

            var empresa = await empresas.ObtenerAsync(abierta.Valor.EmpresaId, ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                abierta.Valor.Email,
                rolTexto = TextosRol.De(abierta.Valor.Rol),
                abierta.Valor.YaTieneCuenta,
                empresa = empresa.Exito ? empresa.Valor.Nombre : null,
            });
        })
        .AllowAnonymous()
        .WithSummary("Qué hay detrás del enlace: qué empresa, qué rol y si ya hay cuenta con ese correo.");

        publico.MapPost("/{token}", async (
            string token, PeticionAceptar p, ServicioEquipo servicio, ServicioIdentidad identidad,
            ServicioEmpresas empresas, ContextoMatchketing bd, IContextoEmpresaPublico contextoPublico,
            IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(bd);
            ArgumentNullException.ThrowIfNull(contextoPublico);
            ArgumentNullException.ThrowIfNull(identidad);

            if (!await FijarEmpresaDelTokenAsync(token, bd, contextoPublico, ct).ConfigureAwait(false))
            {
                return NoVale();
            }

            var abierta = await servicio.AbrirAsync(token, ct).ConfigureAwait(false);
            if (abierta.Fallido)
            {
                return ResultadosHttp.Problema(abierta.Error!);
            }

            var aceptada = await servicio.AceptarAsync(token, p.Nombre, p.Contrasena, ct).ConfigureAwait(false);
            if (aceptada.Fallido)
            {
                return ResultadosHttp.Problema(aceptada.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            // Y se entra directamente, con la empresa ya activa: aceptar una invitación y tener que
            // buscar la pantalla de acceso después sería raro dos veces.
            var empresa = await empresas.ObtenerAsync(abierta.Valor.EmpresaId, ct).ConfigureAwait(false);
            var sesion = await identidad.SeleccionarEmpresaAsync(
                aceptada.Valor.Id, abierta.Valor.EmpresaId, empresa.Exito ? empresa.Valor.Nombre : string.Empty, ct)
                .ConfigureAwait(false);

            return sesion.Exito ? Results.Ok(sesion.Valor) : ResultadosHttp.Problema(sesion.Error!);
        })
        .AllowAnonymous()

        // Con techo, porque este endpoint comprueba una contraseña cuando la cuenta ya existe y
        // serviría igual de bien para adivinarla. El cubo es **la invitación**, no la IP: lo que se
        // puede adivinar aquí es la contraseña de una sola cuenta, y así una oficina entera dándose de
        // alta no se estorba a sí misma. Ver el porqué completo en `Program.cs`.
        .RequireRateLimiting("invitacion")
        .WithSummary("Acepta la invitación y devuelve la sesión con la empresa ya activa.");
    }

    /// <summary>
    /// Saca la empresa del token y la fija en la petición. Devuelve `false` si el token no tiene forma
    /// de token: así el mensaje es el mismo que para una invitación caducada o inventada, y no se puede
    /// distinguir «este token no existe» de «este token existió».
    /// </summary>
    private static async Task<bool> FijarEmpresaDelTokenAsync(
        string? token, ContextoMatchketing bd, IContextoEmpresaPublico contextoPublico, CancellationToken ct)
    {
        if (Invitacion.EmpresaDelToken(token) is not { } empresaId)
        {
            return false;
        }

        contextoPublico.FijarEmpresa(empresaId);
        await bd.ReaplicarEmpresaAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static IResult NoVale() => Results.NotFound(new
    {
        codigo = "invitacion.no_vale",
        mensaje = "Esta invitación ya no vale. Pide otra a quien te invitó.",
    });
}
