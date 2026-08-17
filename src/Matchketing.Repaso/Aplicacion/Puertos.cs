using Matchketing.Repaso.Dominio;

namespace Matchketing.Repaso.Aplicacion;

/// <summary>Los datos crudos de una pregunta, antes de redactarla. Los saca la persistencia.</summary>
public sealed record Hallazgo(
    TipoPregunta Tipo,
    Guid ReferenciaId,
    Guid? ContactoId,
    string? NombreContacto,
    string? Telefono,
    Guid? OportunidadId,
    Guid? TareaId,
    string? Titulo,
    decimal? Importe,
    int? Match,
    /// <summary>Días de retraso, de silencio o de estancamiento, según el tipo. Ordena la pila.</summary>
    int Dias,
    DateOnly? Fecha);

/// <summary>
/// Todo lo que hay que mirar para saber qué preguntar. Es **una lectura por tipo** y nada más: si el
/// repaso necesitara una consulta por contacto, con doscientos contactos tardaría más en pintarse que
/// en contestarse, y entonces no sirve.
/// </summary>
public interface IConsultaRepaso
{
    Task<IReadOnlyList<Hallazgo>> HallazgosAsync(CancellationToken ct = default);

    Task<ResumenSemana> ResumenAsync(int dias, CancellationToken ct = default);
}

public interface IRepositorioPospuestas
{
    /// <summary>Las claves que hoy siguen pospuestas. Se consulta una vez por pila.</summary>
    Task<IReadOnlyCollection<string>> VigentesAsync(DateOnly hoy, CancellationToken ct = default);

    void Anadir(Pospuesta pospuesta);

    /// <summary>Cuántas preguntas se han resuelto en los últimos días. Va en el resumen.</summary>
    Task<int> ResueltasDesdeAsync(DateOnly desde, CancellationToken ct = default);
}

/// <summary>
/// Lo que una respuesta provoca en los otros módulos: cerrar una tarea, registrar una llamada, ganar
/// una oportunidad.
///
/// Es un puerto y no una referencia directa porque Repaso **orquesta** cinco módulos —contactos,
/// embudo, tareas, match y organización— y referenciarlos habría convertido la arquitectura en una
/// bola. El adaptador vive en la capa que ya conoce a todos, la API, y es pura delegación: aquí queda
/// la decisión de qué hacer con cada respuesta, que es lo que hay que poder probar sin base de datos.
///
/// Ninguno de estos métodos guarda: el guardado lo hace quien atiende la petición, en una sola
/// transacción con el apunte de que la pregunta se pospuso.
/// </summary>
public interface IAccionesRepaso
{
    Task<bool> CompletarTareaAsync(Guid tareaId, CancellationToken ct = default);

    Task<bool> AplazarTareaAsync(Guid tareaId, DateOnly nuevaFecha, CancellationToken ct = default);

    Task<bool> DescartarTareaAsync(Guid tareaId, CancellationToken ct = default);

    /// <summary>Registra la llamada y, si procede, el seguimiento. Devuelve falso si no hay contacto.</summary>
    Task<bool> RegistrarLlamadaAsync(Guid contactoId, ResultadoDeLlamada resultado, CancellationToken ct = default);

    Task<bool> DescartarContactoAsync(Guid contactoId, CancellationToken ct = default);

    Task<bool> CrearTareaAsync(Guid contactoId, string titulo, DateOnly venceEl, CancellationToken ct = default);

    /// <summary>Devuelve el importe de la oportunidad ganada, para poder decírselo.</summary>
    Task<decimal?> GanarOportunidadAsync(Guid oportunidadId, CancellationToken ct = default);

    Task<bool> PerderOportunidadAsync(Guid oportunidadId, int motivo, CancellationToken ct = default);

    Task<bool> MoverCierreAsync(Guid oportunidadId, DateOnly nuevaFecha, CancellationToken ct = default);
}

/// <summary>
/// El resultado de una llamada, en los términos de Repaso. Se traduce al del módulo de contactos en el
/// adaptador: así este proyecto no depende de un enum que vive en otro módulo.
/// </summary>
public enum ResultadoDeLlamada
{
    Contactado = 1,
    NoContesta = 2,
    NoInteresa = 3,
}
