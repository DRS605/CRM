using System.Globalization;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Automatizacion.Dominio;

/// <summary>
/// Lo que se sabe del sujeto en el momento de disparar, y lo único sobre lo que se puede condicionar.
///
/// Es un registro plano y cerrado por dos motivos. Uno, que las condiciones se evalúan **en memoria**
/// sobre esto: una consulta por regla convertiría guardar un contacto en diez consultas. Y dos, que
/// tener que añadir un campo aquí para poder condicionar sobre él obliga a pensar si de verdad hace
/// falta, que es exactamente la fricción que se quiere.
/// </summary>
public sealed record Hechos(
    string? Provincia,
    string? Origen,
    string? Sector,
    decimal? Importe,
    string? MotivoPerdida);

/// <summary>
/// «Si la provincia es Valencia». Una sola comparación; las reglas admiten hasta tres y se cumplen
/// **todas** (nunca «o»).
///
/// No hay «o» a propósito. En cuanto hay «y» y «o» mezclados hace falta paréntesis, y con paréntesis
/// hace falta un lienzo de ramas; y un lienzo de ramas es la funcionalidad que convierte una
/// herramienta que se entiende en una que necesita un consultor. Quien quiera «Valencia o Alicante»
/// hace dos reglas, que además se leen mejor.
/// </summary>
public sealed record Condicion(Campo Campo, Operador Operador, string Valor)
{
    /// <summary>¿La cumple este sujeto?</summary>
    public bool Cumple(Hechos hechos)
    {
        ArgumentNullException.ThrowIfNull(hechos);

        return Operador switch
        {
            Operador.MayorQue or Operador.MenorQue => ComparaNumeros(hechos),
            _ => ComparaTexto(hechos),
        };
    }

    public string Leer() => $"{Textos.De(Campo)} {Textos.De(Operador)} «{Valor}»";

    /// <summary>
    /// Comprueba que la condición tiene sentido antes de guardarla. Una regla que no puede cumplirse
    /// nunca es peor que no tener regla: parece que algo va a pasar y no pasa nada.
    /// </summary>
    public Resultado Validar()
    {
        if (string.IsNullOrWhiteSpace(Valor))
        {
            return Resultado.Fallo(Error.Validacion(
                "regla.condicion_sin_valor", $"La condición sobre {Textos.De(Campo)} no tiene valor."));
        }

        var numerica = Operador is Operador.MayorQue or Operador.MenorQue;

        if (numerica && !decimal.TryParse(Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
        {
            return Resultado.Fallo(Error.Validacion(
                "regla.condicion_no_numerica", $"«{Valor}» no es una cifra, así que no se puede comparar."));
        }

        // «Provincia mayor que Valencia» no significa nada. Se rechaza al guardar y no al disparar,
        // porque al disparar nadie lo estaría mirando.
        if (numerica && Campo != Campo.Importe)
        {
            return Resultado.Fallo(Error.Validacion(
                "regla.condicion_incoherente",
                $"«{Textos.De(Campo)} {Textos.De(Operador)}» no tiene sentido: solo el importe se compara por tamaño."));
        }

        if (!numerica && Campo == Campo.Importe && Operador == Operador.Contiene)
        {
            return Resultado.Fallo(Error.Validacion(
                "regla.condicion_incoherente", "Un importe no «contiene» nada; compáralo por tamaño."));
        }

        return Resultado.Ok();
    }

    private bool ComparaNumeros(Hechos hechos)
    {
        if (hechos.Importe is not { } importe
            || !decimal.TryParse(Valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var contra))
        {
            return false;
        }

        return Operador == Operador.MayorQue ? importe > contra : importe < contra;
    }

    private bool ComparaTexto(Hechos hechos)
    {
        var actual = Campo switch
        {
            Campo.Provincia => hechos.Provincia,
            Campo.Origen => hechos.Origen,
            Campo.Sector => hechos.Sector,
            Campo.MotivoPerdida => hechos.MotivoPerdida,
            _ => hechos.Importe?.ToString(CultureInfo.InvariantCulture),
        };

        // Sin distinguir mayúsculas ni acentos de más: quien escribe «valencia» en una regla quiere decir
        // Valencia, y una regla que no dispara por una tilde es una tarde perdida.
        return Operador switch
        {
            Operador.Es => string.Equals(actual?.Trim(), Valor.Trim(), StringComparison.OrdinalIgnoreCase),

            // Ojo: «no es» se cumple cuando el dato **falta**. Es lo que se espera: «si el sector no es
            // hostelería» tiene que incluir a quien no tiene sector puesto.
            Operador.NoEs => !string.Equals(actual?.Trim(), Valor.Trim(), StringComparison.OrdinalIgnoreCase),

            Operador.Contiene => actual is not null
                && actual.Contains(Valor.Trim(), StringComparison.OrdinalIgnoreCase),

            _ => false,
        };
    }
}
