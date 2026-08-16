using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Identidad.Tests;

/// <summary>Reloj controlado: el tiempo no se espera, se decide.</summary>
public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; private set; } = ahora;

    public void Avanzar(TimeSpan cuanto) => AhoraUtc = AhoraUtc.Add(cuanto);
}
