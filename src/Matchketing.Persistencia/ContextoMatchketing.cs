using Matchketing.Contactos.Dominio;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Organizacion.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia;

/// <summary>
/// Contexto único de la aplicación. Cada módulo aporta sus configuraciones y usa su propio esquema
/// de PostgreSQL, de forma que las fronteras entre módulos también se ven en la base de datos.
/// </summary>
public sealed class ContextoMatchketing(
    DbContextOptions<ContextoMatchketing> opciones, IContextoEmpresa contexto, IReloj reloj)
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

    public DbSet<Embudo.Dominio.PasoEtapa> PasosEtapa => Set<Embudo.Dominio.PasoEtapa>();

    public DbSet<Tareas.Dominio.Tarea> Tareas => Set<Tareas.Dominio.Tarea>();

    public DbSet<Match.Dominio.Senal> Senales => Set<Match.Dominio.Senal>();

    public DbSet<Match.Dominio.PuntuacionMatch> Puntuaciones => Set<Match.Dominio.PuntuacionMatch>();

    public DbSet<Captacion.Dominio.Formulario> Formularios => Set<Captacion.Dominio.Formulario>();

    public DbSet<Captacion.Dominio.EnvioFormulario> Envios => Set<Captacion.Dominio.EnvioFormulario>();

    public DbSet<Cumplimiento.Dominio.Consentimiento> Consentimientos => Set<Cumplimiento.Dominio.Consentimiento>();

    public DbSet<Auditoria.Dominio.RegistroAuditoria> RegistrosAuditoria => Set<Auditoria.Dominio.RegistroAuditoria>();

    public DbSet<Repaso.Dominio.Pospuesta> Pospuestas => Set<Repaso.Dominio.Pospuesta>();

    public DbSet<Avisos.Dominio.SuscripcionAviso> Suscripciones => Set<Avisos.Dominio.SuscripcionAviso>();

    public DbSet<Webhooks.Dominio.SuscripcionWebhook> Webhooks => Set<Webhooks.Dominio.SuscripcionWebhook>();

    public DbSet<Webhooks.Dominio.Entrega> EntregasWebhook => Set<Webhooks.Dominio.Entrega>();

    /// <summary>Empresa activa de la petición. La usan los filtros globales de los módulos de negocio.</summary>
    public Guid? EmpresaActual => contexto.EmpresaId;

    /// <summary>
    /// El único guardado de la aplicación, y por eso el sitio donde se despachan los eventos de
    /// dominio: pasa por aquí lo que hace un endpoint y lo que hace el repaso, sin excepción.
    ///
    /// Va antes de `SaveChangesAsync` **a propósito**, no en un interceptor: así las filas de entrega
    /// que se añadan entran en el mismo `SaveChanges` que el cambio que las provocó, y no hay ninguna
    /// duda sobre si EF las ve o no. Un interceptor de guardado corre cuando los cambios ya se han
    /// recogido, y ahí el orden depende de detalles internos que no conviene apostar.
    /// </summary>
    public async Task<int> GuardarCambiosAsync(CancellationToken ct = default)
    {
        await DespachadorEventos.DespacharAsync(this, reloj, ct).ConfigureAwait(false);
        return await SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Vuelve a fijar <c>app.empresa_actual</c> en la conexión que ya esté abierta. Lo necesita la
    /// entrada pública de leads: la empresa se conoce **después** de leer el formulario, y para
    /// entonces la conexión puede llevar abierta desde antes con el valor vacío.
    /// </summary>
    public async Task ReaplicarEmpresaAsync(CancellationToken ct = default)
    {
        var conexion = Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open)
        {
            return;
        }

        await using var orden = conexion.CreateCommand();
        orden.CommandText = "SELECT set_config('app.empresa_actual', $1, false)";
        var p = orden.CreateParameter();
        p.Value = EmpresaActual?.ToString() ?? string.Empty;
        orden.Parameters.Add(p);
        await orden.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

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
        modelo.Entity<Captacion.Dominio.Formulario>().HasQueryFilter(f => f.EmpresaId == EmpresaActual);
        modelo.Entity<Captacion.Dominio.EnvioFormulario>().HasQueryFilter(e => e.EmpresaId == EmpresaActual);
        modelo.Entity<Cumplimiento.Dominio.Consentimiento>().HasQueryFilter(c => c.EmpresaId == EmpresaActual);
        modelo.Entity<Auditoria.Dominio.RegistroAuditoria>().HasQueryFilter(r => r.EmpresaId == EmpresaActual);
        modelo.Entity<Repaso.Dominio.Pospuesta>().HasQueryFilter(p => p.EmpresaId == EmpresaActual);
        modelo.Entity<Avisos.Dominio.SuscripcionAviso>().HasQueryFilter(s => s.EmpresaId == EmpresaActual);
        modelo.Entity<Webhooks.Dominio.SuscripcionWebhook>().HasQueryFilter(s => s.EmpresaId == EmpresaActual);
        modelo.Entity<Webhooks.Dominio.Entrega>().HasQueryFilter(e => e.EmpresaId == EmpresaActual);

        // Los identificadores los genera **el dominio**, nunca la base: todos los agregados hacen
        // `Guid.NewGuid()` al crearse. Hay que decírselo a EF, porque si cree que los genera la base
        // usa la heurística «clave distinta de vacío ⇒ la fila ya existe» y, al descubrir una
        // entidad hija nueva colgando de un padre ya rastreado, emite UPDATE en vez de INSERT. Eso
        // falla con «expected to affect 1 row(s), but actually affected 0».
        foreach (var tipo in modelo.Model.GetEntityTypes())
        {
            var clave = tipo.FindPrimaryKey();
            if (clave?.Properties is [{ ClrType: var t } propiedad] && t == typeof(Guid))
            {
                propiedad.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }
        }

        // Nota deliberada: `identidad.membresia` NO lleva filtro global por empresa. Es la tabla que
        // decide a qué empresas puede entrar un usuario, así que filtrarla por la empresa activa
        // impediría listar las empresas entre las que elegir.
        base.OnModelCreating(modelo);
    }
}
