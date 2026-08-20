using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Webhooks.Aplicacion;
using Matchketing.Webhooks.Dominio;

namespace Matchketing.Webhooks.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; set; } = ahora;

    public void Avanzar(TimeSpan cuanto) => AhoraUtc = AhoraUtc.Add(cuanto);
}

public sealed class ContextoDePrueba(Guid? empresaId) : IContextoEmpresa
{
    public Guid? EmpresaId { get; } = empresaId;

    public Guid? UsuarioId { get; } = Guid.NewGuid();

    public IReadOnlyCollection<string> Permisos => [];

    public bool Tiene(string permiso) => true;
}

public sealed class RepositorioEnMemoria(Guid empresaId) : IRepositorioWebhooks
{
    public List<SuscripcionWebhook> Suscripciones { get; } = [];

    public List<Entrega> Entregas { get; } = [];

    public Task<SuscripcionWebhook?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Suscripciones.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<SuscripcionWebhook>> DeLaEmpresaAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SuscripcionWebhook>>(
            Suscripciones.Where(s => s.EmpresaId == empresaId).ToList());

    public Task<IReadOnlyList<SuscripcionWebhook>> QueEscuchanAsync(TipoEvento tipo, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SuscripcionWebhook>>(
            Suscripciones.Where(s => s.EmpresaId == empresaId && s.Escucha(tipo)).ToList());

    public void Anadir(SuscripcionWebhook suscripcion) => Suscripciones.Add(suscripcion);

    public void Quitar(SuscripcionWebhook suscripcion) => Suscripciones.Remove(suscripcion);

    public Task<IReadOnlyList<Entrega>> PendientesAsync(DateTimeOffset hasta, int tope, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Entrega>>(Entregas
            .Where(e => e.LeToca(hasta))
            .OrderBy(e => e.CreadaEn)
            .Take(tope)
            .ToList());

    public Task<IReadOnlyList<Entrega>> UltimasDeAsync(Guid suscripcionId, int cuantas, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Entrega>>(Entregas
            .Where(e => e.SuscripcionId == suscripcionId)
            .OrderByDescending(e => e.CreadaEn)
            .Take(cuantas)
            .ToList());

    public Task<IReadOnlyDictionary<Guid, int>> PendientesPorSuscripcionAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(Entregas
            .Where(e => e.Estado == EstadoEntrega.Pendiente)
            .GroupBy(e => e.SuscripcionId)
            .ToDictionary(g => g.Key, g => g.Count()));

    public void AnadirEntrega(Entrega entrega) => Entregas.Add(entrega);
}

/// <summary>Un receptor de mentira que contesta lo que se le diga y guarda lo que le llega.</summary>
public sealed class EmisorDePrueba : IEnviaWebhook
{
    public List<(SuscripcionWebhook Suscripcion, Entrega Entrega)> Intentos { get; } = [];

    /// <summary>Qué contestar. Por defecto, 200.</summary>
    public Func<Entrega, ResultadoEntrega> Contesta { get; set; } = _ => new ResultadoEntrega(true, 200, null);

    public Task<ResultadoEntrega> EnviarAsync(
        SuscripcionWebhook suscripcion, Entrega entrega, CancellationToken ct = default)
    {
        Intentos.Add((suscripcion, entrega));
        return Task.FromResult(Contesta(entrega));
    }
}
