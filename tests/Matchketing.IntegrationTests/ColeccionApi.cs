using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// Una sola instancia de la API para todas las clases de prueba. Si cada clase creara la suya,
/// cada una borraría y recrearía la base mientras la otra la está usando.
/// </summary>
[CollectionDefinition(Nombre)]
public sealed class ColeccionApi : ICollectionFixture<ApiDePrueba>
{
    public const string Nombre = "api";
}
