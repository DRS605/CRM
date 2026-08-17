using System.Text.RegularExpressions;

namespace Matchketing.Auditoria.Dominio;

/// <summary>
/// Red de seguridad del <see cref="RegistroAuditoria.Detalle"/>.
///
/// La regla —«en el detalle nunca van datos personales»— es fácil de escribir en un comentario y
/// fácil de romper seis meses después, cuando alguien añada un campo de más al objeto que se
/// serializa. Y el registro de auditoría es **append-only**: si un correo entra ahí, no sale. Así que
/// la regla se cumple aquí, tapando lo que huele a correo o a teléfono antes de guardar, en vez de
/// confiar en que nadie se despiste.
///
/// No pretende ser un detector perfecto: los dos formatos que de verdad se cuelan son esos dos.
/// </summary>
public static partial class Detalles
{
    public const string CorreoTapado = "«correo oculto»";
    public const string TelefonoTapado = "«teléfono oculto»";

    /// <summary>Mínimo de dígitos de un número de teléfono. Menos que eso es una cifra cualquiera.</summary>
    private const int DigitosMinimos = 9;

    /// <summary>Máximo de dígitos de un número de teléfono en el mundo real (E.164).</summary>
    private const int DigitosMaximos = 15;

    /// <summary>Tapa correos y teléfonos. Deja intactos identificadores, cifras y fechas.</summary>
    public static string? Tapar(string? detalle) =>
        string.IsNullOrWhiteSpace(detalle)
            ? null
            : Telefonos().Replace(Correos().Replace(detalle, CorreoTapado), TaparSiCabeEnUnTelefono);

    [GeneratedRegex(@"[\w.+%-]+@[\w-]+\.[\w.-]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Correos();

    /// <summary>
    /// Dígitos con prefijo y separadores. Se pesca de más a propósito —los guiones entran, porque los
    /// teléfonos se escriben con guiones— y luego se descarta por el número de dígitos.
    /// </summary>
    [GeneratedRegex(@"(?<![\w-])\+?\d[\d ().-]{7,}\d(?![\w-])", RegexOptions.CultureInvariant)]
    private static partial Regex Telefonos();

    /// <summary>
    /// Un teléfono tiene entre 9 y 15 dígitos. Fuera de ese rango no lo es, y el caso que de verdad
    /// importa es el de arriba: un UUID cuyo primer tramo sea todo dígitos —
    /// <c>11111111-2222-3333-4444-123456789012</c>— encaja de sobra en el patrón, tiene 32 dígitos y
    /// **no** se toca. Taparlo dejaría el apunte de auditoría sin la única cosa que lo hace útil.
    /// </summary>
    private static string TaparSiCabeEnUnTelefono(Match encontrado)
    {
        var digitos = encontrado.Value.Count(char.IsAsciiDigit);
        return digitos is >= DigitosMinimos and <= DigitosMaximos ? TelefonoTapado : encontrado.Value;
    }
}
