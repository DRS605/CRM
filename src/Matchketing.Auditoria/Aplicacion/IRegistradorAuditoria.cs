namespace Matchketing.Auditoria.Aplicacion;

/// <summary>Una línea del registro, ya con el nombre de quien la provocó resuelto.</summary>
public sealed record LineaAuditoria(Guid Id, Guid? ActorId, string Actor, string Entidad, Guid? EntidadId, string Accion, string? Detalle, DateTimeOffset En);

/// <summary>
/// Anota una acción crítica. **No guarda por su cuenta**: deja el apunte en la misma unidad de
/// trabajo que la operación, para que los dos se confirmen o se deshagan juntos. Auditar en una
/// transacción aparte produce el peor registro posible: uno que puede mentir en los dos sentidos.
/// </summary>
public interface IRegistradorAuditoria
{
    /// <summary>
    /// Anota lo que acaba de hacer el usuario de la petición. Si no hay empresa activa no anota
    /// nada: sin empresa no hay operación de negocio que auditar.
    /// </summary>
    void Registrar(string entidad, Guid? entidadId, string accion, object? detalle = null);

    /// <summary>Para el sistema: trabajos nocturnos y acciones públicas, sin usuario detrás.</summary>
    void RegistrarDelSistema(Guid empresaId, string entidad, Guid? entidadId, string accion, object? detalle = null);

    Task<IReadOnlyList<LineaAuditoria>> UltimosAsync(int cuantos, CancellationToken ct = default);
}
