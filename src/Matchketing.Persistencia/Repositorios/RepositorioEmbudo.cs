using Matchketing.Embudo.Aplicacion;
using Matchketing.Embudo.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioEmbudos(ContextoMatchketing bd) : IRepositorioEmbudos
{
    public Task<Embudo.Dominio.Embudo?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Embudos.Include(e => e.Etapas).FirstOrDefaultAsync(e => e.Id == id, ct);

    public Task<Embudo.Dominio.Embudo?> PorDefectoAsync(CancellationToken ct = default) =>
        bd.Embudos.Include(e => e.Etapas).OrderByDescending(e => e.PorDefecto).FirstOrDefaultAsync(ct);

    public void Anadir(Embudo.Dominio.Embudo embudo) => bd.Embudos.Add(embudo);
}

public sealed class RepositorioOportunidades(ContextoMatchketing bd) : IRepositorioOportunidades
{
    public Task<Oportunidad?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Oportunidades.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Oportunidad>> DeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        await bd.Oportunidades.Where(o => o.ContactoId == contactoId)
            .OrderByDescending(o => o.CreadoEn).ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(Oportunidad oportunidad) => bd.Oportunidades.Add(oportunidad);
}
