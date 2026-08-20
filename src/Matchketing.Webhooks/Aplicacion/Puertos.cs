using Matchketing.Webhooks.Dominio;

namespace Matchketing.Webhooks.Aplicacion;

public interface IRepositorioWebhooks
{
    Task<SuscripcionWebhook?> PorIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<SuscripcionWebhook>> DeLaEmpresaAsync(CancellationToken ct = default);

    /// <summary>
    /// Las que escuchan este evento y están activas. Es la consulta del camino caliente: se hace en
    /// cada cambio de negocio que puede emitir, así que va por índice y devuelve poco.
    /// </summary>
    Task<IReadOnlyList<SuscripcionWebhook>> QueEscuchanAsync(TipoEvento tipo, CancellationToken ct = default);

    void Anadir(SuscripcionWebhook suscripcion);

    void Quitar(SuscripcionWebhook suscripcion);

    Task<IReadOnlyList<Entrega>> PendientesAsync(DateTimeOffset hasta, int tope, CancellationToken ct = default);

    Task<IReadOnlyList<Entrega>> UltimasDeAsync(Guid suscripcionId, int cuantas, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, int>> PendientesPorSuscripcionAsync(CancellationToken ct = default);

    void AnadirEntrega(Entrega entrega);
}

/// <summary>
/// Hace el POST. Lo implementa la infraestructura porque habla HTTP, y **solo** por eso: la firma y la
/// política de reintentos están en el dominio, donde se pueden probar sin red.
/// </summary>
public interface IEnviaWebhook
{
    Task<ResultadoEntrega> EnviarAsync(SuscripcionWebhook suscripcion, Entrega entrega, CancellationToken ct = default);
}
