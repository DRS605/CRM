using Matchketing.Organizacion.Dominio;

namespace Matchketing.Organizacion.Aplicacion;

public interface IRepositorioEmpresas
{
    Task<Empresa?> BuscarPorIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Empresa>> DeIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    void Anadir(Empresa empresa);
}
