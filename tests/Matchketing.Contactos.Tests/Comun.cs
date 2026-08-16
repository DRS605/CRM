using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Contactos.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; private set; } = ahora;

    public void Avanzar(TimeSpan cuanto) => AhoraUtc = AhoraUtc.Add(cuanto);
}

public static class Datos
{
    public static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly Guid OtraEmpresa = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static RelojFijo Reloj() => new(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));
}
