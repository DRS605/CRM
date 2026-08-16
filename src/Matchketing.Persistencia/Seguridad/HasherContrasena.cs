using System.Security.Cryptography;
using Matchketing.Identidad.Aplicacion;

namespace Matchketing.Persistencia.Seguridad;

/// <summary>
/// PBKDF2 con SHA-256, sal aleatoria por contraseña y comparación en tiempo constante. Sin
/// dependencias externas: son treinta líneas y evitan arrastrar todo ASP.NET Core Identity.
/// </summary>
public sealed class HasherContrasena : IHasherContrasena
{
    private const int TamanoSal = 16;
    private const int TamanoHash = 32;
    private const int Iteraciones = 210_000;

    public string Hashear(string contrasenaEnClaro)
    {
        ArgumentNullException.ThrowIfNull(contrasenaEnClaro);

        var sal = RandomNumberGenerator.GetBytes(TamanoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasenaEnClaro, sal, Iteraciones, HashAlgorithmName.SHA256, TamanoHash);
        return $"pbkdf2-sha256${Iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string contrasenaEnClaro, string hash)
    {
        ArgumentNullException.ThrowIfNull(contrasenaEnClaro);
        ArgumentNullException.ThrowIfNull(hash);

        var partes = hash.Split('$');
        if (partes.Length != 4 || partes[0] != "pbkdf2-sha256" || !int.TryParse(partes[1], out var iteraciones))
        {
            return false;
        }

        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(contrasenaEnClaro, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
