using Matchketing.Campos.Aplicacion;
using Matchketing.Campos.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioCampos(ContextoMatchketing bd) : IRepositorioCampos
{
    public Task<CampoPropio?> CampoAsync(Guid id, CancellationToken ct = default) =>
        bd.Campos.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<CampoPropio>> CamposAsync(Ambito ambito, CancellationToken ct = default) =>
        await bd.Campos
            .Where(c => c.Ambito == ambito)
            .OrderBy(c => c.Orden)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<CampoPropio>> TodosAsync(CancellationToken ct = default) =>
        await bd.Campos
            .OrderBy(c => c.Ambito)
            .ThenBy(c => c.Orden)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void Anadir(CampoPropio campo) => bd.Campos.Add(campo);

    public void Quitar(CampoPropio campo) => bd.Campos.Remove(campo);

    public Task<ValorCampo?> ValorAsync(Guid campoId, Guid entidadId, CancellationToken ct = default) =>
        bd.ValoresCampo.FirstOrDefaultAsync(v => v.CampoId == campoId && v.EntidadId == entidadId, ct);

    public async Task<IReadOnlyList<ValorCampo>> ValoresDeAsync(
        Ambito ambito, Guid entidadId, CancellationToken ct = default) =>
        await bd.ValoresCampo
            .Where(v => v.Ambito == ambito && v.EntidadId == entidadId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void Anadir(ValorCampo valor) => bd.ValoresCampo.Add(valor);

    public void Quitar(ValorCampo valor) => bd.ValoresCampo.Remove(valor);

    public async Task<int> QuitarValoresDeAsync(Guid campoId, CancellationToken ct = default)
    {
        var suyos = await bd.ValoresCampo.Where(v => v.CampoId == campoId).ToListAsync(ct).ConfigureAwait(false);
        bd.ValoresCampo.RemoveRange(suyos);
        return suyos.Count;
    }

    public Task<int> CuantosRellenosAsync(Guid campoId, CancellationToken ct = default) =>
        bd.ValoresCampo.CountAsync(v => v.CampoId == campoId, ct);

    /// <summary>
    /// Cuántos valores guardados se quedarían fuera de esa lista de opciones.
    ///
    /// La comparación se hace **en memoria y sin acentos**, igual que la del dominio. En SQL habría que
    /// elegir entre `= ANY(...)` —que distingue mayúsculas y tildes, y daría falsos positivos en cuanto
    /// alguien cambie «Electrica» por «Eléctrica»— o montar la normalización dentro de la consulta. Los
    /// valores de un campo son como mucho unos miles: traerlos cuesta menos que acertar con eso.
    /// </summary>
    public async Task<int> CuantosFueraDeAsync(
        Guid campoId, IReadOnlyList<string> opciones, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opciones);

        var guardados = await bd.ValoresCampo
            .Where(v => v.CampoId == campoId)
            .Select(v => v.Texto)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var validas = opciones
            .Select(Nucleo.Comun.Castellano.SinAcentos)
            .ToHashSet(StringComparer.Ordinal);

        return guardados.Count(t => !validas.Contains(Nucleo.Comun.Castellano.SinAcentos(t)));
    }
}

/// <summary>
/// ¿Existe ese contacto o esa cuenta en esta empresa?
///
/// Vive aquí y no en el módulo de campos porque cruza con contactos, y ningún módulo de negocio conoce a
/// otro. Los dos filtros globales por empresa hacen el trabajo: un identificador de otra empresa no
/// existe desde aquí, que es exactamente lo que hace falta.
/// </summary>
public sealed class ConsultaExisteLaEntidad(ContextoMatchketing bd) : IExisteLaEntidad
{
    public Task<bool> ExisteAsync(Ambito ambito, Guid entidadId, CancellationToken ct = default) =>
        ambito == Ambito.Contacto
            ? bd.Contactos.AnyAsync(c => c.Id == entidadId, ct)
            : bd.Cuentas.AnyAsync(c => c.Id == entidadId, ct);
}
