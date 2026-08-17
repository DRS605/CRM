using System.Globalization;

namespace Matchketing.Nucleo.Comun;

/// <summary>
/// Números escritos como se escriben en España, para el texto que compone el servidor.
///
/// Hace falta porque el proyecto compila con <c>InvariantGlobalization</c>, así que <c>:N0</c> usa la
/// cultura invariante y produce «8,400 €». En España eso se lee como ocho euros y cuatro décimas: no
/// es un detalle de estilo, es una cifra distinta. Y no se puede arreglar pidiendo la cultura
/// <c>es-ES</c>, porque sin ICU no existe.
///
/// La solución es construir el formato a mano, que además no depende de qué haya instalado en la
/// máquina. La interfaz web ya formatea con <c>toLocaleString('es-ES')</c>; esto es para lo que el
/// servidor escribe en prosa: la cronología de un contacto, el resultado de una respuesta del repaso.
/// </summary>
public static class Castellano
{
    private static readonly NumberFormatInfo Formato = new()
    {
        NumberGroupSeparator = ".",
        NumberDecimalSeparator = ",",
        NumberGroupSizes = [3],
    };

    /// <summary>
    /// Importe en euros, sin decimales. Se redondea a propósito: en una frase como «ganada por 8.400 €»
    /// los céntimos no aportan nada y ensucian la lectura. Donde importan —los informes, el CSV— se
    /// usa el número, no esto.
    /// </summary>
    public static string Euros(decimal importe) => importe.ToString("N0", Formato) + " €";

    /// <summary>Un número entero con separador de millares: «1.275 filas».</summary>
    public static string Numero(long valor) => valor.ToString("N0", Formato);
}
