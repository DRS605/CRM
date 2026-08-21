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
    DbContextOptions<ContextoMatchketing> opciones, IContextoEmpresa contexto, IReloj reloj,
    IServiceProvider servicios)
    : DbContext(opciones), IUnidadDeTrabajo
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    public DbSet<Membresia> Membresias => Set<Membresia>();

    public DbSet<Invitacion> Invitaciones => Set<Invitacion>();

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

    public DbSet<Correo.Dominio.Plantilla> Plantillas => Set<Correo.Dominio.Plantilla>();

    public DbSet<Correo.Dominio.Correo> Mensajes => Set<Correo.Dominio.Correo>();

    public DbSet<Automatizacion.Dominio.Regla> Reglas => Set<Automatizacion.Dominio.Regla>();

    public DbSet<Automatizacion.Dominio.Ejecucion> Ejecuciones => Set<Automatizacion.Dominio.Ejecucion>();

    public DbSet<Campanias.Dominio.Segmento> Segmentos => Set<Campanias.Dominio.Segmento>();

    public DbSet<Campanias.Dominio.Campania> Campanias => Set<Campanias.Dominio.Campania>();

    public DbSet<Campanias.Dominio.EnvioCampania> EnviosCampania => Set<Campanias.Dominio.EnvioCampania>();

    public DbSet<Objetivos.Dominio.Objetivo> Objetivos => Set<Objetivos.Dominio.Objetivo>();

    public DbSet<Campos.Dominio.CampoPropio> Campos => Set<Campos.Dominio.CampoPropio>();

    public DbSet<Campos.Dominio.ValorCampo> ValoresCampo => Set<Campos.Dominio.ValorCampo>();

    /// <summary>Empresa activa de la petición. La usan los filtros globales de los módulos de negocio.</summary>
    public Guid? EmpresaActual => contexto.EmpresaId;

    /// <summary>
    /// El único guardado de la aplicación, y por eso el sitio donde se despachan los eventos de dominio:
    /// pasa por aquí lo que hace un endpoint y lo que hace el repaso, sin excepción.
    ///
    /// Hay **dos momentos** distintos y la diferencia importa:
    ///
    /// · Los **webhooks** se resuelven antes de guardar. Sus filas de entrega son escrituras sueltas y así
    ///   entran en el mismo `SaveChanges` que el cambio que las provocó: el buzón de salida y el hecho no
    ///   se pueden separar.
    /// · Las **reglas** se ejecutan después. Sus acciones pasan por los servicios de contactos, correo y
    ///   tareas, y esos servicios cargan de la base el contacto sobre el que actúan. Con el contacto
    ///   todavía sin guardar, tres de las cuatro acciones fallaban en silencio.
    ///
    /// Cuando hay reglas que ejecutar, los dos guardados van dentro de una transacción, así que el cambio
    /// de negocio y lo que provoca entran o no entran juntos. Cuando no hay ninguna —el caso de casi todo
    /// el mundo— se guarda una sola vez y no se abre nada.
    /// </summary>
    public async Task<int> GuardarCambiosAsync(CancellationToken ct = default)
    {
        var ocurrencias = await DespachadorEventos.DespacharAsync(this, reloj, ct).ConfigureAwait(false);

        // El proveedor resuelve `ServicioAutomatizacion` **aquí y no en el constructor**: ese servicio
        // depende de un repositorio que depende de este mismo contexto, así que pedirlo al construir sería
        // un ciclo. Dentro de un método no lo es, porque la instancia ya existe.
        if (ocurrencias.Count == 0
            || servicios.GetService(typeof(Automatizacion.Aplicacion.ServicioAutomatizacion))
                is not Automatizacion.Aplicacion.ServicioAutomatizacion automatizacion
            || !await automatizacion.HayReglasParaAsync(ocurrencias, ct).ConfigureAwait(false))
        {
            return await SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // Si quien llama ya abrió una transacción —el trabajo de retención lo hace— se usa la suya.
        var propia = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(ct).ConfigureAwait(false)
            : null;

        try
        {
            var filas = await SaveChangesAsync(ct).ConfigureAwait(false);
            await DespachadorEventos.AutomatizarAsync(this, servicios, ocurrencias, ct).ConfigureAwait(false);
            filas += await SaveChangesAsync(ct).ConfigureAwait(false);

            if (propia is not null)
            {
                await propia.CommitAsync(ct).ConfigureAwait(false);
            }

            return filas;
        }
        finally
        {
            if (propia is not null)
            {
                await propia.DisposeAsync().ConfigureAwait(false);
            }
        }
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
        modelo.Entity<Correo.Dominio.Plantilla>().HasQueryFilter(p => p.EmpresaId == EmpresaActual);
        modelo.Entity<Correo.Dominio.Correo>().HasQueryFilter(c => c.EmpresaId == EmpresaActual);
        modelo.Entity<Automatizacion.Dominio.Regla>().HasQueryFilter(r => r.EmpresaId == EmpresaActual);
        modelo.Entity<Automatizacion.Dominio.Ejecucion>().HasQueryFilter(e => e.EmpresaId == EmpresaActual);
        modelo.Entity<Campanias.Dominio.Segmento>().HasQueryFilter(s => s.EmpresaId == EmpresaActual);
        modelo.Entity<Campanias.Dominio.Campania>().HasQueryFilter(c => c.EmpresaId == EmpresaActual);
        modelo.Entity<Campanias.Dominio.EnvioCampania>().HasQueryFilter(e => e.EmpresaId == EmpresaActual);
        modelo.Entity<Objetivos.Dominio.Objetivo>().HasQueryFilter(o => o.EmpresaId == EmpresaActual);
        modelo.Entity<Campos.Dominio.CampoPropio>().HasQueryFilter(c => c.EmpresaId == EmpresaActual);
        modelo.Entity<Campos.Dominio.ValorCampo>().HasQueryFilter(v => v.EmpresaId == EmpresaActual);

        // La invitación **sí** lleva filtro, al contrario que la membresía: es un dato de una empresa
        // concreta. El endpoint público que la lee fija la empresa antes de consultar, sacándola del
        // propio token, así que la consulta va con las dos barreras puestas.
        modelo.Entity<Invitacion>().HasQueryFilter(i => i.EmpresaId == EmpresaActual);

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
