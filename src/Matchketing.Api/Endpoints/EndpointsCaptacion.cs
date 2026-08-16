using System.Text.Json;
using Matchketing.Api.Comun;
using Matchketing.Api.Contratos;
using Matchketing.Captacion.Aplicacion;
using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Cumplimiento.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Match.Aplicacion;
using Matchketing.Nucleo.Comun;
using Matchketing.Persistencia;
using Matchketing.Tareas.Aplicacion;

namespace Matchketing.Api.Endpoints;

public static class EndpointsCaptacion
{
    public static void MapearCaptacion(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        MapearGestion(rutas);
        MapearEntradaPublica(rutas);
    }

    private static void MapearGestion(IEndpointRouteBuilder rutas)
    {
        var grupo = rutas.MapGroup("/formularios").WithTags("Captación").RequireAuthorization();

        grupo.MapGet(string.Empty, async (ServicioFormularios servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.FormularioGestionar))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.ListarAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("Formularios activos, con cuántos envíos lleva cada uno.");

        grupo.MapPost(string.Empty, async (
            PeticionFormulario p, ServicioFormularios servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.FormularioGestionar))
            {
                return Results.Forbid();
            }

            var r = servicio.Crear(p.Nombre, p.TextoConsentimiento, p.PideTelefono, p.PideEmpresa, p.PideMensaje, p.PaginaGracias, p.Origen);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/formularios/{r.Valor.Id}", new { id = r.Valor.Id, clave = r.Valor.Clave });
        })
        .WithSummary("Crea un formulario y devuelve su clave pública.");

        grupo.MapPut("/{id:guid}", async (
            Guid id, PeticionFormulario p, ServicioFormularios servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.FormularioGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.ActualizarAsync(id, p.Nombre, p.TextoConsentimiento, p.PideTelefono, p.PideEmpresa, p.PideMensaje, p.PaginaGracias, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Actualiza un formulario.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioFormularios servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.FormularioGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.DesactivarAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Desactiva un formulario. Deja de aceptar envíos.");
    }

    private static string Json(string? valor) => JsonSerializer.Serialize(valor);

    private static string Campo(string nombre, string etiqueta, string tipo, bool obligatorio) =>
        $"""<label>{etiqueta}{(obligatorio ? " *" : string.Empty)}<input type="{tipo}" data-mk="{nombre}" /></label>""";

    private static string AreaTexto(string nombre, string etiqueta) =>
        $"""<label>{etiqueta}<textarea rows="3" data-mk="{nombre}"></textarea></label>""";

    private static void MapearEntradaPublica(IEndpointRouteBuilder rutas)
    {
        // La política CORS «captacion» solo se aplica a este grupo. El resto de la API sigue
        // siendo de mismo origen.
        var publico = rutas.MapGroup("/f").WithTags("Captación (público)").RequireCors("captacion");

        publico.MapGet("/{clave}", async (string clave, ServicioFormularios servicio, CancellationToken ct) =>
        {
            var f = await servicio.PorClaveAsync(clave, ct).ConfigureAwait(false);
            return f is null
                ? Results.NotFound(new { codigo = "formulario.no_encontrado", mensaje = "Ese formulario no existe o ya no está activo." })
                : Results.Ok(new
                {
                    nombre = f.Nombre,
                    textoConsentimiento = f.TextoConsentimiento,
                    pideTelefono = f.PideTelefono,
                    pideEmpresa = f.PideEmpresa,
                    pideMensaje = f.PideMensaje,
                });
        })
        .WithSummary("Definición pública del formulario, para pintarlo.");

        // El flujo estrella: de la web del cliente a una tarjeta en Hoy, sin que nadie lo toque.
        publico.MapPost("/{clave}", async (
            string clave, PeticionLead p, HttpContext http,
            ServicioFormularios formularios, ServicioContactos contactos, ServicioMatch match,
            ServicioTareas tareas, ContextoMatchketing bd, IContextoEmpresaPublico contextoPublico,
            Matchketing.Nucleo.Tiempo.IReloj reloj, IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(http);

            var formulario = await formularios.PorClaveAsync(clave, ct).ConfigureAwait(false);
            if (formulario is null)
            {
                return Results.NotFound(new { codigo = "formulario.no_encontrado", mensaje = "Ese formulario no existe o ya no está activo." });
            }

            // G1: sin marcar la casilla no entra nada. Ni el contacto: guardar a alguien que no ha
            // consentido para «ya se lo pediremos luego» es exactamente lo que no se puede hacer.
            if (!p.Consiente)
            {
                return Results.BadRequest(new { codigo = "lead.sin_consentimiento", mensaje = "Hay que aceptar el aviso de privacidad." });
            }

            // A partir de aquí la empresa la dice la clave del formulario, no un token.
            contextoPublico.FijarEmpresa(formulario.EmpresaId);
            await bd.ReaplicarEmpresaAsync(ct).ConfigureAwait(false);

            var creado = await contactos.CrearAsync(p.Nombre, p.Email, p.Telefono, null, null, formulario.Origen, ct).ConfigureAwait(false);
            if (creado.Fallido)
            {
                return ResultadosHttp.Problema(creado.Error!);
            }

            var contacto = creado.Valor;

            // Se guarda **aquí**, antes de seguir: el contacto es el ancla de todo lo que viene
            // después —consentimiento, señal, puntuación, reparto, tarea— y todos esos pasos lo
            // buscan en la base. Si algo falla más adelante, el lead ya está dentro; perderlo por un
            // fallo en el reparto sería el peor desenlace posible.
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            var consentimiento = Consentimiento.Otorgar(
                formulario.EmpresaId, contacto.Id, FinalidadConsentimiento.AtenderSolicitud, BaseLegal.Consentimiento,
                "formulario web", formulario.TextoConsentimiento,
                http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), reloj);

            if (consentimiento.Exito)
            {
                bd.Consentimientos.Add(consentimiento.Valor);
            }

            var datos = JsonSerializer.Serialize(new
            {
                p.Nombre, p.Email, p.Telefono, p.Empresa, p.Mensaje,
                formulario = formulario.Nombre,
            });

            formularios.RegistrarEnvio(
                formulario.EmpresaId, formulario.Id, datos,
                http.Connection.RemoteIpAddress?.ToString(), http.Request.Headers.UserAgent.ToString(), contacto.Id);

            var texto = string.IsNullOrWhiteSpace(p.Mensaje)
                ? $"Rellenó el formulario «{formulario.Nombre}»."
                : $"Rellenó el formulario «{formulario.Nombre}»: {p.Mensaje!.Trim()}";

            await contactos.RegistrarActividadAsync(
                contacto.Id, TipoActividad.Formulario, SentidoActividad.Entrante, texto, null, ct).ConfigureAwait(false);

            await match.RegistrarSenalAsync(contacto.Id, Matchketing.Match.Dominio.TipoSenal.FormularioEnviado, ct).ConfigureAwait(false);

            // Reparto y primera acción. Si no hay comerciales, el lead entra igual: perderlo por no
            // saber a quién dárselo sería el peor de los desenlaces.
            var propuesta = await match.ProponerComercialAsync(contacto.Id, ct).ConfigureAwait(false);
            if (propuesta.Exito)
            {
                await contactos.AsignarPropietarioAsync(contacto.Id, propuesta.Valor.UsuarioId, ct).ConfigureAwait(false);
                await contactos.RegistrarActividadAsync(
                    contacto.Id, TipoActividad.Sistema, SentidoActividad.Interna,
                    $"Asignado a {propuesta.Valor.Nombre}: {string.Join(", ", propuesta.Valor.Motivos)}.", null, ct).ConfigureAwait(false);
            }

            tareas.Crear($"Primera llamada a {contacto.Nombre}", contacto.Id, null, null, Tareas.Dominio.OrigenTarea.Automatica);

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                gracias = true,
                paginaGracias = formulario.PaginaGracias,
                asignadoA = propuesta.Exito ? propuesta.Valor.Nombre : null,
            });
        })
        .WithSummary("Entrada pública de leads: crea contacto, guarda el consentimiento, puntúa, asigna y crea la primera llamada.");


        // El «script de una línea»: se pega en la web del cliente y pinta el formulario donde esté
        // la etiqueta. Sin dependencias, sin iframe y sin estilos que peleen con los de su web.
        publico.MapGet("/{clave}/script.js", async (string clave, HttpContext http, ServicioFormularios servicio, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(http);

            var f = await servicio.PorClaveAsync(clave, ct).ConfigureAwait(false);
            if (f is null)
            {
                return Results.Text("/* Formulario no encontrado o desactivado. */", "application/javascript");
            }

            var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            var campos = new List<string> { Campo("nombre", "Nombre", "text", true) };
            campos.Add(Campo("email", "Correo electrónico", "email", false));
            if (f.PideTelefono) { campos.Add(Campo("telefono", "Teléfono", "tel", false)); }
            if (f.PideEmpresa) { campos.Add(Campo("empresa", "Empresa", "text", false)); }
            if (f.PideMensaje) { campos.Add(AreaTexto("mensaje", "¿En qué podemos ayudarte?")); }

            var js = $$"""
                (function () {
                  'use strict';
                  var BASE = {{Json(baseUrl)}};
                  var CLAVE = {{Json(f.Clave)}};
                  var GRACIAS = {{Json(f.PaginaGracias)}};
                  var actual = document.currentScript;

                  var caja = document.createElement('div');
                  caja.className = 'mk-formulario';
                  caja.innerHTML = {{Json(string.Join(string.Empty, campos))}} +
                    '<label class="mk-consent"><input type="checkbox" data-mk="consiente" /> ' +
                    {{Json(f.TextoConsentimiento)}} + '</label>' +
                    '<button type="button" data-mk="enviar">Enviar</button>' +
                    '<p class="mk-aviso" data-mk="aviso" hidden></p>';

                  var estilo = document.createElement('style');
                  estilo.textContent =
                    '.mk-formulario{display:flex;flex-direction:column;gap:10px;max-width:420px;font:inherit}' +
                    '.mk-formulario input[type=text],.mk-formulario input[type=email],' +
                    '.mk-formulario input[type=tel],.mk-formulario textarea{font:inherit;padding:10px 12px;' +
                    'border:1px solid #ccc;border-radius:8px;width:100%;box-sizing:border-box}' +
                    '.mk-formulario button{font:inherit;font-weight:600;padding:11px 18px;border:0;' +
                    'border-radius:8px;background:#D4006E;color:#fff;cursor:pointer}' +
                    '.mk-consent{display:flex;gap:8px;align-items:flex-start;font-size:13px;line-height:1.4}' +
                    '.mk-aviso{font-size:13px;color:#A66A00;margin:0}';

                  (actual && actual.parentNode ? actual.parentNode : document.body).insertBefore(estilo, actual);
                  (actual && actual.parentNode ? actual.parentNode : document.body).insertBefore(caja, actual);

                  function valor(nombre) {
                    var el = caja.querySelector('[data-mk="' + nombre + '"]');
                    return el ? el.value : null;
                  }

                  function avisar(texto) {
                    var a = caja.querySelector('[data-mk="aviso"]');
                    a.textContent = texto;
                    a.hidden = !texto;
                  }

                  caja.querySelector('[data-mk="enviar"]').addEventListener('click', function () {
                    var boton = caja.querySelector('[data-mk="enviar"]');
                    boton.disabled = true;
                    avisar('');

                    fetch(BASE + '/f/' + CLAVE, {
                      method: 'POST',
                      headers: { 'Content-Type': 'application/json' },
                      body: JSON.stringify({
                        nombre: valor('nombre'),
                        email: valor('email'),
                        telefono: valor('telefono'),
                        empresa: valor('empresa'),
                        mensaje: valor('mensaje'),
                        consiente: caja.querySelector('[data-mk="consiente"]').checked
                      })
                    }).then(function (r) {
                      return r.json().then(function (d) { return { ok: r.ok, d: d }; });
                    }).then(function (res) {
                      if (!res.ok) {
                        avisar(res.d.mensaje || 'No se ha podido enviar.');
                        boton.disabled = false;
                        return;
                      }
                      if (GRACIAS) { window.location.href = GRACIAS; return; }
                      caja.innerHTML = '<p><strong>Gracias.</strong> Te llamamos enseguida.</p>';
                    }).catch(function () {
                      avisar('No se ha podido enviar. Inténtalo de nuevo.');
                      boton.disabled = false;
                    });
                  });
                })();
                """;

            return Results.Text(js, "application/javascript");
        })
        .WithSummary("El script de una línea que pinta el formulario en la web del cliente.");

        publico.MapPost("/{clave}/visita", async (
            string clave, PeticionVisita p, ServicioFormularios formularios, ServicioMatch match,
            ContextoMatchketing bd, IContextoEmpresaPublico contextoPublico,
            IUnidadDeTrabajo unidad, CancellationToken ct) =>
        {
            var formulario = await formularios.PorClaveAsync(clave, ct).ConfigureAwait(false);
            if (formulario is null)
            {
                return Results.NotFound();
            }

            contextoPublico.FijarEmpresa(formulario.EmpresaId);
            await bd.ReaplicarEmpresaAsync(ct).ConfigureAwait(false);

            // Solo se registra la visita de un contacto **ya conocido**, el que volvió a la web
            // después de dejarnos sus datos. No se identifica a visitantes anónimos.
            var r = await match.RegistrarSenalAsync(p.ContactoId, Matchketing.Match.Dominio.TipoSenal.VisitaWeb, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return Results.NoContent();
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Visita web de un contacto ya conocido. Solo con su consentimiento previo.");
    }
}
