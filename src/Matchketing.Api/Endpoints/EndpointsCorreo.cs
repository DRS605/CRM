using Matchketing.Api.Comun;
using Matchketing.Correo.Aplicacion;
using Matchketing.Correo.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Persistencia;

namespace Matchketing.Api.Endpoints;

public sealed record PeticionPlantilla(string? Nombre, string? Asunto, string? Cuerpo, ParaQue ParaQue);

public sealed record PeticionEnvio(Guid ContactoId, Guid? PlantillaId, string? Asunto, string? Cuerpo);

public static class EndpointsCorreo
{
    /// <summary>
    /// Un GIF transparente de 1×1, los 43 bytes más pequeños que existen para esto. Va en el código y no
    /// en un fichero para que no pueda faltar en un despliegue: un píxel que devuelve 404 se ve en el
    /// cliente de correo como un icono de imagen rota, en medio del mensaje de un comercial.
    /// </summary>
    private static readonly byte[] Pixel = Convert.FromBase64String(
        "R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    public static void MapearCorreo(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        MapearPlantillas(rutas);
        MapearEnvios(rutas);
        MapearPixel(rutas);
    }

    private static void MapearPlantillas(IEndpointRouteBuilder rutas)
    {
        var grupo = rutas.MapGroup("/plantillas").WithTags("Correo").RequireAuthorization();

        grupo.MapGet("/campos", (IContextoEmpresa contexto) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(Campos.Todos.Select(c => new { campo = c, hueco = "{{" + c + "}}" }));
        })
        .WithSummary("Los cuatro huecos que puede llevar una plantilla.");

        grupo.MapGet(string.Empty, async (ServicioCorreo servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.PlantillasAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("Las plantillas de la empresa, las más usadas primero.");

        grupo.MapPost(string.Empty, async (
            PeticionPlantilla p, ServicioCorreo servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            // Escribir una plantilla es una decisión de la empresa —el texto sale en su nombre— así que
            // pide ajustes, no solo gestionar contactos.
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.CrearPlantillaAsync(p.Nombre, p.Asunto, p.Cuerpo, p.ParaQue, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/plantillas/{r.Valor.Id}", new { id = r.Valor.Id });
        })
        .WithSummary("Crea una plantilla. Los huecos que no existan se rechazan aquí, no al enviar.");

        grupo.MapPut("/{id:guid}", async (
            Guid id, PeticionPlantilla p, ServicioCorreo servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.CambiarPlantillaAsync(id, p.Nombre, p.Asunto, p.Cuerpo, p.ParaQue, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia una plantilla. No toca los correos ya enviados: cada uno guarda su texto.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioCorreo servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.BorrarPlantillaAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("La borra. El historial de correos enviados no se toca.");
    }

    private static void MapearEnvios(IEndpointRouteBuilder rutas)
    {
        var grupo = rutas.MapGroup("/correo").WithTags("Correo").RequireAuthorization();

        grupo.MapGet("/borrador", async (
            Guid contactoId, Guid plantillaId, ServicioCorreo servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            var r = await servicio.PrepararAsync(contactoId, plantillaId, ct).ConfigureAwait(false);
            return r.Exito ? Results.Ok(r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("Lo que se va a mandar, con los huecos rellenos, y si se puede mandar. Sin enviar nada.");

        grupo.MapPost("/enviar", async (
            PeticionEnvio p, ServicioCorreo servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            // Sin plantilla, un correo escrito a mano solo puede ser para atender una solicitud. Dejar
            // elegir «comercial» por parámetro sería dejar que el cliente decida qué consentimiento se
            // le exige, que es lo mismo que no exigir ninguno.
            var r = await servicio.EnviarAsync(
                p.ContactoId, p.PlantillaId, p.Asunto, p.Cuerpo, ParaQue.AtenderSolicitud, ct).ConfigureAwait(false);

            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            // 202 y no 200: el correo está en el buzón de salida, no en la bandeja de nadie. Decir 200
            // sería decir «enviado», y todavía no lo está.
            return Results.Accepted($"/correo/contacto/{p.ContactoId}", new { id = r.Valor.Id, estado = "en cola" });
        })
        .WithSummary("Encola un correo. El permiso se comprueba aquí y otra vez justo antes de salir.");

        grupo.MapGet("/contacto/{id:guid}", async (
            Guid id, ServicioCorreo servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.DeContactoAsync(id, ct).ConfigureAwait(false));
        })
        .WithSummary("Los correos que se le han mandado, con su texto y si los ha abierto.");
    }

    private static void MapearPixel(IEndpointRouteBuilder rutas)
    {
        // Sin autenticación, como la página de baja y la entrada de formularios: la petición la hace el
        // cliente de correo de la persona, que no tiene sesión ni la va a tener nunca.
        rutas.MapGet("/e/{token}.gif", async (
            string token, ServicioCorreo servicio, ContextoMatchketing bd,
            IContextoEmpresaPublico contextoPublico, IUnidadDeTrabajo unidad, HttpContext http,
            CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(http);
            ArgumentNullException.ThrowIfNull(bd);
            ArgumentNullException.ThrowIfNull(contextoPublico);

            // La empresa la dice el propio token, igual que en el enlace de baja y por el mismo motivo:
            // sin ella la RLS no deja ver ninguna fila y la apertura no se apuntaría nunca. Y hay que
            // fijarla **antes** de tocar la base, porque `app.empresa_actual` es estado de la conexión.
            if (Matchketing.Correo.Dominio.Correo.EmpresaDelToken(token) is { } empresaId)
            {
                contextoPublico.FijarEmpresa(empresaId);
                await bd.ReaplicarEmpresaAsync(ct).ConfigureAwait(false);

                var anotado = await servicio.AnotarAperturaAsync(token, ct).ConfigureAwait(false);
                if (anotado)
                {
                    await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
                }
            }

            // Se devuelve **siempre el mismo píxel**, exista el token o no.
            //
            // Contestar 404 a un token inventado confirmaría, por eliminación, cuáles sí existen. Y a
            // quien abre el correo le da exactamente igual: solo quiere una imagen.
            http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
            http.Response.Headers.Pragma = "no-cache";

            return Results.File(Pixel, "image/gif");
        })
        .AllowAnonymous()
        .WithSummary("El píxel de apertura. Devuelve la misma imagen exista el token o no.");
    }
}
