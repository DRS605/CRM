using Matchketing.Nucleo.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Matchketing.Persistencia;

/// <summary>
/// Solo para las herramientas de EF Core (crear migraciones). En ejecución real el contexto recibe
/// el <see cref="IContextoEmpresa"/> de la petición.
/// </summary>
public sealed class FabricaContextoDisenno : IDesignTimeDbContextFactory<ContextoMatchketing>
{
    public ContextoMatchketing CreateDbContext(string[] args)
    {
        var cadena = Environment.GetEnvironmentVariable("MATCHKETING_CONEXION")
            ?? "Host=localhost;Port=5432;Database=matchketing;Username=postgres;Password=postgres";

        var opciones = new DbContextOptionsBuilder<ContextoMatchketing>().UseNpgsql(cadena).Options;
        return new ContextoMatchketing(opciones, new ContextoEmpresaVacio());
    }

    private sealed class ContextoEmpresaVacio : IContextoEmpresa
    {
        public Guid? EmpresaId => null;

        public Guid? UsuarioId => null;

        public IReadOnlyCollection<string> Permisos => [];

        public bool Tiene(string permiso) => false;
    }
}
