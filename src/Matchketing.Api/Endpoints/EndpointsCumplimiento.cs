using Matchketing.Api.Comun;
using Matchketing.Api.Contratos;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Cumplimiento.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Persistencia;

namespace Matchketing.Api.Endpoints;

public static class EndpointsCumplimiento
{
    public static void MapearCumplimiento(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        MapearGestion(rutas);
        MapearDerechos(rutas);
        MapearBajaPublica(rutas);
    }

    private static void MapearGestion(IEndpointRouteBuilder rutas)
    {
        var grupo = rutas.MapGroup("/cumplimiento").WithTags("Cumplimiento").RequireAuthorization();

        grupo.MapGet("/contactos/{id:guid}", async (Guid id, ServicioCumplimiento servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            var r = await servicio.FichaAsync(id, ct).ConfigureAwait(false);
            return r.Exito ? Results.Ok(r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("Panel de privacidad: qué se le puede enviar, por qué, y su enlace de baja.");

        grupo.MapPost("/contactos/{id:guid}/consentimientos", async (
            Guid id, PeticionConsentimiento p, HttpContext http, ServicioCumplimiento servicio,
            IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(http);

            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.OtorgarAsync(
                id, p.Finalidad, p.Base, p.Canal, p.TextoAceptado,
                http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), ct).ConfigureAwait(false);

            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/cumplimiento/contactos/{id}", new { id = r.Valor.Id });
        })
        .WithSummary("Apunta un permiso con su base legal, el canal y el texto que se aceptó.");

        grupo.MapDelete("/contactos/{id:guid}/consentimientos", async (
            Guid id, FinalidadConsentimiento finalidad, ServicioCumplimiento servicio,
            IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.RetirarAsync(id, finalidad, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Retira un permiso. Inmediato e irreversible.");

        // G1 expuesta como endpoint. Existe para que cualquier integración que vaya a enviar algo
        // —una herramienta de correo, un marcador automático— pueda preguntar antes, en vez de
        // fiarse de que alguien mirara la ficha.
        grupo.MapGet("/contactos/{id:guid}/puede-enviar", async (
            Guid id, FinalidadConsentimiento finalidad, ServicioCumplimiento servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            var r = await servicio.PuedeEnviarAsync(id, finalidad, ct).ConfigureAwait(false);
            return r.Exito
                ? Results.Ok(new { puede = true })
                : r.Error!.Tipo == Nucleo.Resultados.TipoError.NoEncontrado
                    ? ResultadosHttp.Problema(r.Error!)
                    : Results.Ok(new { puede = false, codigo = r.Error!.Codigo, motivo = r.Error!.Mensaje });
        })
        .WithSummary("¿Se le puede enviar esto? Responde sí o no con el motivo.");
    }

    private static void MapearDerechos(IEndpointRouteBuilder rutas)
    {
        var grupo = rutas.MapGroup("/cumplimiento").WithTags("Cumplimiento").RequireAuthorization();

        grupo.MapGet("/contactos/{id:guid}/exportar", async (
            Guid id, ServicioCumplimiento servicio, IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.DatosExportar))
            {
                return Results.Forbid();
            }

            var r = await servicio.ExportarContactoAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            // El apunte de auditoría de la exportación se guarda aunque la respuesta sea de lectura:
            // saber quién se llevó los datos de quién es justo el tipo de cosa que hay que registrar.
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(r.Valor);
        })
        .WithSummary("Derecho de acceso y portabilidad: todo lo que guardamos de una persona, en JSON.");

        grupo.MapDelete("/contactos/{id:guid}", async (
            Guid id, ServicioCumplimiento servicio, ContextoMatchketing bd, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(bd);

            // Borrar del todo es más que gestionar contactos: hace falta poder cambiar ajustes.
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            // Transacción explícita: el borrado usa `ExecuteDelete`, que se ejecuta al momento y no
            // espera a `SaveChanges`. Sin envolverlos, un fallo al guardar la auditoría dejaría los
            // datos borrados y sin rastro de quién lo hizo.
            await using var transaccion = await bd.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var r = await servicio.BorrarContactoAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return Results.Ok(r.Valor);
        })
        .WithSummary("Derecho de supresión: borra a la persona de todas las tablas. No hay vuelta.");

        grupo.MapGet("/empresa/exportar", async (
            ServicioCumplimiento servicio, IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.ExportarEmpresaAsync(ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(r.Valor);
        })
        .WithSummary("Copia completa de los datos de la empresa. Para llevárselos a otro sitio.");

        grupo.MapPost("/empresa/borrar", async (
            PeticionBorrarEmpresa p, ServicioCumplimiento servicio, ContextoMatchketing bd,
            IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(bd);

            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            await using var transaccion = await bd.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var r = await servicio.BorrarEmpresaAsync(p.Confirmacion, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return Results.Ok(r.Valor);
        })
        .WithSummary("Cierra la cuenta: borra la empresa y todo lo suyo. Hay que escribir su nombre.");

        grupo.MapPost("/retencion", async (
            ServicioCumplimiento servicio, ContextoMatchketing bd, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(bd);

            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            if (contexto.EmpresaId is not { } empresaId)
            {
                return Results.BadRequest(new { codigo = "empresa.sin_seleccionar", mensaje = "No hay empresa activa." });
            }

            await using var transaccion = await bd.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var r = await servicio.AplicarRetencionAsync(empresaId, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return Results.Ok(r.Valor);
        })
        .WithSummary("Aplica ya la retención de leads, sin esperar al trabajo nocturno.");
    }

    private static void MapearBajaPublica(IEndpointRouteBuilder rutas)
    {
        // Sin autenticación y con CORS abierto, como la captación: quien pulsa el enlace de baja está
        // en su gestor de correo, no en nuestra aplicación.
        var publico = rutas.MapGroup("/b").WithTags("Baja (público)").RequireCors("captacion");

        // GET solo **pregunta**. Nunca da de baja.
        //
        // Esto no es puntillismo con los verbos HTTP: los antivirus de correo, las vistas previas de
        // los mensajeros y los prefetch del navegador abren los enlaces de un correo sin que nadie
        // los pulse. Un GET que diera de baja daría de baja a gente que jamás lo pidió, y la baja es
        // irreversible desde nuestro lado. Así que aquí se pinta un botón y se espera el POST.
        publico.MapGet("/{token}", (string token, ServicioCumplimiento servicio) =>
        {
            var comprobado = servicio.ComprobarEnlaceBaja(token);
            return Results.Text(PaginaBaja(token, comprobado.Exito), "text/html; charset=utf-8");
        })
        .WithSummary("Página de baja. Pregunta y espera confirmación; no da de baja por sí sola.");

        publico.MapPost("/{token}", async (
            string token, ServicioCumplimiento servicio, ContextoMatchketing bd,
            IContextoEmpresaPublico contextoPublico, IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(bd);

            var comprobado = servicio.ComprobarEnlaceBaja(token);
            if (comprobado.Fallido)
            {
                return ResultadosHttp.Problema(comprobado.Error!);
            }

            // La empresa la dice la firma del enlace, no un token de sesión: igual que en la entrada
            // pública de leads, y por el mismo motivo.
            var (empresaId, contactoId) = comprobado.Valor;
            contextoPublico.FijarEmpresa(empresaId);
            await bd.ReaplicarEmpresaAsync(ct).ConfigureAwait(false);

            var r = await servicio.DarDeBajaAsync(empresaId, contactoId, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { baja = true, yaEstaba = !r.Valor });
        })
        .WithSummary("Confirma la baja: marca el contacto y retira todos sus consentimientos.");
    }

    /// <summary>
    /// La página que ve quien pulsa el enlace del correo. Se sirve entera desde aquí, sin depender de
    /// la aplicación: es la única pantalla del sistema que tiene que funcionar cuando todo lo demás
    /// esté caído, porque del otro lado hay alguien que ya está molesto.
    /// </summary>
    private static string PaginaBaja(string token, bool valido)
    {
        // `$$` en vez de `$`: el JavaScript de dentro está lleno de llaves y con un solo `$` cada
        // `{` abriría una interpolación.
        var cuerpo = valido
            ? $$"""
                <h1>¿Quieres dejar de recibir nuestras comunicaciones?</h1>
                <p>Si confirmas, dejaremos de escribirte y de llamarte para ofrecerte cosas. Es inmediato.</p>
                <button id="confirmar" type="button">Sí, darme de baja</button>
                <p class="fino">No hace falta que hagas nada más. No te pediremos ningún motivo.</p>
                <script>
                  document.getElementById('confirmar').addEventListener('click', function () {
                    var b = this;
                    b.disabled = true;
                    b.textContent = 'Un momento…';
                    fetch({{System.Text.Json.JsonSerializer.Serialize("/b/" + token)}}, { method: 'POST' })
                      .then(function (r) { return r.ok ? r.json() : Promise.reject(r); })
                      .then(function () {
                        // Se cambia solo el cuerpo, no la tarjeta entera: quitar la marca dejaría a
                        // la persona en una página anónima que dice «hecho», sin saber de quién es.
                        document.getElementById('cuerpo').innerHTML =
                          '<h1>Hecho.</h1><p>No volverás a recibir comunicaciones comerciales nuestras. ' +
                          'Puedes cerrar esta página.</p>';
                      })
                      .catch(function () {
                        b.disabled = false;
                        b.textContent = 'Sí, darme de baja';
                        document.getElementById('aviso').hidden = false;
                      });
                  });
                </script>
                <p class="aviso" id="aviso" hidden>No hemos podido completarlo. Inténtalo otra vez en un minuto.</p>
                """
            : """
                <h1>Este enlace no es válido</h1>
                <p>Puede que esté incompleto: algunos gestores de correo cortan los enlaces largos al
                   copiarlos. Prueba a pulsarlo directamente desde el mensaje.</p>
                """;

        // Estilos en línea y ni una petición fuera: la página tiene que pintarse igual en el
        // navegador de un móvil viejo con la conexión regular.
        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <meta name="robots" content="noindex, nofollow" />
              <title>Darse de baja · match.keting</title>
              <style>
                :root {
                  --magenta: #5C2340; --tinta: #191316; --suave: #6E6167;
                  --fondo: #F7F4F2; --tarjeta: #FFFFFF; --borde: #E2D9DA; --ambar: #8A5A16;
                }
                @media (prefers-color-scheme: dark) {
                  :root {
                    --magenta: #C89BB4; --tinta: #F2ECEE; --suave: #9C8F95;
                    --fondo: #171114; --tarjeta: #211A1E; --borde: #372B32; --ambar: #E0A33A;
                  }
                }
                * { box-sizing: border-box; }
                body {
                  margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 24px;
                  background: var(--fondo); color: var(--tinta);
                  font: 16px/1.55 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
                }
                .tarjeta {
                  background: var(--tarjeta); border: 1px solid var(--borde); border-radius: 16px;
                  padding: 32px; max-width: 30rem; width: 100%;
                }
                .marca { font-weight: 700; letter-spacing: -0.02em; margin: 0 0 24px; }
                .marca i { color: var(--magenta); font-style: normal; }
                h1 { font-size: 1.35rem; line-height: 1.25; margin: 0 0 12px; }
                p { margin: 0 0 14px; }
                .fino, .aviso { font-size: 0.875rem; color: var(--suave); }
                .aviso { color: var(--ambar); }
                button {
                  font: inherit; font-weight: 600; margin: 6px 0 14px; padding: 12px 20px;
                  border: 0; border-radius: 10px; background: var(--magenta); color: #fff; cursor: pointer;
                }
                button:disabled { opacity: 0.6; cursor: default; }
              </style>
            </head>
            <body>
              <main class="tarjeta">
                <p class="marca">match<i>.</i>keting</p>
                <div id="cuerpo">{{cuerpo}}</div>
              </main>
            </body>
            </html>
            """;
    }
}
