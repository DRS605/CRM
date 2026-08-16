using Matchketing.Contactos.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Contactos.Aplicacion;

/// <summary>Alta, edición y cronología de contactos.</summary>
public sealed class ServicioContactos(
    IRepositorioContactos contactos,
    IRepositorioCuentas cuentas,
    IRepositorioActividades actividades,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    public async Task<Resultado<Contacto>> CrearAsync(
        string? nombre, string? email, string? telefono, string? cargo,
        Guid? cuentaId, string? origen, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Contacto>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        if (cuentaId is { } id && await cuentas.BuscarPorIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Resultado.Fallo<Contacto>(Error.NoEncontrado("cuenta.no_encontrada", "La cuenta indicada no existe."));
        }

        var creado = Contacto.Crear(empresaId, nombre, email, telefono, cargo, cuentaId, origen, contexto.UsuarioId, reloj);
        if (creado.Fallido)
        {
            return creado;
        }

        contactos.Anadir(creado.Valor);
        return creado;
    }

    public async Task<Resultado<Contacto>> ActualizarAsync(
        Guid id, string? nombre, string? email, string? telefono, string? cargo,
        Guid? cuentaId, Guid? propietarioId, CancellationToken ct = default)
    {
        var contacto = await contactos.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (contacto is null)
        {
            return Resultado.Fallo<Contacto>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        var r = contacto.Actualizar(nombre, email, telefono, cargo, cuentaId, propietarioId, reloj);
        return r.Fallido ? Resultado.Fallo<Contacto>(r.Error!) : Resultado.Ok(contacto);
    }

    /// <summary>Cambia el dueño del contacto. Lo usa el reparto de leads del módulo Match.</summary>
    public async Task<Resultado<Contacto>> AsignarPropietarioAsync(Guid id, Guid propietarioId, CancellationToken ct = default)
    {
        var contacto = await contactos.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (contacto is null)
        {
            return Resultado.Fallo<Contacto>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        var r = contacto.Actualizar(contacto.Nombre, contacto.Email, contacto.Telefono, contacto.Cargo, contacto.CuentaId, propietarioId, reloj);
        return r.Fallido ? Resultado.Fallo<Contacto>(r.Error!) : Resultado.Ok(contacto);
    }

    public async Task<Resultado> CambiarEstadoAsync(Guid id, EstadoContacto estado, CancellationToken ct = default)
    {
        var contacto = await contactos.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return contacto is null
            ? Resultado.Fallo(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."))
            : contacto.CambiarEstado(estado, reloj);
    }

    public async Task<Resultado> DesactivarAsync(Guid id, CancellationToken ct = default)
    {
        var contacto = await contactos.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (contacto is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        contacto.Desactivar(reloj);
        return Resultado.Ok();
    }

    /// <summary>Añade una anotación a la cronología: nota, correo, reunión, formulario…</summary>
    public async Task<Resultado<Actividad>> RegistrarActividadAsync(
        Guid contactoId, TipoActividad tipo, SentidoActividad sentido, string? cuerpo,
        ResultadoLlamada? resultado = null, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Actividad>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var contacto = await contactos.BuscarPorIdAsync(contactoId, ct).ConfigureAwait(false);
        if (contacto is null)
        {
            return Resultado.Fallo<Actividad>(Error.NoEncontrado("contacto.no_encontrado", "El contacto no existe."));
        }

        var creada = Actividad.Crear(empresaId, contactoId, tipo, sentido, cuerpo, contexto.UsuarioId, reloj, resultado);
        if (creada.Fallido)
        {
            return creada;
        }

        actividades.Anadir(creada.Valor);
        return creada;
    }

    /// <summary>
    /// Registro de llamada en un clic: el resultado se convierte en texto legible para la
    /// cronología, y «volver a llamar» deja constancia de que hay que hacerlo (la tarea la creará
    /// el módulo 4).
    /// </summary>
    public Task<Resultado<Actividad>> RegistrarLlamadaAsync(Guid contactoId, ResultadoLlamada resultado, string? nota, CancellationToken ct = default)
    {
        var texto = resultado switch
        {
            ResultadoLlamada.Contactado => "Llamada: contactado.",
            ResultadoLlamada.NoContesta => "Llamada: no contesta.",
            ResultadoLlamada.NoInteresa => "Llamada: no le interesa.",
            ResultadoLlamada.VolverALlamar => "Llamada: hay que volver a llamar.",
            _ => "Llamada.",
        };

        if (!string.IsNullOrWhiteSpace(nota))
        {
            texto += " " + nota.Trim();
        }

        return RegistrarActividadAsync(contactoId, TipoActividad.Llamada, SentidoActividad.Saliente, texto, resultado, ct);
    }

    public async Task<Resultado<Cuenta>> CrearCuentaAsync(
        string? nombre, string? nif, string? sector, string? provincia, int? tamano, string? web, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Cuenta>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var creada = Cuenta.Crear(empresaId, nombre, nif, sector, provincia, tamano, web, reloj);
        if (creada.Exito)
        {
            cuentas.Anadir(creada.Valor);
        }

        return creada;
    }

    public Task<IReadOnlyList<Cuenta>> CuentasAsync(CancellationToken ct = default) => cuentas.ActivasAsync(ct);
}
