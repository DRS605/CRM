using Matchketing.Match.Aplicacion;
using Matchketing.Match.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioSenales(ContextoMatchketing bd) : IRepositorioSenales
{
    public void Anadir(Senal senal) => bd.Senales.Add(senal);

    public async Task<IReadOnlyList<SenalPuntuable>> DeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        await bd.Senales
            .Where(s => s.ContactoId == contactoId)
            .Select(s => new SenalPuntuable(s.Tipo, s.OcurridaEn))
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<SenalPuntuable>>> DeVariosAsync(
        IReadOnlyCollection<Guid> contactos, CancellationToken ct = default)
    {
        var filas = await bd.Senales
            .Where(s => contactos.Contains(s.ContactoId))
            .Select(s => new { s.ContactoId, s.Tipo, s.OcurridaEn })
            .ToListAsync(ct).ConfigureAwait(false);

        return filas
            .GroupBy(f => f.ContactoId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SenalPuntuable>)g.Select(f => new SenalPuntuable(f.Tipo, f.OcurridaEn)).ToList());
    }
}

public sealed class RepositorioPuntuaciones(ContextoMatchketing bd) : IRepositorioPuntuaciones
{
    public Task<PuntuacionMatch?> DeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        bd.Puntuaciones.FirstOrDefaultAsync(p => p.ContactoId == contactoId, ct);

    public void Anadir(PuntuacionMatch puntuacion) => bd.Puntuaciones.Add(puntuacion);
}
