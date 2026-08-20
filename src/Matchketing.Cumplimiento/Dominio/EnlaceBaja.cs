using System.Security.Cryptography;
using System.Text;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Cumplimiento.Dominio;

/// <summary>
/// El «no quiero recibir más» de un clic, sin contraseña y sin formularios.
///
/// Es un token **firmado, no guardado**: dentro llevan la empresa y el contacto, y detrás una firma
/// HMAC-SHA256 con el secreto del servidor. Ventajas de no tener tabla: no hay nada que caducar ni
/// que limpiar, y el enlace sigue valiendo dentro de tres años, cuando alguien encuentre en el
/// buzón un correo viejo y quiera darse de baja. Ahí está el punto: **el enlace no caduca a
/// propósito**. Un enlace de baja muerto es peor que no poner ninguno, porque convierte una baja de
/// un clic en una reclamación.
///
/// Lo que sí puede hacer el titular del secreto es invalidarlos todos de golpe cambiándolo. Es la
/// única palanca, y es la correcta: nunca hace falta revocar una baja concreta.
/// </summary>
public static class EnlaceBaja
{
    /// <summary>Dos UUID en binario: 16 bytes de empresa y 16 de contacto.</summary>
    private const int BytesCarga = 32;

    public static string Firmar(Guid empresaId, Guid contactoId, string secreto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secreto);

        var carga = new byte[BytesCarga];
        empresaId.TryWriteBytes(carga.AsSpan(0, 16));
        contactoId.TryWriteBytes(carga.AsSpan(16, 16));

        return $"{Base64Url.Codificar(carga)}.{Base64Url.Codificar(Firma(carga, secreto))}";
    }

    /// <summary>
    /// Comprueba la firma y devuelve a quién señala el token. El mensaje de error es el mismo para
    /// un token mal formado y para uno con firma falsa: distinguirlos solo ayudaría a quien esté
    /// probando firmas.
    /// </summary>
    public static Resultado<(Guid EmpresaId, Guid ContactoId)> Comprobar(string? token, string secreto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secreto);

        var invalido = Resultado.Fallo<(Guid, Guid)>(
            Error.Validacion("baja.enlace_invalido", "Este enlace de baja no es válido."));

        if (string.IsNullOrWhiteSpace(token))
        {
            return invalido;
        }

        var partes = token.Split('.');
        if (partes.Length != 2
            || Base64Url.Descodificar(partes[0]) is not { Length: BytesCarga } carga
            || Base64Url.Descodificar(partes[1]) is not { } firma)
        {
            return invalido;
        }

        // Comparación en tiempo constante: comparar firmas con `SequenceEqual` filtra por el tiempo
        // de respuesta cuántos bytes acertó quien la está adivinando.
        return CryptographicOperations.FixedTimeEquals(firma, Firma(carga, secreto))
            ? Resultado.Ok((new Guid(carga.AsSpan(0, 16)), new Guid(carga.AsSpan(16, 16))))
            : invalido;
    }

    private static byte[] Firma(byte[] carga, string secreto) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto), carga);

}
