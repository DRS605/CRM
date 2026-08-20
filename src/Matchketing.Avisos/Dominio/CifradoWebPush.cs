using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;

namespace Matchketing.Avisos.Dominio;

/// <summary>
/// El cifrado del cuerpo de un aviso push (RFC 8291 sobre RFC 8188, «aes128gcm»).
///
/// El servicio de push —Google, Mozilla, Apple— reenvía el mensaje sin poder leerlo: solo el navegador
/// que se suscribió tiene la clave. Eso no es un extra de este módulo, es la condición para poder
/// mandar avisos: **un cuerpo sin cifrar se rechaza**.
///
/// De la suscripción vienen dos cosas: `p256dh`, la clave pública P-256 del navegador, y `auth`, un
/// secreto de 16 bytes que actúa como sal del primer HKDF. Se genera un par efímero por mensaje, se
/// hace ECDH contra la clave del navegador, y de ahí salen la clave AES y el nonce.
///
/// Está en el dominio, sin frameworks, porque es **la única parte de Web Push que se puede comprobar
/// sin un servicio de push de verdad**: se cifra con entradas fijas y se compara con la salida que
/// produce una implementación independiente. Ver <c>PruebasCifradoWebPush</c>.
/// </summary>
public static class CifradoWebPush
{
    /// <summary>Tamaño de registro. 4096 es lo que usa todo el mundo y lo que aceptan todos los servicios.</summary>
    public const int TamanoRegistro = 4096;

    /// <summary>Lo máximo que cabe en un aviso. Por encima, muchos servicios devuelven 413.</summary>
    public const int MaximoBytesMensaje = 3800;

    private static readonly byte[] InfoWebPush = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] InfoClave = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] InfoNonce = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    /// <summary>
    /// Cifra un mensaje para una suscripción.
    ///
    /// <paramref name="sal"/> y <paramref name="efimera"/> solo se pasan en las pruebas, para poder
    /// comparar con una salida conocida. En producción son aleatorias y **tienen que serlo**: repetir
    /// la sal con la misma clave rompe AES-GCM, y la clave efímera se llama efímera por algo.
    /// </summary>
    public static Resultado<byte[]> Cifrar(
        string mensaje, string? p256dh, string? auth, byte[]? sal = null, ECDiffieHellman? efimera = null)
    {
        ArgumentNullException.ThrowIfNull(mensaje);

        var texto = Encoding.UTF8.GetBytes(mensaje);
        if (texto.Length > MaximoBytesMensaje)
        {
            return Resultado.Fallo<byte[]>(Error.Validacion(
                "aviso.mensaje_largo", $"El mensaje del aviso no puede pasar de {MaximoBytesMensaje} bytes."));
        }

        if (Base64Url.Descodificar(p256dh) is not { Length: 65 } puntoNavegador || puntoNavegador[0] != 0x04)
        {
            return Resultado.Fallo<byte[]>(Error.Validacion(
                "suscripcion.p256dh_invalida", "La clave del navegador tiene que ser un punto P-256 sin comprimir de 65 bytes."));
        }

        if (Base64Url.Descodificar(auth) is not { Length: 16 } secreto)
        {
            return Resultado.Fallo<byte[]>(Error.Validacion(
                "suscripcion.auth_invalida", "El secreto de autenticación tiene que ser de 16 bytes."));
        }

        // La clave inyectada la posee quien la pasó; la propia se descarta al salir.
        using var propia = efimera is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var servidor = efimera ?? propia!;
        var puntoServidor = PuntoSinComprimir(servidor.PublicKey.ExportParameters().Q);

        using var publicaNavegador = ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = puntoNavegador[1..33], Y = puntoNavegador[33..65] },
        });

        // El secreto ECDH en bruto, sin pasarlo por ningún hash: el RFC lo mete tal cual en el HKDF.
        var compartido = servidor.DeriveRawSecretAgreement(publicaNavegador.PublicKey);

        // Primer HKDF, el de Web Push: la sal es el secreto de la suscripción y el «info» lleva las dos
        // claves públicas **en este orden** —primero la del navegador, después la del servidor—. Al
        // revés cifra igual de bien y el navegador no puede descifrarlo, y el error no se ve por
        // ninguna parte: el aviso simplemente no llega.
        var info = new byte[InfoWebPush.Length + 130];
        InfoWebPush.CopyTo(info, 0);
        puntoNavegador.CopyTo(info, InfoWebPush.Length);
        puntoServidor.CopyTo(info, InfoWebPush.Length + 65);

        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, compartido, 32, secreto, info);

        // Segundo HKDF, el de la codificación del cuerpo (RFC 8188).
        var salUsada = sal ?? RandomNumberGenerator.GetBytes(16);
        var clave = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 16, salUsada, InfoClave);
        var nonce = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 12, salUsada, InfoNonce);

        // Un único registro, así que el contador es 0 y el nonce va tal cual. El 0x02 del final es el
        // delimitador de «último registro»; sin él el navegador descarta el mensaje.
        var relleno = new byte[texto.Length + 1];
        texto.CopyTo(relleno, 0);
        relleno[^1] = 0x02;

        var cifrado = new byte[relleno.Length];
        var etiqueta = new byte[16];
        using (var aes = new AesGcm(clave, etiqueta.Length))
        {
            aes.Encrypt(nonce, relleno, cifrado, etiqueta);
        }

        // Cabecera: sal(16) · tamaño de registro(4) · longitud de la clave(1) · clave del servidor(65).
        var cuerpo = new byte[16 + 4 + 1 + 65 + cifrado.Length + etiqueta.Length];
        salUsada.CopyTo(cuerpo, 0);
        BinaryPrimitives.WriteUInt32BigEndian(cuerpo.AsSpan(16, 4), TamanoRegistro);
        cuerpo[20] = 65;
        puntoServidor.CopyTo(cuerpo, 21);
        cifrado.CopyTo(cuerpo, 86);
        etiqueta.CopyTo(cuerpo, 86 + cifrado.Length);

        return Resultado.Ok(cuerpo);
    }

    /// <summary>
    /// Reconstruye un par P-256 a partir de sus dos mitades en base64url. Lo usan las pruebas, para
    /// poder fijar la clave efímera y comparar el resultado con el de otra implementación.
    /// </summary>
    public static ECDiffieHellman ClaveDe(string privada, string publica)
    {
        var punto = Base64Url.Descodificar(publica)!;
        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.Descodificar(privada)!,
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
