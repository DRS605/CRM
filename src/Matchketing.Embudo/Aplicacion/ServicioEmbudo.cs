using Matchketing.Auditoria.Aplicacion;
using Matchketing.Auditoria.Dominio;
using Matchketing.Embudo.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Embudo.Aplicacion;

public sealed class ServicioEmbudo(
    IRepositorioEmbudos embudos,
    IRepositorioOportunidades oportunidades,
    IConsultaEmbudo consulta,
    IRegistradorAuditoria auditoria,
    IContextoEmpresa contexto,
    IReloj reloj)
{
    /// <summary>Crea el embudo por defecto de la empresa. Se llama al dar de alta la empresa.</summary>
    public Dominio.Embudo CrearEmbudoPorDefecto(Guid empresaId)
    {
        var embudo = Dominio.Embudo.CrearPorDefecto(empresaId, reloj);
        embudos.Anadir(embudo);
        return embudo;
    }

    public Task<Tablero?> TableroAsync(Guid? embudoId = null, CancellationToken ct = default) =>
        consulta.TableroAsync(embudoId, ct);

    public async Task<Resultado<Oportunidad>> CrearAsync(
        Guid contactoId, Guid? cuentaId, string? titulo, decimal importe,
        Guid? etapaId, DateOnly? previstaCierre, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Resultado.Fallo<Oportunidad>(Error.Validacion("empresa.sin_seleccionar", "No hay empresa activa."));
        }

        var embudo = await embudos.PorDefectoAsync(ct).ConfigureAwait(false);
        if (embudo is null)
        {
            return Resultado.Fallo<Oportunidad>(Error.NoEncontrado("embudo.no_encontrado", "Esta empresa no tiene embudo."));
        }

        var creada = Oportunidad.Crear(
            empresaId, contactoId, cuentaId, titulo, importe, embudo, etapaId, previstaCierre, contexto.UsuarioId, reloj);
        if (creada.Exito)
        {
            oportunidades.Anadir(creada.Valor);
        }

        return creada;
    }

    public async Task<Resultado<Oportunidad>> MoverAsync(Guid id, Guid etapaId, CancellationToken ct = default)
    {
        var oportunidad = await oportunidades.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (oportunidad is null)
        {
            return Resultado.Fallo<Oportunidad>(Error.NoEncontrado("oportunidad.no_encontrada", "La oportunidad no existe."));
        }

        var embudo = await embudos.BuscarPorIdAsync(oportunidad.EmbudoId, ct).ConfigureAwait(false);
        if (embudo is null)
        {
            return Resultado.Fallo<Oportunidad>(Error.NoEncontrado("embudo.no_encontrado", "El embudo no existe."));
        }

        var r = oportunidad.Mover(embudo, etapaId, reloj);
        return r.Fallido ? Resultado.Fallo<Oportunidad>(r.Error!) : Resultado.Ok(oportunidad);
    }

    public async Task<Resultado<Oportunidad>> ActualizarAsync(
        Guid id, string? titulo, decimal importe, DateOnly? previstaCierre, Guid? propietarioId, CancellationToken ct = default)
    {
        var oportunidad = await oportunidades.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (oportunidad is null)
        {
            return Resultado.Fallo<Oportunidad>(Error.NoEncontrado("oportunidad.no_encontrada", "La oportunidad no existe."));
        }

        var r = oportunidad.Actualizar(titulo, importe, previstaCierre, propietarioId, reloj);
        return r.Fallido ? Resultado.Fallo<Oportunidad>(r.Error!) : Resultado.Ok(oportunidad);
    }

    public async Task<Resultado<Oportunidad>> GanarAsync(Guid id, CancellationToken ct = default)
    {
        var oportunidad = await oportunidades.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (oportunidad is null)
        {
            return Resultado.Fallo<Oportunidad>(Error.NoEncontrado("oportunidad.no_encontrada", "La oportunidad no existe."));
        }

        var r = oportunidad.Ganar(reloj);
        if (r.Fallido)
        {
            return Resultado.Fallo<Oportunidad>(r.Error!);
        }

        // Cerrar una venta cambia las cifras de todos los informes y el histórico con el que el motor
        // calcula el Encaje. Quién la cerró y cuándo no puede quedar solo en el `cerrada_en`.
        auditoria.Registrar("oportunidad", id, Acciones.OportunidadGanada, new { importe = oportunidad.Importe });
        return Resultado.Ok(oportunidad);
    }

    public async Task<Resultado<Oportunidad>> PerderAsync(Guid id, MotivoPerdida? motivo, string? detalle, CancellationToken ct = default)
    {
        var oportunidad = await oportunidades.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (oportunidad is null)
        {
            return Resultado.Fallo<Oportunidad>(Error.NoEncontrado("oportunidad.no_encontrada", "La oportunidad no existe."));
        }

        var r = oportunidad.Perder(motivo, detalle, reloj);
        if (r.Fallido)
        {
            return Resultado.Fallo<Oportunidad>(r.Error!);
        }

        // El motivo sí; el detalle **no**: es texto libre y ahí escribe la gente cosas como «me lo
        // dijo su mujer por teléfono». En un registro que no se puede borrar, no.
        auditoria.Registrar("oportunidad", id, Acciones.OportunidadPerdida, new { importe = oportunidad.Importe, motivo = motivo?.ToString() });
        return Resultado.Ok(oportunidad);
    }

    public Task<IReadOnlyList<Oportunidad>> DeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        oportunidades.DeContactoAsync(contactoId, ct);
}
