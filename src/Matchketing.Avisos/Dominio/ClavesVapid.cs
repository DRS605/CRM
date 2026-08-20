using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Avisos.Dominio;

/// <summary>
/// Las claves con las que este servidor se identifica ante los servicios de push (RFC 8292, «VAPID»).
///
/// Son un par de claves P-256 que **no se rotan**: la pública se le da al navegador al suscribirse y
/// queda grabada dentro de la suscripción. Si se cambia el par, todas las suscripciones existentes
/// dejan de valer de golpe y hay que volver a pedir permiso a cada persona. Se genera una vez, se
/// guarda en la configuración, y ahí se queda.
///
/// El servicio de push no comprueba quiénes somos —no hay registro previo en ninguna parte—; solo
/// comprueba que quien manda hoy es el mismo que mandaba ayer y que tiene una forma de contacto. De
/// ahí que el token lleve un `sub` con un correo: es a quien avisan si esto empieza a mandar basura.
/// </summary>
public sealed class ClavesVapid
{
    /// <summary>Doce horas. El máximo que admite el RFC es 24; la mitad deja margen si el reloj baila.</summary>
    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(12);

    private ClavesVapid(string publica, string privada, string sujeto)
    {
        Publica = publica;
        Privada = privada;
        Sujeto = sujeto;
    }

    /// <summary>Punto sin comprimir de 65 bytes, en base64url. Es lo que recibe el navegador.</summary>
    public string Publica { get; }

    /// <summary>El escalar de 32 bytes, en base64url. Nunca sale de aquí.</summary>
    public string Privada { get; }

    /// <summary>A quién avisar si algo va mal: `mailto:` o una dirección web.</summary>
    public string Sujeto { get; }

    /// <summary>Genera un par nuevo. Se usa una vez, a mano, para rellenar la configuración.</summary>
    public static ClavesVapid Generar(string sujeto)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(true);

        return new ClavesVapid(
            Base64Url.Codificar(PuntoSinComprimir(p.Q)),
            Base64Url.Codificar(p.D!),
            sujeto);
    }

    public static Resultado<ClavesVapid> De(string? publica, string? privada, string? sujeto)
    {
        if (Base64Url.Descodificar(publica) is not { Length: 65 } punto || punto[0] != 0x04)
        {
            return Resultado.Fallo<ClavesVapid>(Error.Validacion(
                "vapid.publica_invalida", "La clave pública VAPID tiene que ser un punto P-256 sin comprimir de 65 bytes."));
        }

        if (Base64Url.Descodificar(privada) is not { Length: 32 })
        {
            return Resultado.Fallo<ClavesVapid>(Error.Validacion(
                "vapid.privada_invalida", "La clave privada VAPID tiene que ser un escalar de 32 bytes."));
        }

        if (string.IsNullOrWhiteSpace(sujeto) || (!sujeto.StartsWith("mailto:", StringComparison.Ordinal) && !sujeto.StartsWith("https://", StringComparison.Ordinal)))
        {
            return Resultado.Fallo<ClavesVapid>(Error.Validacion(
                "vapid.sujeto_invalido", "El sujeto VAPID tiene que ser un «mailto:» o una dirección https."));
        }

        return Resultado.Ok(new ClavesVapid(publica!, privada!, sujeto));
    }

    /// <summary>
    /// El token que va en la cabecera <c>Authorization: vapid t=…, k=…</c>.
    ///
    /// La audiencia es **solo el origen** del endpoint de push (esquema y host, sin la ruta): el RFC lo
    /// exige así y los servicios rechazan el token si se les cuela la ruta completa, que es el error
    /// más fácil de cometer aquí porque el endpoint sí es una URL larga.
    /// </summary>
    public string Token(Uri endpoint, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var cabecera = Base64Url.Codificar(Encoding.UTF8.GetBytes("""{"typ":"JWT","alg":"ES256"}"""));
        var cuerpo = Base64Url.Codificar(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = endpoint.GetLeftPart(UriPartial.Authority),
            exp = ahora.Add(Vigencia).ToUnixTimeSeconds(),
            sub = Sujeto,
        }));

        var porFirmar = Encoding.UTF8.GetBytes($"{cabecera}.{cuerpo}");

        using var ec = Ecdsa();
        // `IeeeP1363FixedFieldConcatenation` es R||S en 64 bytes fijos. Un JWT ES256 **no** admite la
        // firma en DER, que es lo que .NET produce por defecto: con DER el servicio de push devuelve
        // un 401 sin explicar por qué.
        var firma = ec.SignData(porFirmar, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{cabecera}.{cuerpo}.{Base64Url.Codificar(firma)}";
    }

    /// <summary>La clave para firmar. Se reconstruye del escalar y del punto público.</summary>
    public ECDsa Ecdsa()
    {
        var punto = Base64Url.Descodificar(Publica)!;
        return ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.Descodificar(Privada)!,
            Q = new ECPoint { X = punto[1..33], Y = punto[33..65] },
        });
    }

    private static byte[] PuntoSinComprimir(ECPoint q)
    {
        var punto = new byte[65];
        punto[0] = 0x04;
        q.X!.CopyTo(punto, 1);
        q.Y!.CopyTo(punto, 33);
        return punto;
    }
}
