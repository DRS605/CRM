using Matchketing.Contactos.Dominio;
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

    public DbSet<Cuenta> Cuentas => Set<Cuenta>();

    public DbSet<Contacto> Contactos => Set<Contacto>();

    public DbSet<Actividad> Actividades => Set<Actividad>();

    public DbSet<Embudo.Dominio.Embudo> Embudos => Set<Embudo.Dominio.Embudo>();

    public DbSet<Embudo.Dominio.Etapa> Etapas => Set<Embudo.Dominio.Etapa>();

    public DbSet<Embudo.Dominio.Oportunidad> Oportunidades => Set<Embudo.Dominio.Oportunidad>();

    public DbSet<Tareas.Dominio.Tarea> Tareas => Set<Tareas.Dominio.Tarea>();

    public DbSet<Match.Dominio.Senal> Senales => Set<Match.Dominio.Senal>();

    public DbSet<Match.Dominio.PuntuacionMatch> Puntuaciones => Set<Match.Dominio.PuntuacionMatch>();

    /// <summary>Empresa activa de la petición. La usan los filtros globales de los módulos de negocio.</summary>
    public Guid? EmpresaActual => contexto.EmpresaId;

    public Task<int> GuardarCambiosAsync(CancellationToken ct = default) => SaveChangesAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        modelo.HasDefaultSchema("publico");
        modelo.ApplyConfigurationsFromAssembly(typeof(ContextoMatchketing).Assembly);

        // Primera barrera del aislamiento entre empresas: imposible olvidarse del WHERE. Si no hay
        // empresa activa, EmpresaActual es null y no casa con ninguna fila: falla cerrado.
        modelo.Entity<Cuenta>().HasQueryFilter(c => c.EmpresaId == EmpresaActual);
        modelo.Entity<Contacto>().HasQueryFilter(c => c.EmpresaId == EmpresaActual);
        modelo.Entity<Actividad>().HasQueryFilter(a => a.EmpresaId == EmpresaActual);
        modelo.Entity<Embudo.Dominio.Embudo>().HasQueryFilter(e => e.EmpresaId == EmpresaActual);
        modelo.Entity<Embudo.Dominio.Oportunidad>().HasQueryFilter(o => o.EmpresaId == EmpresaActual);
        modelo.Entity<Tareas.Dominio.Tarea>().HasQueryFilter(t => t.EmpresaId == EmpresaActual);
        modelo.Entity<Match.Dominio.Senal>().HasQueryFilter(s => s.EmpresaId == EmpresaActual);
        modelo.Entity<Match.Dominio.PuntuacionMatch>().HasQueryFilter(p => p.EmpresaId == EmpresaActual);

        // Nota deliberada: `identidad.membresia` NO lleva filtro global por empresa. Es la tabla que
        // decide a qué empresas puede entrar un usuario, así que filtrarla por la empresa activa
        // impediría listar las empresas entre las que elegir.
        base.OnModelCreating(modelo);
    }
}
