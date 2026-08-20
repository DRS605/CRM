using Matchketing.Api.Comun;
using Matchketing.Auditoria.Aplicacion;
using Matchketing.Auditoria.Dominio;
using Matchketing.Automatizacion.Aplicacion;
using Matchketing.Automatizacion.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;

namespace Matchketing.Api.Endpoints;

public sealed record PeticionCondicion(Campo Campo, Operador Operador, string? Valor);

public sealed record PeticionAccion(TipoAccion Tipo, string? Texto, Guid? Referencia, int? Numero);

public sealed record PeticionRegla(
    string? Nombre, string? Disparador, IReadOnlyList<PeticionCondicion>? Condiciones, IReadOnlyList<PeticionAccion>? Acciones);

public static class EndpointsAutomatizacion
{
    public static void MapearAutomatizacion(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        // Todo el grupo pide `empresa.ajustes`. Una regla hace cosas en nombre de la empresa sin que nadie
        // las pulse: no es una pantalla de trabajo, es una decisión de administración.
        var grupo = rutas.MapGroup("/reglas").WithTags("Automatización").RequireAuthorization();

        grupo.MapGet("/catalogo", (IContextoEmpresa contexto) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            // Todo lo que la pantalla necesita para pintar el formulario, del servidor y no escrito en el
            // cliente: así añadir un disparador o una acción no obliga a tocar la interfaz.
            return Results.Ok(new
            {
                disparadores = Textos.TodosLosDisparadores.Select(d => new { nombre = Textos.De(d) }),
                campos = Enum.GetValues<Campo>().Select(c => new { valor = (int)c, nombre = Textos.De(c) }),
                operadores = Enum.GetValues<Operador>().Select(o => new { valor = (int)o, nombre = Textos.De(o) }),
                acciones = Enum.GetValues<TipoAccion>().Select(a => new { valor = (int)a, nombre = Textos.De(a) }),
                maximoCondiciones = Regla.MaximoCondiciones,
                maximoAcciones = Regla.MaximoAcciones,
            });
        })
        .WithSummary("Qué se puede poner en una regla. Lo pinta la pantalla desde aquí.");

        grupo.MapGet(string.Empty, async (ServicioAutomatizacion servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.ListarAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("Las reglas de la empresa, las encendidas primero, cada una leída en castellano.");

        grupo.MapPost(string.Empty, async (
            PeticionRegla p, ServicioAutomatizacion servicio, IUnidadDeTrabajo unidad,
            IRegistradorAuditoria auditoria, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            if (Textos.DisparadorDe(p.Disparador) is not { } disparador)
            {
                return ResultadosHttp.Problema(Matchketing.Nucleo.Resultados.Error.Validacion(
                    "regla.disparador_desconocido", $"El disparador «{p.Disparador}» no existe."));
            }

            var r = await servicio.CrearAsync(p.Nombre, disparador, Traducir(p.Condiciones), Traducir(p.Acciones), ct)
                .ConfigureAwait(false);

            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            // Se audita porque una regla puede mandar correos y crear tareas sin que nadie las pulse: quién
            // la escribió es exactamente el tipo de cosa que hay que poder averiguar después.
            auditoria.Registrar("regla", r.Valor.Id, Acciones.AjustesCambiados, new { creada = r.Valor.Nombre });

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            // Nace apagada, y se dice: quien la crea tiene que leerla antes de encenderla.
            return Results.Created($"/reglas/{r.Valor.Id}", new
            {
                id = r.Valor.Id,
                leida = r.Valor.Leer(),
                aviso = "Nace apagada. Léela, pruébala con un contacto y enciéndela cuando te cuadre.",
            });
        })
        .WithSummary("Crea una regla, apagada.");

        grupo.MapPut("/{id:guid}", async (
            Guid id, PeticionRegla p, ServicioAutomatizacion servicio, IUnidadDeTrabajo unidad,
            IRegistradorAuditoria auditoria, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            if (Textos.DisparadorDe(p.Disparador) is not { } disparador)
            {
                return ResultadosHttp.Problema(Matchketing.Nucleo.Resultados.Error.Validacion(
                    "regla.disparador_desconocido", $"El disparador «{p.Disparador}» no existe."));
            }

            var r = await servicio.CambiarAsync(id, p.Nombre, disparador, Traducir(p.Condiciones), Traducir(p.Acciones), ct)
                .ConfigureAwait(false);

            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            auditoria.Registrar("regla", id, Acciones.AjustesCambiados, new { cambiada = p.Nombre });
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("La cambia, y **la apaga**: un cambio a medias no puede seguir disparando.");

        grupo.MapPost("/{id:guid}/encender", async (
            Guid id, bool? encender, ServicioAutomatizacion servicio, IUnidadDeTrabajo unidad,
            IRegistradorAuditoria auditoria, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.EncenderAsync(id, encender ?? true, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            auditoria.Registrar("regla", id, Acciones.AjustesCambiados, new { activa = r.Valor });
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { activa = r.Valor });
        })
        .WithSummary("La enciende o la apaga.");

        grupo.MapGet("/{id:guid}/ensayo", async (
            Guid id, Guid contactoId, ServicioAutomatizacion servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.EnsayarAsync(id, contactoId, ct).ConfigureAwait(false);
            return r.Exito ? Results.Ok(r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("Qué haría con este contacto, **sin hacerlo**. Es la única forma de probar una regla.");

        grupo.MapGet("/{id:guid}/ejecuciones", async (
            Guid id, ServicioAutomatizacion servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.HistorialAsync(id, ct).ConfigureAwait(false);
            return r.Exito ? Results.Ok(r.Valor) : ResultadosHttp.Problema(r.Error!);
        })
        .WithSummary("Qué ha hecho esta regla y sobre quién. Sin esto, una automatización no se puede auditar.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioAutomatizacion servicio, IUnidadDeTrabajo unidad,
            IRegistradorAuditoria auditoria, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.BorrarAsync(id, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            auditoria.Registrar("regla", id, Acciones.AjustesCambiados, new { borrada = true });
            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("La borra. Lo que ya hizo no se deshace.");
    }

    private static List<Condicion> Traducir(IReadOnlyList<PeticionCondicion>? condiciones) =>
        (condiciones ?? []).Select(c => new Condicion(c.Campo, c.Operador, c.Valor ?? string.Empty)).ToList();

    private static List<Accion> Traducir(IReadOnlyList<PeticionAccion>? acciones) =>
        (acciones ?? []).Select(a => new Accion(a.Tipo, a.Texto, a.Referencia, a.Numero)).ToList();
}
