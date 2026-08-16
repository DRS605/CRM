using Matchketing.Match.Dominio;

namespace Matchketing.Match.Aplicacion;

public interface IRepositorioSenales
{
    void Anadir(Senal senal);

    Task<IReadOnlyList<SenalPuntuable>> DeContactoAsync(Guid contactoId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<SenalPuntuable>>> DeVariosAsync(IReadOnlyCollection<Guid> contactos, CancellationToken ct = default);
}

public interface IRepositorioPuntuaciones
{
    Task<PuntuacionMatch?> DeContactoAsync(Guid contactoId, CancellationToken ct = default);

    void Anadir(PuntuacionMatch puntuacion);
}

/// <summary>
/// Las lecturas que el motor necesita y que cruzan módulos: el perfil de lo que se gana, los datos
/// del contacto y los comerciales con su histórico. Se implementa en persistencia.
/// </summary>
public interface IConsultaMatch
{
    Task<PerfilGanadas> PerfilAsync(CancellationToken ct = default);

    Task<DatosContacto?> DatosDeAsync(Guid contactoId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, DatosContacto>> DatosDeVariosAsync(IReadOnlyCollection<Guid> contactos, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> ContactosActivosAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CandidatoComercial>> ComercialesAsync(string? sector, CancellationToken ct = default);

    /// <summary>Peso del Encaje configurado por la empresa. Por defecto, mitad y mitad.</summary>
    Task<decimal> PesoEncajeAsync(CancellationToken ct = default);
}
