using Matchketing.Campanias.Dominio;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Campanias.Aplicacion;

public interface IRepositorioCampanias
{
    // ---------- Segmentos ----------

    Task<Segmento?> SegmentoAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Segmento>> SegmentosAsync(CancellationToken ct = default);

    void Anadir(Segmento segmento);

    void Quitar(Segmento segmento);

    // ---------- Campañas ----------

    Task<Campania?> CampaniaAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Campania>> CampaniasAsync(CancellationToken ct = default);

    /// <summary>Las que están enviando, para el trabajo que va encolando por lotes.</summary>
    Task<IReadOnlyList<Campania>> EnMarchaAsync(CancellationToken ct = default);

    /// <summary>Cuántas campañas usan este segmento. Un segmento con historia no se borra.</summary>
    Task<int> CuantasUsanAsync(Guid segmentoId, CancellationToken ct = default);

    void Anadir(Campania campania);

    void Quitar(Campania campania);

    // ---------- Envíos ----------

    void Anadir(IReadOnlyList<EnvioCampania> envios);

    /// <summary>El siguiente lote de pendientes de una campaña, en el orden en que se congelaron.</summary>
    Task<IReadOnlyList<EnvioCampania>> PendientesAsync(Guid campaniaId, int tope, CancellationToken ct = default);

    /// <summary>Todos los pendientes que quedan, para descartarlos al detener la campaña.</summary>
    Task<IReadOnlyList<EnvioCampania>> TodosLosPendientesAsync(Guid campaniaId, CancellationToken ct = default);

    /// <summary>Los que se quedaron fuera, con su motivo. Es la pantalla que contesta «¿y a este por qué no?».</summary>
    Task<IReadOnlyList<EnvioCampania>> ExcluidosAsync(Guid campaniaId, int tope, CancellationToken ct = default);
}

/// <summary>
/// Resuelve un segmento: devuelve los contactos que lo cumplen **ahora**.
///
/// Lo implementa la persistencia y no este módulo porque los criterios tocan cuatro tablas de otros
/// módulos —contactos, cuentas, puntuaciones de match y oportunidades— y este módulo no conoce
/// ninguna. Aquí solo vive lo que significa un criterio; traducirlo a una consulta es trabajo de la
/// capa que sí puede mirar todas las tablas, igual que <c>ConsultaInformes</c> o <c>ConsultaRepaso</c>.
///
/// Excluye siempre, y sin que se pueda pedir lo contrario, a quien está de baja y a quien no tiene
/// dirección de correo. Lo primero porque una baja no es un filtro sino un muro; lo segundo porque un
/// contacto sin correo no es un destinatario, y meterlo en la audiencia solo serviría para inflar el
/// número de excluidos con gente que nunca pudo estar dentro.
/// </summary>
public interface IBuscaContactosDelSegmento
{
    Task<IReadOnlyList<Guid>> ResolverAsync(CriteriosSegmento criterios, int tope, CancellationToken ct = default);

    Task<int> ContarAsync(CriteriosSegmento criterios, CancellationToken ct = default);

    /// <summary>Una muestra con nombre y correo, para que se vea a quién se le va a escribir antes de lanzarla.</summary>
    Task<IReadOnlyList<QuienRecibe>> MuestraAsync(CriteriosSegmento criterios, int cuantos, CancellationToken ct = default);

    /// <summary>Cómo se llama esa etapa, para poder escribir la frase del segmento. Nulo si no existe.</summary>
    Task<string?> NombreDeEtapaAsync(Guid etapaId, CancellationToken ct = default);
}

/// <summary>
/// Lo que hace falta saber de la plantilla antes de dejar lanzar una campaña con ella. Lo implementa la
/// API sobre el módulo de correo.
/// </summary>
public interface IPlantillaDeCampania
{
    Task<DatosPlantilla?> DeAsync(Guid plantillaId, CancellationToken ct = default);
}

/// <summary>
/// Encola un correo de campaña a una persona. Lo implementa la API llamando al módulo de correo, **por
/// el mismo camino que un correo escrito a mano**: el mismo servicio, la misma comprobación de permiso
/// y el mismo buzón de salida.
///
/// Devuelve `Resultado` y no un booleano porque el motivo es el producto: cuando falla, el texto del
/// error es lo que se guarda en la exclusión y lo que se lee en la pantalla.
/// </summary>
public interface IEncolaCorreoDeCampania
{
    Task<Resultado<Guid>> EncolarAsync(
        Guid contactoId, Guid plantillaId, Guid enNombreDe, CancellationToken ct = default);
}

/// <summary>
/// Qué fue de los correos de una campaña. Lo implementa la persistencia, que puede juntar la tabla de
/// envíos con la del buzón de salida en una sola consulta.
/// </summary>
public interface IConsultaEnviosDeCampania
{
    Task<ContadoresCorreo> ContadoresAsync(Guid campaniaId, CancellationToken ct = default);
}

/// <summary>Nombre y correo de alguien de la audiencia. Solo para la vista previa.</summary>
public sealed record QuienRecibe(Guid ContactoId, string Nombre, string? Email);

/// <summary>
/// <paramref name="EsComercial"/> es lo único que decide si la plantilla se puede usar en una campaña.
/// </summary>
public sealed record DatosPlantilla(Guid Id, string Nombre, string Asunto, bool EsComercial);

/// <summary>
/// Lo que ha pasado con los correos ya encolados. <paramref name="Abiertos"/> cuenta personas, no
/// aperturas: quien abre el correo cuatro veces sigue siendo una persona que lo abrió.
/// </summary>
public sealed record ContadoresCorreo(int Enviados, int EnCola, int Fallidos, int Cancelados, int Abiertos);
