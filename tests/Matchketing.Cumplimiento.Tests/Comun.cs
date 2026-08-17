using Matchketing.Auditoria.Aplicacion;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Cumplimiento.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Cumplimiento.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; set; } = ahora;
}

public sealed class ContextoDePrueba(Guid? empresaId, Guid? usuarioId = null) : IContextoEmpresa
{
    public Guid? EmpresaId { get; } = empresaId;

    public Guid? UsuarioId { get; } = usuarioId;

    public IReadOnlyCollection<string> Permisos => [];

    public bool Tiene(string permiso) => true;
}

/// <summary>Registra en memoria lo que se auditaría. Los tests comprueban qué queda apuntado.</summary>
public sealed class AuditoriaDePrueba : IRegistradorAuditoria
{
    public List<(Guid? EmpresaId, bool DelSistema, string Entidad, Guid? EntidadId, string Accion, object? Detalle)> Apuntes { get; } = [];

    public void Registrar(string entidad, Guid? entidadId, string accion, object? detalle = null) =>
        Apuntes.Add((null, false, entidad, entidadId, accion, detalle));

    public void RegistrarDelSistema(Guid empresaId, string entidad, Guid? entidadId, string accion, object? detalle = null) =>
        Apuntes.Add((empresaId, true, entidad, entidadId, accion, detalle));

    public Task<IReadOnlyList<LineaAuditoria>> UltimosAsync(int cuantos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LineaAuditoria>>([]);
}

public sealed class ConsentimientosEnMemoria : IRepositorioConsentimientos
{
    public List<Consentimiento> Todos { get; } = [];

    public Task<IReadOnlyList<Consentimiento>> DeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Consentimiento>>(Todos.Where(c => c.ContactoId == contactoId).ToList());

    public Task<Consentimiento?> VigenteAsync(Guid contactoId, FinalidadConsentimiento finalidad, CancellationToken ct = default) =>
        Task.FromResult(Todos
            .Where(c => c.ContactoId == contactoId && c.Finalidad == finalidad && c.Vigente)
            .OrderByDescending(c => c.OtorgadoEn)
            .FirstOrDefault());

    public void Anadir(Consentimiento consentimiento) => Todos.Add(consentimiento);
}

/// <summary>
/// Almacén de mentira con lo justo: si el contacto existe, si está de baja y qué se borró. Suficiente
/// para probar las reglas, que es lo que se prueba aquí; que el SQL borre de verdad se comprueba en
/// los tests de integración, contra PostgreSQL.
/// </summary>
public sealed class AlmacenDePrueba : IAlmacenPersonal
{
    public HashSet<Guid> Contactos { get; } = [];

    public HashSet<Guid> DeBaja { get; } = [];

    public List<Guid> Borrados { get; } = [];

    public List<Guid> Caducados { get; } = [];

    public string? NombreEmpresa { get; set; } = "Reformas Ana";

    public bool EmpresaBorrada { get; private set; }

    public Task<object?> ReunirContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        Task.FromResult<object?>(Contactos.Contains(contactoId) ? new { contacto = contactoId } : null);

    public Task<bool> ExisteContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        Task.FromResult(Contactos.Contains(contactoId));

    public Task<bool?> EstaDeBajaAsync(Guid contactoId, CancellationToken ct = default) =>
        Task.FromResult<bool?>(Contactos.Contains(contactoId) ? DeBaja.Contains(contactoId) : null);

    public Task<bool> DarDeBajaContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        if (!Contactos.Contains(contactoId))
        {
            return Task.FromResult(false);
        }

        DeBaja.Add(contactoId);
        return Task.FromResult(true);
    }

    public Task<string?> NombreEmpresaAsync(CancellationToken ct = default) => Task.FromResult(NombreEmpresa);

    public Task<object> ReunirEmpresaAsync(CancellationToken ct = default) => Task.FromResult<object>(new { empresa = NombreEmpresa });

    public Task<RecuentoBorrado> BorrarContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        Borrados.Add(contactoId);
        Contactos.Remove(contactoId);
        Caducados.Remove(contactoId);
        return Task.FromResult(new RecuentoBorrado(1, 2, 1, 1, 3, 1, 1, 1));
    }

    public Task<RecuentoBorrado> BorrarEmpresaAsync(CancellationToken ct = default)
    {
        EmpresaBorrada = true;
        return Task.FromResult(new RecuentoBorrado(Contactos.Count, 0, 0, 0, 0, 0, 0, 0));
    }

    public Task<IReadOnlyList<Guid>> LeadsCaducadosAsync(DateTimeOffset limite, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(Caducados.ToList());
}

public sealed class RetencionDePrueba(int? meses) : IAjustesRetencion
{
    public Task<int?> MesesRetencionAsync(CancellationToken ct = default) => Task.FromResult(meses);

    public Task<IReadOnlyList<(Guid EmpresaId, int Meses)>> DeTodasLasEmpresasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<(Guid, int)>>([]);
}
