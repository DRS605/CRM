using Matchketing.Webhooks.Aplicacion;
using Matchketing.Webhooks.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioWebhooks(ContextoMatchketing bd) : IRepositorioWebhooks
{
    public Task<SuscripcionWebhook?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Webhooks.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<SuscripcionWebhook>> DeLaEmpresaAsync(CancellationToken ct = default) =>
        await bd.Webhooks.ToListAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Las activas de la empresa, filtrando los tipos **en memoria**.
    ///
    /// Los tipos viven en una columna de texto separada por comas, así que preguntarlos en SQL sería un
    /// `LIKE '%oportunidad.ganada%'` —que además casaría con nombres que empiecen igual—. Traer las
    /// activas de una empresa son como mucho veinte filas cortas por el índice
    /// `ix_webhook_empresa_activa`, y filtrarlas aquí es exacto y no cuesta nada. Si algún día hicieran
    /// falta cientos de suscripciones, esto sería lo primero que habría que cambiar.
    /// </summary>
    public async Task<IReadOnlyList<SuscripcionWebhook>> QueEscuchanAsync(TipoEvento tipo, CancellationToken ct = default)
    {
        var activas = await bd.Webhooks.Where(s => s.Activa).ToListAsync(ct).ConfigureAwait(false);
        return activas.Where(s => s.Escucha(tipo)).ToList();
    }

    public void Anadir(SuscripcionWebhook suscripcion) => bd.Webhooks.Add(suscripcion);

    public void Quitar(SuscripcionWebhook suscripcion) => bd.Webhooks.Remove(suscripcion);

    /// <summary>
    /// Las entregas que ya les toca, **las más viejas primero**. El orden importa: dos eventos de la
    /// misma oportunidad tienen que salir como ocurrieron, y con un tope por pasada un orden arbitrario
    /// podría dejar la primera para la pasada siguiente y mandar la segunda ya.
    /// </summary>
    public async Task<IReadOnlyList<Entrega>> PendientesAsync(DateTimeOffset hasta, int tope, CancellationToken ct = default) =>
        await bd.EntregasWebhook
            .Where(e => e.Estado == EstadoEntrega.Pendiente && e.ProximoIntentoEn != null && e.ProximoIntentoEn <= hasta)
            .OrderBy(e => e.CreadaEn)
            .Take(tope)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Entrega>> UltimasDeAsync(Guid suscripcionId, int cuantas, CancellationToken ct = default) =>
        await bd.EntregasWebhook
            .Where(e => e.SuscripcionId == suscripcionId)
            .OrderByDescending(e => e.CreadaEn)
            .Take(cuantas)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, int>> PendientesPorSuscripcionAsync(CancellationToken ct = default)
    {
        var cuentas = await bd.EntregasWebhook
            .Where(e => e.Estado == EstadoEntrega.Pendiente)
            .GroupBy(e => e.SuscripcionId)
            .Select(g => new { g.Key, Cuantas = g.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return cuentas.ToDictionary(x => x.Key, x => x.Cuantas);
    }

    public void AnadirEntrega(Entrega entrega) => bd.EntregasWebhook.Add(entrega);
}
