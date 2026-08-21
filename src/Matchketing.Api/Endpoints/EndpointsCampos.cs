using Matchketing.Api.Comun;
using Matchketing.Campos.Aplicacion;
using Matchketing.Campos.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Api.Endpoints;

public sealed record PeticionCampo(string? Ambito, string? Nombre, string? Tipo, IReadOnlyList<string>? Opciones);

public sealed record PeticionRenombrar(string? Nombre);

public sealed record PeticionOpciones(IReadOnlyList<string>? Opciones);

public sealed record PeticionOrdenCampos(string? Ambito, IReadOnlyList<Guid> Orden);

public sealed record PeticionValor(string? Valor);

public static class EndpointsCampos
{
    public static void MapearCampos(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/campos").WithTags("Campos propios").RequireAuthorization();

        // ---------- La definición ----------

        // Leer la definición pide `contacto.leer` y no el permiso de ajustes: la ficha de cualquier
        // contacto necesita saber qué campos hay para pintarlos, así que quien puede abrir una ficha
        // tiene que poder pedir esto. Quien no puede leer contactos no tiene ninguna pantalla donde se
        // vean.
        grupo.MapGet(string.Empty, async (
            ServicioCampos servicio, IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            return Results.Ok(await servicio.DefinicionAsync(ct).ConfigureAwait(false));
        })
        .WithSummary("Los campos propios de la empresa, con cuántas fichas tienen relleno cada uno.");

        // Definir, renombrar y borrar campos va con **`empresa.ajustes`**, no con `contacto.gestionar`.
        // Un campo propio es configuración del CRM y lo ve todo el mundo: quien lo define está cambiando
        // la ficha de contacto de sus compañeros, no rellenando un dato.
        grupo.MapPost(string.Empty, async (
            PeticionCampo p, ServicioCampos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            if (TextosCampo.AmbitoDe(p.Ambito) is not { } ambito)
            {
                return ResultadosHttp.Problema(Error.Validacion(
                    "campo.ambito_invalido", "Un campo propio se define sobre un contacto o sobre una cuenta."));
            }

            if (TextosCampo.TipoDe(p.Tipo) is not { } tipo)
            {
                return ResultadosHttp.Problema(Error.Validacion(
                    "campo.tipo_invalido", "Los tipos son: texto, numero, fecha, si_no y lista."));
            }

            var r = await servicio.CrearAsync(ambito, p.Nombre, tipo, p.Opciones, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.Created($"/campos/{r.Valor.Id}", new { id = r.Valor.Id, clave = r.Valor.Clave });
        })
        .WithSummary("Define un campo propio. Diez por ámbito, y el tipo no se cambia después.");

        grupo.MapPut("/{id:guid}/nombre", async (
            Guid id, PeticionRenombrar p, ServicioCampos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.RenombrarAsync(id, p.Nombre, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia la etiqueta. La clave se queda como estaba, a propósito.");

        grupo.MapPut("/{id:guid}/opciones", async (
            Guid id, PeticionOpciones p, ServicioCampos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            var r = await servicio.CambiarOpcionesAsync(id, p.Opciones, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Cambia las opciones de una lista. No se puede quitar una que alguien esté usando.");

        grupo.MapPut("/orden", async (
            PeticionOrdenCampos p, ServicioCampos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.EmpresaAjustes))
            {
                return Results.Forbid();
            }

            if (TextosCampo.AmbitoDe(p.Ambito) is not { } ambito)
            {
                return ResultadosHttp.Problema(Error.Validacion(
                    "campo.ambito_invalido", "El ámbito es «contacto» o «cuenta»."));
            }

            var r = await servicio.ReordenarAsync(ambito, p.Orden, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Coloca los campos de un ámbito en el orden en que se ven en la ficha.");

        grupo.MapDelete("/{id:guid}", async (
            Guid id, ServicioCampos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
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

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);

            // Se dice cuántos valores se fueron con él. Es un borrado con consecuencias y quien lo hace
            // merece verlas escritas, aunque la pantalla ya le haya avisado antes.
            return Results.Ok(new { valoresBorrados = r.Valor });
        })
        .WithSummary("Borra el campo y todos sus valores. Se dice cuántos se han ido.");

        // ---------- Los valores ----------

        grupo.MapGet("/{ambito}/{entidadId:guid}", async (
            string ambito, Guid entidadId, ServicioCampos servicio,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            if (!contexto.Tiene(Permisos.ContactoLeer))
            {
                return Results.Forbid();
            }

            return TextosCampo.AmbitoDe(ambito) is { } cual
                ? Results.Ok(await servicio.DeLaFichaAsync(cual, entidadId, ct).ConfigureAwait(false))
                : Results.NotFound();
        })
        .WithSummary("Los campos propios de una ficha, con su valor. Salen también los que están vacíos.");

        // Rellenar un campo es **`contacto.gestionar`**: es un dato de la ficha, como el teléfono. Un
        // comercial rellena los campos de sus contactos; lo que no puede es inventarse campos nuevos.
        grupo.MapPut("/{id:guid}/valor/{entidadId:guid}", async (
            Guid id, Guid entidadId, PeticionValor p, ServicioCampos servicio, IUnidadDeTrabajo unidad,
            IContextoEmpresa contexto, CancellationToken ct) =>
        {
            ArgumentNullException.ThrowIfNull(p);

            if (!contexto.Tiene(Permisos.ContactoGestionar))
            {
                return Results.Forbid();
            }

            var r = await servicio.FijarAsync(id, entidadId, p.Valor, ct).ConfigureAwait(false);
            if (r.Fallido)
            {
                return ResultadosHttp.Problema(r.Error!);
            }

            await unidad.GuardarCambiosAsync(ct).ConfigureAwait(false);
            return Results.NoContent();
        })
        .WithSummary("Pone el valor de un campo en una ficha. Vacío lo quita.");
    }
}
