namespace Matchketing.Nucleo.Comun;

/// <summary>
/// Base64 apto para URL y para cabeceras HTTP: sin «+», sin «/» y sin relleno.
///
/// Está aquí porque lo necesitan dos sitios muy distintos —los enlaces de baja y todo lo de Web Push,
/// donde es el formato de las claves y de los tokens VAPID— y tener dos copias de esto es la forma
/// clásica de que una de ellas se equivoque con el relleno y falle solo con ciertas longitudes.
///
/// .NET 9 trae <c>Base64Url</c> en la biblioteca estándar. Cuando este proyecto suba de versión, esto
/// se borra.
/// </summary>
public static class Base64Url
{
    public static string Codificar(ReadOnlySpan<byte> datos) =>
        Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Devuelve nulo si el texto no es base64 válido. No lanza: el texto suele venir de fuera.</summary>
    public static byte[]? Descodificar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var completo = texto.Replace('-', '+').Replace('_', '/');
        completo += new string('=', (4 - (completo.Length % 4)) % 4);

        var destino = new byte[completo.Length / 4 * 3];
        return Convert.TryFromBase64String(completo, destino, out var escritos) ? destino[..escritos] : null;
    }
}
