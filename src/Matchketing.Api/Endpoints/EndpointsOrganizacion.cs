using System.Security.Claims;
using Matchketing.Api.Comun;
using Matchketing.Api.Contratos;
using Matchketing.Auditoria.Aplicacion;
using Matchketing.Auditoria.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Embudo.Aplicacion;
using Matchketing.Organizacion.Aplicacion;

namespace Matchketing.Api.Endpoints;

public static class EndpointsOrganizacion
{
    public static void MapearOrganizacion(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/empresas").WithTags("Organización").RequireAuthorization();

        grupo.MapPost(string.Empty, async (
            PeticionEmpresa p,
            ClaimsPrincipal quien,
            ServicioEmpresas empresas,
            ServicioIdentidad identidad,
            ServicioEmbudo embudo,
            IUnidadDeTrabajo unidad,
            IReloj reloj,
            CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(quien.FindFirstValue(Claims.UsuarioId)!);

            var creada = empresas.Crear(p.Nombre, p.Nif, p.Provincia);
            if (creada.Fallido)
            {
                return ResultadosHttp.Problema(creada.Error!);
            }

            // Quien crea la empresa es su propietario. Empresa y membresía se guardan en la misma
            // transacción: una empresa sin propietario sería una empresa a la que nadie puede entrar.
            identidad.AnadirMembresia(Membresia.Crear(usuarioId, creada.Valor.Id, Rol.Propietario, reloj));

            // La empresa nace con su embudo de cinco etapas: nadie debería tener que montarlo antes
            // de poder apuntar su primera venta.
            embudo.CrearEmbudoPorDefecto(creada.Valor.Id);

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            var sesion = await identidad.SeleccionarEmpresaAsync(usuarioId, creada.Valor.Id, creada.Valor.Nombre, ct).ConfigureAwait(false);
            return sesion.Exito
                ? Results.Created($"/empresas/{creada.Valor.Id}", sesion.Valor)
                : ResultadosHttp.Problema(sesion.Error!);
        })
        .WithSummary("Crea una empresa, hace propietario a quien la crea y devuelve el token con ella activa.");

        grupo.MapPost("/{id:guid}/seleccionar", async (
            Guid id,
            ClaimsPrincipal quien,
            ServicioEmpresas empresas,
            ServicioIdentidad identidad,
            CancellationToken ct) =>
        {
            var usuarioId = Guid.Parse(quien.FindFirstValue(Claims.UsuarioId)!);

            var empresa = await empresas.ObtenerAsync(id, ct).ConfigureAwait(false);
            if (empresa.Fallido)
            {
                return ResultadosHttp.Problema(empresa.Error!);
            }

            var sesion = await identidad.SeleccionarEmpresaAsync(usuarioId, id, empresa.Valor.Nombre, ct).ConfigureAwait(false);
            return sesion.Exito ? Results.Ok(sesion.Valor) : ResultadosHttp.Problema(sesion.Error!);
        })
        .WithSummary("Emite un token nuevo con esa empresa como activa.");

        grupo.MapGet("/activa", async (ServicioEmpresas empresas, Matchketing.Nucleo.Comun.IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (contexto.EmpresaId is not { } id)
            {
                return Results.NoContent();
            }

            var empresa = await empresas.ObtenerAsync(id, ct).ConfigureAwait(false);
            return empresa.Exito
                ? Results.Ok(new
                {
                    id = empresa.Valor.Id,
                    nombre = empresa.Valor.Nombre,
                    nif = empresa.Valor.Nif,
                    provincia = empresa.Valor.Provincia,
                    pesoEncaje = empresa.Valor.PesoEncaje,
                    horasRebote = empresa.Valor.HorasRebote,
                    mesesRetencionLeads = empresa.Valor.MesesRetencionLeads,
                })
                : ResultadosHttp.Problema(empresa.Error!);
        })
        .WithSummary("Datos y ajustes de la empresa activa.");

        grupo.MapPut("/activa/ajustes-match", async (
            PeticionAjustesMatch p,
            ServicioEmpresas empresas,
            IRegistradorAuditoria auditoria,
            Matchketing.Nucleo.Comun.IContextoEmpresa contexto,
            IUnidadDeTrabajo unidad,
            CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            if (contexto.EmpresaId is not { } id)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            var r = await empresas.AjustarMatchAsync(id, p.PesoEncaje, p.HorasRebote, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            auditoria.Registrar("empresa", id, Acciones.AjustesCambiados, new { p.PesoEncaje, p.HorasRebote });
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Ajusta el peso del Encaje y las horas de rebote de leads.");

        grupo.MapPut("/activa/ajustes-retencion", async (
            PeticionAjustesRetencion p,
            ServicioEmpresas empresas,
            IRegistradorAuditoria auditoria,
            Matchketing.Nucleo.Comun.IContextoEmpresa contexto,
            IUnidadDeTrabajo unidad,
            CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            if (contexto.EmpresaId is not { } id)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            var r = await empresas.AjustarRetencionAsync(id, p.MesesRetencionLeads, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            // Este ajuste decide cuándo se borran datos de gente. Cambiarlo se audita siempre.
            auditoria.Registrar("empresa", id, Acciones.AjustesCambiados, new { p.MesesRetencionLeads });
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Ajusta el plazo de conservación de leads que no llegaron a nada.");
    }
}
