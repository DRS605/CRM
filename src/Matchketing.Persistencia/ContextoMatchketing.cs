using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Organizacion.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia;

/// <summary>
/// Contexto único de la aplicación. Cada módulo aporta sus configuraciones y usa su propio esquema
/// de PostgreSQL, de forma que las fronteras entre módulos también se ven en la base de datos.
/// </summary>
public sealed class ContextoMatchketing(DbContextOptions<ContextoMatchketing> opciones, IContextoEmpresa contexto)
    : DbContext(opciones), IUnidadDeTrabajo
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    public DbSet<Membresia> Membresias => Set<Membresia>();

    public DbSet<Empresa> Empresas => Set<Empresa>();

    public Task<int> GuardarCambiosAsync(CancellationToken ct = default) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        modelo.HasDefaultSchema("publico");
        modelo.ApplyConfigurationsFromAssembly(typeof(ContextoMatchketing).Assembly);

        // Nota deliberada: `membresia` NO lleva filtro global por empresa. Es la tabla que decide a
        // qué empresas puede entrar un usuario, así que filtrarla por la empresa activa impediría
        // listar las empresas entre las que elegir. El aislamiento de los datos de negocio
        // (contactos, oportunidades…) sí irá por filtro global + RLS, módulo a módulo.
        base.OnModelCreating(modelo);
    }

    /// <summary>Empresa activa de la petición. La usarán los filtros globales de los módulos de negocio.</summary>
    public Guid? EmpresaActual => contexto.EmpresaId;
}
