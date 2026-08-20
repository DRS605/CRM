using Matchketing.Avisos.Dominio;

namespace Matchketing.Avisos.Aplicacion;

public interface IRepositorioSuscripciones
{
    Task<SuscripcionAviso?> PorEndpointAsync(string endpoint, CancellationToken ct = default);

    Task<IReadOnlyList<SuscripcionAviso>> DeUsuarioAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Todas las de la empresa activa. Las recorre el trabajo del viernes.</summary>
    Task<IReadOnlyList<SuscripcionAviso>> DeLaEmpresaAsync(CancellationToken ct = default);

    void Anadir(SuscripcionAviso suscripcion);

    void Quitar(SuscripcionAviso suscripcion);
}

/// <summary>
/// Manda un aviso a un aparato. Lo implementa la infraestructura porque hace una petición HTTP, y
/// **solo** por eso: el cifrado y el token están en el dominio, donde se pueden probar.
/// </summary>
public interface IEmisorAvisos
{
    Task<ResultadoEnvio> EnviarAsync(SuscripcionAviso suscripcion, Aviso aviso, CancellationToken ct = default);
}

/// <summary>
/// Cuántas decisiones tiene pendientes cada comercial de la empresa activa. Es lo único que el módulo
/// de avisos necesita saber del repaso, y así no depende de él.
/// </summary>
public interface IConsultaPendientes
{
    Task<IReadOnlyDictionary<Guid, int>> PorUsuarioAsync(CancellationToken ct = default);
}
