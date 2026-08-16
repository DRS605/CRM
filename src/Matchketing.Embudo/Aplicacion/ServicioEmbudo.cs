using Matchketing.Embudo.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Embudo.Aplicacion;

public sealed class ServicioEmbudo(
    IRepositorioEmbudos embudos,
    IRepositorioOportunidades oportunidades,
    IConsultaEmbudo consulta,
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

    public Task<InformeMotivos> MotivosAsync(CancellationToken ct = default) => consulta.MotivosAsync(ct);

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
        return r.Fallido ? Resultado.Fallo<Oportunidad>(r.Error!) : Resultado.Ok(oportunidad);
    }

    public async Task<Resultado<Oportunidad>> PerderAsync(Guid id, MotivoPerdida? motivo, string? detalle, CancellationToken ct = default)
    {
        var oportunidad = await oportunidades.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (oportunidad is null)
        {
            return Resultado.Fallo<Oportunidad>(Error.NoEncontrado("oportunidad.no_encontrada", "La oportunidad no existe."));
        }

        var r = oportunidad.Perder(motivo, detalle, reloj);
        return r.Fallido ? Resultado.Fallo<Oportunidad>(r.Error!) : Resultado.Ok(oportunidad);
    }

    public Task<IReadOnlyList<Oportunidad>> DeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        oportunidades.DeContactoAsync(contactoId, ct);
}
