using System.Text.RegularExpressions;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Nucleo.Comun;

/// <summary>
/// Correo electrónico normalizado (recortado y en minúsculas). La normalización no es cosmética:
/// sin ella la detección de duplicados de contactos no funciona.
/// </summary>
public sealed partial record Email
{
    private Email(string valor) => Valor = valor;

    public string Valor { get; }

    public static Resultado<Email> Crear(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return Resultado.Fallo<Email>(Error.Validacion("email.vacio", "El correo electrónico es obligatorio."));
        }

        var normalizado = valor.Trim().ToLowerInvariant();
        if (normalizado.Length > 254 || !Patron().IsMatch(normalizado))
        {
            return Resultado.Fallo<Email>(Error.Validacion("email.invalido", "El correo electrónico no es válido."));
        }

        return Resultado.Ok(new Email(normalizado));
    }

    public override string ToString() => Valor;

    [GeneratedRegex(@"^[^@\s]+@[^@\s.]+(\.[^@\s.]+)+$")]
    private static partial Regex Patron();
}
