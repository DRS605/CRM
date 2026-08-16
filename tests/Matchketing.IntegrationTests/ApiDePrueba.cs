using Matchketing.Persistencia;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// Levanta la API real contra un PostgreSQL real. La cadena se puede sobrescribir con la variable
/// de entorno MATCHKETING_TEST_CONEXION.
/// </summary>
public sealed class ApiDePrueba : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static string Conexion =>
        Environment.GetEnvironmentVariable("MATCHKETING_TEST_CONEXION")
        ?? "Host=localhost;Port=5432;Database=matchketing_test;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        // Production evita que el arranque aplique migraciones por su cuenta: las aplicamos aquí,
        // una sola vez, antes de que corra ninguna prueba.
        constructor.UseEnvironment(Environments.Production);
        constructor.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Matchketing"] = Conexion,
            }));
    }

    public async Task InitializeAsync()
    {
        using var alcance = Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();
        await bd.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await bd.Database.MigrateAsync().ConfigureAwait(false);
    }

    public new async Task DisposeAsync() => await base.DisposeAsync().ConfigureAwait(false);
}
