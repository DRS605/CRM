using Matchketing.Campos.Aplicacion;
using Matchketing.Campos.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campos.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; set; } = ahora;

    public void Avanzar(TimeSpan cuanto) => AhoraUtc = AhoraUtc.Add(cuanto);
}

public sealed class ContextoDePrueba(Guid? empresaId, Guid? usuarioId = null) : IContextoEmpresa
{
    public Guid? EmpresaId { get; } = empresaId;

    public Guid? UsuarioId { get; } = usuarioId;

    public IReadOnlyCollection<string> Permisos => [];

    public bool Tiene(string permiso) => true;
}

/// <summary>
/// El repositorio en memoria. Imita lo que hace el de verdad, incluido lo que importa: los valores se
/// buscan por campo y entidad, y quitar los valores de un campo los quita todos.
/// </summary>
public sealed class RepositorioEnMemoria : IRepositorioCampos
{
    public List<CampoPropio> Campos { get; } = [];

    public List<ValorCampo> Valores { get; } = [];

    public Task<CampoPropio?> CampoAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Campos.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<CampoPropio>> CamposAsync(Ambito ambito, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CampoPropio>>(Campos
            .Where(c => c.Ambito == ambito)
            .OrderBy(c => c.Orden)
            .ToList());

    public Task<IReadOnlyList<CampoPropio>> TodosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CampoPropio>>(Campos
            .OrderBy(c => c.Ambito)
            .ThenBy(c => c.Orden)
            .ToList());

    public void Anadir(CampoPropio campo) => Campos.Add(campo);

    public void Quitar(CampoPropio campo) => Campos.Remove(campo);

    public Task<ValorCampo?> ValorAsync(Guid campoId, Guid entidadId, CancellationToken ct = default) =>
        Task.FromResult(Valores.FirstOrDefault(v => v.CampoId == campoId && v.EntidadId == entidadId));

    public Task<IReadOnlyList<ValorCampo>> ValoresDeAsync(
        Ambito ambito, Guid entidadId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ValorCampo>>(Valores
            .Where(v => v.Ambito == ambito && v.EntidadId == entidadId)
            .ToList());

    public void Anadir(ValorCampo valor) => Valores.Add(valor);

    public void Quitar(ValorCampo valor) => Valores.Remove(valor);

    public Task<int> QuitarValoresDeAsync(Guid campoId, CancellationToken ct = default) =>
        Task.FromResult(Valores.RemoveAll(v => v.CampoId == campoId));

    public Task<int> CuantosRellenosAsync(Guid campoId, CancellationToken ct = default) =>
        Task.FromResult(Valores.Count(v => v.CampoId == campoId));

    public Task<int> CuantosFueraDeAsync(
        Guid campoId, IReadOnlyList<string> opciones, CancellationToken ct = default) =>
        Task.FromResult(Valores.Count(v =>
            v.CampoId == campoId
            && !opciones.Any(o => Castellano.SinAcentos(o) == Castellano.SinAcentos(v.Texto))));
}

/// <summary>Los contactos y las cuentas que existen, según esta prueba.</summary>
public sealed class EntidadesDePrueba : IExisteLaEntidad
{
    public HashSet<Guid> Contactos { get; } = [];

    public HashSet<Guid> Cuentas { get; } = [];

    public Task<bool> ExisteAsync(Ambito ambito, Guid entidadId, CancellationToken ct = default) =>
        Task.FromResult(ambito == Ambito.Contacto ? Contactos.Contains(entidadId) : Cuentas.Contains(entidadId));
}
