using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Auditoria.Dominio;

/// <summary>Qué se hizo. Lista corta y cerrada: solo se audita lo que de verdad importa.</summary>
public static class Acciones
{
    public const string ContactoFusionado = "contacto.fusionado";
    public const string ContactoAsignado = "contacto.asignado";
    public const string ContactoBaja = "contacto.baja";
    public const string ContactoBorrado = "contacto.borrado";
    public const string ContactoExportado = "contacto.exportado";
    public const string OportunidadGanada = "oportunidad.ganada";
    public const string OportunidadPerdida = "oportunidad.perdida";
    public const string ConsentimientoOtorgado = "consentimiento.otorgado";
    public const string ConsentimientoRetirado = "consentimiento.retirado";
    public const string AjustesCambiados = "ajustes.cambiados";
    public const string RetencionAplicada = "retencion.aplicada";
    public const string EmpresaExportada = "empresa.exportada";
    public const string EmpresaBorrada = "empresa.borrada";

    public static readonly IReadOnlyList<string> Todas =
    [
        ContactoFusionado, ContactoAsignado, ContactoBaja, ContactoBorrado, ContactoExportado,
        OportunidadGanada, OportunidadPerdida,
        ConsentimientoOtorgado, ConsentimientoRetirado,
        AjustesCambiados, RetencionAplicada, EmpresaExportada, EmpresaBorrada,
    ];
}

/// <summary>
/// Quién hizo qué y cuándo. **Append-only**: no hay forma de editarlo ni de borrarlo, porque un
/// registro que se puede tocar no sirve para lo único que sirve un registro.
///
/// Se escribe en la **misma transacción** que la operación auditada: si la operación se deshace, su
/// rastro también, y nunca queda un apunte de algo que no pasó.
///
/// El <see cref="Detalle"/> lleva cifras e identificadores, **nunca datos personales**. No es una
/// convención: el registrador tapa lo que huela a correo o a teléfono antes de guardar, porque un
/// registro que nadie puede borrar es el último sitio donde quieres el correo de alguien.
/// </summary>
public sealed class RegistroAuditoria : EntidadBase<Guid>
{
    public const int LongitudMaximaDetalle = 2000;

    private RegistroAuditoria(Guid id)
        : base(id)
    {
        Entidad = null!;
        Accion = null!;
    }

    private RegistroAuditoria(Guid id, Guid empresaId, Guid? actorId, string entidad, Guid? entidadId, string accion, string? detalle, DateTimeOffset en)
        : base(id)
    {
        EmpresaId = empresaId;
        ActorId = actorId;
        Entidad = entidad;
        EntidadId = entidadId;
        Accion = accion;
        Detalle = detalle;
        En = en;
    }

    /// <summary>
    /// Siempre hay empresa. Todas las acciones auditadas pertenecen a una, y tenerla obligatoria es
    /// lo que permite que la RLS de PostgreSQL proteja también esta tabla: una política que tuviera
    /// que dejar pasar filas sin empresa dejaría abierto justo el hueco por el que se ve todo.
    /// </summary>
    public Guid EmpresaId { get; private set; }

    /// <summary>Quién lo hizo. Nulo cuando lo hizo el sistema: un trabajo nocturno, una baja pública.</summary>
    public Guid? ActorId { get; private set; }

    public string Entidad { get; private set; }

    public Guid? EntidadId { get; private set; }

    public string Accion { get; private set; }

    /// <summary>Qué cambió, en JSON. Cifras e identificadores; nunca datos personales.</summary>
    public string? Detalle { get; private set; }

    public DateTimeOffset En { get; private set; }

    public static RegistroAuditoria Crear(Guid empresaId, Guid? actorId, string entidad, Guid? entidadId, string accion, string? detalle, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        ArgumentException.ThrowIfNullOrWhiteSpace(entidad);
        ArgumentException.ThrowIfNullOrWhiteSpace(accion);

        return new RegistroAuditoria(
            Guid.NewGuid(), empresaId, actorId, entidad.Trim(), entidadId, accion.Trim(),
            detalle is { Length: > 0 } ? detalle[..Math.Min(detalle.Length, LongitudMaximaDetalle)] : null,
            reloj.AhoraUtc);
    }
}
