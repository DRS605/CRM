using System.Text;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Nucleo.Comun;

/// <summary>
/// Teléfono normalizado a formato internacional (+34…). Como el <see cref="Email"/>, la
/// normalización es lo que hace posible detectar duplicados: «96 123 45 67», «+34961234567» y
/// «0034 961 23 45 67» son la misma persona y deben guardarse igual.
/// </summary>
public sealed record Telefono
{
    public const string PrefijoPorDefecto = "+34";

    private Telefono(string valor) => Valor = valor;

    public string Valor { get; }

    public static Resultado<Telefono> Crear(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return Resultado.Fallo<Telefono>(Error.Validacion("telefono.vacio", "El teléfono es obligatorio."));
        }

        var limpio = new StringBuilder();
        foreach (var c in valor.Trim())
        {
            if (char.IsDigit(c))
            {
                limpio.Append(c);
            }
            else if (c == '+' && limpio.Length == 0)
            {
                limpio.Append('+');
            }
            else if (c is ' ' or '-' or '.' or '(' or ')' or '/')
            {
                continue;
            }
            else
            {
                return Resultado.Fallo<Telefono>(Error.Validacion("telefono.invalido", "El teléfono contiene caracteres que no son válidos."));
            }
        }

        var texto = limpio.ToString();

        if (texto.StartsWith("00", StringComparison.Ordinal))
        {
            texto = "+" + texto[2..];
        }
        else if (!texto.StartsWith('+'))
        {
            // Sin prefijo: se asume España, que es el mercado del producto.
            texto = PrefijoPorDefecto + texto;
        }

        var digitos = texto[1..];
        if (digitos.Length is < 8 or > 15 || !digitos.All(char.IsDigit))
        {
            return Resultado.Fallo<Telefono>(Error.Validacion("telefono.invalido", "El teléfono no tiene un número de dígitos válido."));
        }

        return Resultado.Ok(new Telefono(texto));
    }

    public override string ToString() => Valor;
}
