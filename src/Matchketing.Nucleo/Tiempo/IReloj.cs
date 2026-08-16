namespace Matchketing.Nucleo.Tiempo;

/// <summary>El reloj se inyecta para poder probar el decaimiento del Momento sin esperas reales.</summary>
public interface IReloj
{
    DateTimeOffset AhoraUtc { get; }
}

public sealed class RelojSistema : IReloj
{
    public DateTimeOffset AhoraUtc => DateTimeOffset.UtcNow;
}
