using Matchketing.Organizacion.Aplicacion;
using Matchketing.Organizacion.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioEmpresas(ContextoMatchketing bd) : IRepositorioEmpresas
{
    public Task<Empresa?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Empresas.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<Empresa>> DeIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        await bd.Empresas.Where(e => ids.Contains(e.Id)).OrderBy(e => e.Nombre).ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(Empresa empresa) => bd.Empresas.Add(empresa);
}
