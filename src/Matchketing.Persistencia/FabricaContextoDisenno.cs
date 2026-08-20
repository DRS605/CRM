using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
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
        // Un proveedor que no tiene nada: las herramientas de EF no ejecutan reglas ni webhooks, solo
        // leen el modelo. Antes esto era un parámetro opcional con valor por defecto, y ahí estaba el
        // fallo: EF rellenaba el valor por defecto en vez de resolver el servicio, así que en producción
        // el proveedor llegaba **nulo** y las automatizaciones no se ejecutaban nunca. Sin ningún error.
        return new ContextoMatchketing(opciones, new ContextoEmpresaVacio(), new RelojSistema(), new SinServicios());
    }

    private sealed class SinServicios : IServiceProvider
    {
        public object? GetService(Type servicioTipo) => null;
    }

    private sealed class ContextoEmpresaVacio : IContextoEmpresa
    {
        public Guid? EmpresaId => null;

        public Guid? UsuarioId => null;

        public IReadOnlyCollection<string> Permisos => [];

        public bool Tiene(string permiso) => false;
    }
}
