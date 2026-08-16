using Matchketing.Api.Comun;
using Matchketing.Api.Contratos;
using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Match.Aplicacion;
using Matchketing.Tareas.Aplicacion;

namespace Matchketing.Api.Endpoints;

public static class EndpointsContactos
{
    public static void MapearContactos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/contactos").WithTags("Contactos").RequireAuthorization();

        grupo.MapGet(string.Empty, async (
            string? busqueda, EstadoContacto? estado,
            IConsultaContactos consulta, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await consulta.ListarAsync(busqueda, estado, ct).ConfigureAwait(false));
        })
        .WithSummary("Lista los contactos activos, con búsqueda por nombre, correo, teléfono o cargo.");

        grupo.MapGet("/duplicados", async (IConsultaContactos consulta, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            return Results.Ok(await consulta.DuplicadosAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("Parejas que parecen la misma persona. El sistema propone; la persona decide.");

        grupo.MapGet("/{id:guid}", async (Guid id, IConsultaContactos consulta, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            var ficha = await consulta.FichaAsync(id, ct).ConfigureAwait(false);
            return ficha is null
                ? Results.NotFound(new { codigo = "contacto.no_encontrado", mensaje = "El contacto no existe." })
                : Results.Ok(ficha);
        })
        .WithSummary("Ficha del contacto con su cronología completa.");

        grupo.MapPost(string.Empty, async (
            PeticionContacto p, ServicioContactos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.CrearAsync(p.Nombre, p.Email, p.Telefono, p.Cargo, p.CuentaId, p.Origen, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/contactos/{r.Valor.Id}", new { id = r.Valor.Id });
        })
        .WithSummary("Crea un contacto. Exige al menos correo o teléfono.");

        grupo.MapPut("/{id:guid}", async (
            Guid id, PeticionActualizarContacto p, ServicioContactos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.ActualizarAsync(id, p.Nombre, p.Email, p.Telefono, p.Cargo, p.CuentaId, p.PropietarioId, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Actualiza un contacto.");

        grupo.MapPut("/{id:guid}/estado", async (
            Guid id, PeticionEstado p, ServicioContactos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.CambiarEstadoAsync(id, p.Estado, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia el estado. Un contacto de baja no vuelve por esta puerta.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioContactos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
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
        .WithSummary("Desactiva el contacto. No se borra: la historia no se tira.");

        grupo.MapPost("/{id:guid}/notas", async (
            Guid id, PeticionNota p, ServicioContactos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.RegistrarActividadAsync(id, TipoActividad.Nota, SentidoActividad.Interna, p.Cuerpo, null, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/contactos/{id}", new { id = r.Valor.Id });
        })
        .WithSummary("Añade una nota a la cronología.");

        grupo.MapPost("/{id:guid}/llamada", async (
            Guid id, PeticionLlamada p, ServicioContactos servicio, ServicioTareas tareas,
            ServicioMatch match, IUnidadDeTrabajo unidad, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.RegistrarLlamadaAsync(id, p.Resultado, p.Nota, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            // Si la llamada pide volver a llamar, la siguiente tarea se crea sola: es la única forma
            // de cumplir la promesa de que ningún contacto vivo se queda sin próximo paso.
            if (p.Resultado == ResultadoLlamada.VolverALlamar)
            {
                await tareas.CrearSeguimientoLlamadaAsync(id, ct).ConfigureAwait(false);
            }

            // Que coja el teléfono es señal de interés; que no lo coja, no lo es.
            if (p.Resultado == ResultadoLlamada.Contactado)
            {
                await match.RegistrarSenalAsync(id, Match.Dominio.TipoSenal.LlamadaContestada, ct).ConfigureAwait(false);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/contactos/{id}", new { id = r.Valor.Id });
        })
        .WithSummary("Registra el resultado de una llamada en un clic.");

        grupo.MapPost("/{id:guid}/fusionar", async (
            Guid id, PeticionFusion p, ServicioDuplicados duplicados, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await duplicados.FusionarAsync(id, p.AbsorbidoId, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { actividadesMovidas = r.Valor });
        })
        .WithSummary("Fusiona otro contacto dentro de este. Rellena huecos y se trae todas las actividades.");

        grupo.MapPost("/importar", async (
            PeticionImportacion p, ImportarContactos importador, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await importador.EjecutarAsync(p.Contenido, p.Previsualizar, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            if (!p.Previsualizar)
            {
                await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            }

            return Results.Ok(r.Valor);
        })
        .WithSummary("Importa contactos desde CSV. En previsualización no guarda nada.");

        var cuentas = rutas.MapGroup("/cuentas").WithTags("Contactos").RequireAuthorization();

        cuentas.MapGet(string.Empty, async (ServicioContactos servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            var lista = await servicio.CuentasAsync(ct).ConfigureAwait(false);
            return Results.Ok(lista.Select(c => new
            {
                id = c.Id, nombre = c.Nombre, nif = c.Nif, sector = c.Sector,
                provincia = c.Provincia, tamano = c.Tamano, web = c.Web,
            }));
        })
        .WithSummary("Lista las cuentas activas.");

        cuentas.MapPost(string.Empty, async (
            PeticionCuenta p, ServicioContactos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.CrearCuentaAsync(p.Nombre, p.Nif, p.Sector, p.Provincia, p.Tamano, p.Web, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/cuentas/{r.Valor.Id}", new { id = r.Valor.Id });
        })
        .WithSummary("Crea una cuenta (la empresa del contacto). Opcional: en B2C no se usa.");
    }
}
