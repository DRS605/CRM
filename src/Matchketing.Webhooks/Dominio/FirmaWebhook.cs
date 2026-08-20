using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Matchketing.Webhooks.Dominio;

/// <summary>
/// Firma la entrega de un webhook para que quien la recibe pueda comprobar que la mandamos nosotros.
///
/// Sin firma, un webhook es una URL a la que **cualquiera** puede hacer un POST. Si eso desemboca en
/// «crear el pedido en el ERP», el agujero no es de quien recibe: es nuestro, por no haberle dado con
/// qué comprobarlo.
///
/// El formato es el que ya usa medio internet, y se copia a propósito para que quien lo reciba lo
/// reconozca y no tenga que leerse nada:
///
/// <code>X-Matchketing-Firma: t=1755691200,v1=&lt;hex&gt;</code>
///
/// Dos detalles que parecen de adorno y no lo son:
///
/// · **La marca de tiempo va dentro de lo firmado**, no al lado. Si solo se firmara el cuerpo, quien
///   interceptara una entrega podría reenviarla mañana igual de válida —«oportunidad ganada» dos
///   veces— y la firma seguiría cuadrando. Firmando `t.cuerpo`, cambiar `t` invalida la firma.
/// · **La comparación es en tiempo constante.** Un `==` de cadenas se rinde en el primer byte
///   distinto, y ese tiempo se puede medir para adivinar la firma byte a byte. Aquí no hace falta
///   —nosotros firmamos, no comprobamos— pero <see cref="Comprobar"/> existe para que la prueba
///   verifique lo mismo que verificaría el receptor, y para poder enseñar el código correcto en la
///   documentación.
/// </summary>
public static class FirmaWebhook
{
    /// <summary>Cabecera donde va la firma. En singular y en español, como todo lo demás.</summary>
    public const string Cabecera = "X-Matchketing-Firma";

    /// <summary>
    /// Cuánto se admite de desfase entre el reloj de quien firma y el de quien comprueba. Cinco
    /// minutos: suficiente para dos relojes mal puestos, corto para que una entrega interceptada no
    /// sirva de nada al rato.
    /// </summary>
    public static TimeSpan Tolerancia => TimeSpan.FromMinutes(5);

    /// <summary>El valor completo de la cabecera para este cuerpo y este momento.</summary>
    public static string Cabeza(string cuerpo, string secreto, DateTimeOffset ahora)
    {
        var t = ahora.ToUnixTimeSeconds();
        return $"t={t.ToString(CultureInfo.InvariantCulture)},v1={Hex(Firmar(t, cuerpo, secreto))}";
    }

    /// <summary>
    /// Comprueba una cabecera como la comprobaría quien la recibe. Está aquí para que las pruebas
    /// verifiquen exactamente lo mismo, no para usarse en producción.
    /// </summary>
    public static bool Comprobar(string? cabecera, string cuerpo, string secreto, DateTimeOffset ahora)
    {
        if (string.IsNullOrWhiteSpace(cabecera))
        {
            return false;
        }

        long? t = null;
        string? v1 = null;

        foreach (var trozo in cabecera.Split(',', StringSplitOptions.TrimEntries))
        {
            var igual = trozo.IndexOf('=', StringComparison.Ordinal);
            if (igual <= 0)
            {
                continue;
            }

            var nombre = trozo[..igual];
            var valor = trozo[(igual + 1)..];

            if (nombre == "t" && long.TryParse(valor, CultureInfo.InvariantCulture, out var leido))
            {
                t = leido;
            }
            else if (nombre == "v1")
            {
                v1 = valor;
            }
        }

        if (t is null || v1 is null)
        {
            return false;
        }

        // Fuera de la ventana no se comprueba la firma siquiera: una entrega de hace tres horas no vale
        // aunque venga perfectamente firmada, que es justo lo que impide reenviarla.
        var desfase = ahora - DateTimeOffset.FromUnixTimeSeconds(t.Value);
        if (desfase > Tolerancia || desfase < -Tolerancia)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(v1.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(Hex(Firmar(t.Value, cuerpo, secreto))));
    }

    /// <summary>Un secreto nuevo para una suscripción. 32 bytes: no hay motivo para menos.</summary>
    public static string SecretoNuevo() =>
        "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static byte[] Firmar(long t, string cuerpo, string secreto)
    {
        // `t.cuerpo`, con el punto dentro de lo firmado. Sin el separador, una marca de tiempo que
        // acabara en dígitos y un cuerpo que empezara por dígitos podrían dar el mismo texto firmado
        // que otra pareja distinta.
        var porFirmar = Encoding.UTF8.GetBytes($"{t.ToString(CultureInfo.InvariantCulture)}.{cuerpo}");
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), porFirmar);
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
