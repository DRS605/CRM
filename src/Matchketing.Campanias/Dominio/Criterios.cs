using System.Globalization;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Campanias.Dominio;

/// <summary>
/// El estado del contacto que se puede buscar.
///
/// Es un espejo del de contactos y no una referencia, porque este módulo no conoce a ese —regla de la
/// casa—. El espejo tiene una ausencia deliberada: **no existe «baja»**. Quien se ha dado de baja no
/// es un segmento al que se pueda apuntar; es un muro. Si «baja» estuviera en esta lista habría una
/// pantalla en la que se puede elegir, y entonces la única cosa que impediría el envío sería la
/// comprobación de permiso del final. Vale más que no se pueda ni escribir el filtro.
/// </summary>
public enum EstadoBuscado
{
    Lead = 1,
    Cliente = 2,
    Perdido = 3,
}

/// <summary>
/// A quién apunta un segmento. Seis criterios, y todos salen de datos que el CRM ya tiene por trabajar
/// —no de etiquetas que alguien tenga que mantener a mano—.
///
/// Esa es la diferencia con una lista importada: una lista es una foto del día que se subió y envejece
/// desde el primer minuto. Un segmento se vuelve a resolver cada vez que se lanza una campaña, así que
/// «clientes de Valencia sin actividad desde hace tres meses» significa hoy lo que dice hoy.
///
/// Los criterios se combinan con **y**, nunca con «o». Un editor de condiciones anidadas es la forma
/// más rápida de que alguien construya sin darse cuenta un segmento que incluye a toda su base de
/// datos. Con «y» el segmento solo puede encogerse al añadir criterios, y eso se entiende sin
/// explicación.
/// </summary>
public sealed record CriteriosSegmento(
    EstadoBuscado? Estado,
    string? Provincia,
    string? Origen,
    int? MatchMinimo,
    int? SinActividadDias,
    Guid? EtapaId)
{
    public const int LongitudMaximaTexto = 60;

    /// <summary>
    /// Diez años. No es un límite técnico: es que un criterio de «sin actividad desde hace más de
    /// cuatro mil días» no es un criterio, es un error de teclado que nadie va a revisar.
    /// </summary>
    public const int MaximoDias = 3650;

    public static CriteriosSegmento Vacios { get; } = new(null, null, null, null, null, null);

    /// <summary>Cuántos criterios hay puestos. Cero es el caso que hay que rechazar.</summary>
    public int Cuantos =>
        (Estado is null ? 0 : 1)
        + (string.IsNullOrWhiteSpace(Provincia) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(Origen) ? 0 : 1)
        + (MatchMinimo is null ? 0 : 1)
        + (SinActividadDias is null ? 0 : 1)
        + (EtapaId is null ? 0 : 1);

    /// <summary>
    /// Los valida y los devuelve limpios, o dice qué está mal.
    ///
    /// La regla que importa es la primera: **un segmento sin ningún criterio no se guarda**. Un
    /// segmento vacío significa «todos mis contactos», y eso tiene que costar una decisión explícita
    /// —«clientes», «leads»— y no un despiste al rellenar un formulario. El día que alguien lance una
    /// campaña a toda su base de datos, que sea porque quería.
    /// </summary>
    public static Resultado<CriteriosSegmento> Crear(
        EstadoBuscado? estado, string? provincia, string? origen,
        int? matchMinimo, int? sinActividadDias, Guid? etapaId)
    {
        if (estado is { } cual && !Enum.IsDefined(cual))
        {
            return Resultado.Fallo<CriteriosSegmento>(Error.Validacion(
                "segmento.estado_invalido", "Ese estado no existe."));
        }

        if (Limpiar(provincia) is { } p && p.Length > LongitudMaximaTexto)
        {
            return Resultado.Fallo<CriteriosSegmento>(Error.Validacion(
                "segmento.provincia_larga", $"La provincia no puede pasar de {LongitudMaximaTexto} caracteres."));
        }

        if (Limpiar(origen) is { } o && o.Length > LongitudMaximaTexto)
        {
            return Resultado.Fallo<CriteriosSegmento>(Error.Validacion(
                "segmento.origen_largo", $"El origen no puede pasar de {LongitudMaximaTexto} caracteres."));
        }

        // Un match mínimo de 0 no filtra nada —todo el mundo puntúa 0 o más— así que aceptarlo sería
        // dejar guardar un criterio que no hace nada, y de esos salen los segmentos que sorprenden.
        if (matchMinimo is { } m && m is < 1 or > 100)
        {
            return Resultado.Fallo<CriteriosSegmento>(Error.Validacion(
                "segmento.match_invalido", "El match mínimo va de 1 a 100."));
        }

        if (sinActividadDias is { } d && (d < 1 || d > MaximoDias))
        {
            return Resultado.Fallo<CriteriosSegmento>(Error.Validacion(
                "segmento.dias_invalidos", $"Los días sin actividad van de 1 a {MaximoDias}."));
        }

        if (etapaId == Guid.Empty)
        {
            etapaId = null;
        }

        var criterios = new CriteriosSegmento(
            estado, Limpiar(provincia), Limpiar(origen), matchMinimo, sinActividadDias, etapaId);

        return criterios.Cuantos == 0
            ? Resultado.Fallo<CriteriosSegmento>(Error.Validacion(
                "segmento.sin_criterios",
                "Un segmento tiene que decir a quién apunta. Sin ningún criterio serían todos tus contactos, " +
                "y eso hay que pedirlo a propósito."))
            : Resultado.Ok(criterios);
    }

    /// <summary>
    /// El segmento dicho en una frase, en castellano.
    ///
    /// Se guarda escrita en la campaña al lanzarla, y por eso está en el dominio y no en la pantalla:
    /// dentro de seis meses, cuando alguien mire por qué a un cliente le llegó ese correo, el segmento
    /// puede haberse editado o borrado. La frase que se leyó al lanzar es la que hay que poder leer
    /// otra vez.
    /// </summary>
    /// <param name="nombreEtapa">
    /// Cómo se llama la etapa de <see cref="EtapaId"/>. Lo pasa quien sabe leerlo, porque este módulo
    /// no conoce el embudo. Si no viene, la frase lo dice de forma genérica en vez de mentir.
    /// </param>
    public string Frase(string? nombreEtapa = null)
    {
        var partes = new List<string>(6);

        partes.Add(Estado switch
        {
            EstadoBuscado.Lead => "leads",
            EstadoBuscado.Cliente => "clientes",
            EstadoBuscado.Perdido => "contactos perdidos",
            _ => "contactos",
        });

        if (!string.IsNullOrWhiteSpace(Provincia))
        {
            partes.Add("de " + Provincia);
        }

        if (!string.IsNullOrWhiteSpace(Origen))
        {
            partes.Add("que entraron por " + Origen);
        }

        if (MatchMinimo is { } m)
        {
            partes.Add("con match de " + m.ToString(CultureInfo.InvariantCulture) + " o más");
        }

        if (SinActividadDias is { } d)
        {
            partes.Add(d == 1
                ? "sin actividad desde ayer"
                : "sin actividad desde hace " + d.ToString(CultureInfo.InvariantCulture) + " días");
        }

        if (EtapaId is not null)
        {
            partes.Add(string.IsNullOrWhiteSpace(nombreEtapa)
                ? "con una oportunidad abierta en una etapa concreta"
                : "con una oportunidad abierta en «" + nombreEtapa.Trim() + "»");
        }

        return string.Join(", ", partes);
    }

    private static string? Limpiar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
