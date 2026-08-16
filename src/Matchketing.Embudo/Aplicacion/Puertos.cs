using Matchketing.Embudo.Dominio;

namespace Matchketing.Embudo.Aplicacion;

public interface IRepositorioEmbudos
{
    Task<Dominio.Embudo?> BuscarPorIdAsync(Guid id, CancellationToken ct = default);

    Task<Dominio.Embudo?> PorDefectoAsync(CancellationToken ct = default);

    void Anadir(Dominio.Embudo embudo);
}

public interface IRepositorioOportunidades
{
    Task<Oportunidad?> BuscarPorIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Oportunidad>> DeContactoAsync(Guid contactoId, CancellationToken ct = default);

    void Anadir(Oportunidad oportunidad);
}

public interface IConsultaEmbudo
{
    Task<Tablero?> TableroAsync(Guid? embudoId, CancellationToken ct = default);

    Task<InformeMotivos> MotivosAsync(CancellationToken ct = default);
}
