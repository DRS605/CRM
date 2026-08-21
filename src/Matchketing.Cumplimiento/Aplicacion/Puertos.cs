using Matchketing.Cumplimiento.Dominio;

namespace Matchketing.Cumplimiento.Aplicacion;

public interface IRepositorioConsentimientos
{
    Task<IReadOnlyList<Consentimiento>> DeContactoAsync(Guid contactoId, CancellationToken ct = default);

    Task<Consentimiento?> VigenteAsync(Guid contactoId, FinalidadConsentimiento finalidad, CancellationToken ct = default);

    void Anadir(Consentimiento consentimiento);
}

/// <summary>Cuántas filas se llevó por delante un borrado, por tabla. Va al registro de auditoría.</summary>
/// <summary>
/// Qué se borró, por tabla. El recuento no es decoración: es lo que se le puede contestar a quien
/// ejerció el derecho, y lo que se apunta en la auditoría.
///
/// **Los cinco últimos campos se añadieron arreglando un fallo, y merece la pena que se sepa.** Durante
/// varios módulos la supresión del artículo 17 borraba contacto, actividades, oportunidades, tareas,
/// señales, puntuaciones, envíos de formulario y consentimientos… y dejaba en la base **los correos que
/// se le habían mandado**, con su dirección, su asunto y su texto completo. También su fila en cada
/// campaña, las ejecuciones de reglas sobre él, los cuerpos de webhook que llevaban su identificador y
/// sus preguntas aparcadas del repaso.
///
/// No era un descuido de una tabla: era que cada módulo nuevo añadía datos de personas y nadie volvía
/// aquí. Ahora hay una prueba que recorre **todas** las columnas de la base buscando el identificador
/// del contacto después de borrarlo, así que el siguiente módulo que se olvide se enterará al momento.
/// </summary>
public sealed record RecuentoBorrado(
    int Contactos,
    int Actividades,
    int Oportunidades,
    int Tareas,
    int Senales,
    int Puntuaciones,
    int Envios,
    int Consentimientos,
    int Correos = 0,
    int EnviosDeCampania = 0,
    int Ejecuciones = 0,
    int EntregasWebhook = 0,
    int PreguntasAparcadas = 0)
{
    public int Total =>
        Contactos + Actividades + Oportunidades + Tareas + Senales + Puntuaciones + Envios + Consentimientos
        + Correos + EnviosDeCampania + Ejecuciones + EntregasWebhook + PreguntasAparcadas;
}

/// <summary>
/// Todo lo que el sistema guarda de una persona, o de una empresa entera, listo para serializar.
///
/// Existe porque los derechos de acceso, portabilidad y supresión del RGPD **cruzan todos los
/// módulos**: los datos de un contacto están repartidos entre contactos, embudo, tareas, match y
/// captación. Cumplimiento no puede —ni debe— referenciar a los siete módulos para llegar a ellos,
/// así que declara aquí lo que necesita y la infraestructura lo resuelve con una sola consulta por
/// tabla. La frontera queda en su sitio y la responsabilidad, en el módulo al que le toca.
/// </summary>
public interface IAlmacenPersonal
{
    /// <summary>Todo lo que hay de un contacto, en un objeto anidado. Null si no existe.</summary>
    Task<object?> ReunirContactoAsync(Guid contactoId, CancellationToken ct = default);

    Task<bool> ExisteContactoAsync(Guid contactoId, CancellationToken ct = default);

    /// <summary>¿Pidió el contacto no recibir más? Null si el contacto no existe.</summary>
    Task<bool?> EstaDeBajaAsync(Guid contactoId, CancellationToken ct = default);

    /// <summary>
    /// Marca la baja en el contacto. Vive aquí, y no en Cumplimiento, porque la invariante «de la
    /// baja no se vuelve» es del agregado <c>Contacto</c> y ahí es donde se cumple.
    /// </summary>
    Task<bool> DarDeBajaContactoAsync(Guid contactoId, CancellationToken ct = default);

    Task<string?> NombreEmpresaAsync(CancellationToken ct = default);

    /// <summary>Todo lo de la empresa activa: contactos, cuentas, embudos, formularios y ajustes.</summary>
    Task<object> ReunirEmpresaAsync(CancellationToken ct = default);

    /// <summary>Supresión de verdad: borra las filas, no las marca. Devuelve qué se borró.</summary>
    Task<RecuentoBorrado> BorrarContactoAsync(Guid contactoId, CancellationToken ct = default);

    /// <summary>
    /// Borra la empresa activa y todo lo suyo, incluida su auditoría. Deja el terreno limpio para
    /// que quien llama escriba el último apunte.
    /// </summary>
    Task<RecuentoBorrado> BorrarEmpresaAsync(CancellationToken ct = default);

    /// <summary>
    /// Leads que ya nadie va a trabajar: siguen siendo lead (no cliente), no tienen oportunidad
    /// abierta y no se les ha tocado desde <paramref name="limite"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> LeadsCaducadosAsync(DateTimeOffset limite, CancellationToken ct = default);
}

/// <summary>Los ajustes de retención de la empresa activa. Los guarda el módulo Organización.</summary>
public interface IAjustesRetencion
{
    Task<int?> MesesRetencionAsync(CancellationToken ct = default);

    /// <summary>Empresas activas con sus meses de retención. Lo usa el trabajo nocturno.</summary>
    Task<IReadOnlyList<(Guid EmpresaId, int Meses)>> DeTodasLasEmpresasAsync(CancellationToken ct = default);
}
