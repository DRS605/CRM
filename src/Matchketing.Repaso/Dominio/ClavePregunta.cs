using Matchketing.Nucleo.Resultados;

namespace Matchketing.Repaso.Dominio;

/// <summary>
/// La identidad de una pregunta del repaso: su tipo y la fila de la que nace.
///
/// Las preguntas **no se guardan**. Se derivan de la base cada vez que se pide la pila, porque una
/// tabla de preguntas pendientes sería una segunda verdad que se desincroniza en cuanto alguien cierra
/// una oportunidad por otro camino: aparecerían preguntas sobre cosas que ya no pasan. Al derivarlas,
/// la pila es siempre exacta por construcción.
///
/// Pero el cliente necesita algo que mandar de vuelta al contestar, y por eso la identidad tiene que
/// ser **determinista y legible**: <c>tarea-vencida:a1b2…</c>. Sale de los datos, no de un contador,
/// así que dos peticiones seguidas dan la misma clave para la misma pregunta.
/// </summary>
public readonly record struct ClavePregunta(TipoPregunta Tipo, Guid ReferenciaId)
{
    private static readonly (TipoPregunta Tipo, string Texto)[] Nombres =
    [
        (TipoPregunta.TareaVencida, "tarea-vencida"),
        (TipoPregunta.LeadSinTocar, "lead-sin-tocar"),
        (TipoPregunta.CierrePasado, "cierre-pasado"),
        (TipoPregunta.OportunidadEstancada, "oportunidad-estancada"),
        (TipoPregunta.SilencioCaliente, "silencio-caliente"),
        (TipoPregunta.ClienteSinSiguientePaso, "cliente-sin-siguiente-paso"),
    ];

    /// <summary>Longitud del texto más largo que puede producir <see cref="ToString"/>.</summary>
    public const int LongitudMaxima = 70;

    public override string ToString() => $"{Texto(Tipo)}:{ReferenciaId}";

    public static Resultado<ClavePregunta> Interpretar(string? clave)
    {
        var invalida = Resultado.Fallo<ClavePregunta>(
            Error.Validacion("repaso.clave_no_valida", "Esa pregunta no existe."));

        if (string.IsNullOrWhiteSpace(clave))
        {
            return invalida;
        }

        var partes = clave.Split(':');
        if (partes.Length != 2 || !Guid.TryParse(partes[1], out var referencia))
        {
            return invalida;
        }

        var encontrado = Nombres.FirstOrDefault(n => n.Texto == partes[0]);
        return encontrado.Texto is null ? invalida : Resultado.Ok(new ClavePregunta(encontrado.Tipo, referencia));
    }

    private static string Texto(TipoPregunta tipo) =>
        Nombres.FirstOrDefault(n => n.Tipo == tipo).Texto ?? "desconocida";
}
