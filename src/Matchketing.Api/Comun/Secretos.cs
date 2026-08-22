namespace Matchketing.Api.Comun;

/// <summary>
/// Los secretos que **no pueden quedarse en su valor de desarrollo**, y la comprobación que lo impide.
///
/// Los dos existían con un valor por defecto escrito en el repositorio, y los dos son públicos: están
/// en un archivo que cualquiera puede leer en GitHub. Un despliegue que arrancaba sin configurarlos
/// funcionaba **perfectamente** —entraba, mandaba correos, todo— y era una puerta abierta:
///
/// · Con la clave de firma por defecto, cualquiera puede fabricarse un token de sesión con el
///   identificador de empresa que quiera. No hay que romper nada: hay que firmarlo con una clave que
///   está publicada. Es el aislamiento entre empresas entero.
/// · Con el secreto de las bajas por defecto, cualquiera puede fabricar el enlace de baja de cualquier
///   contacto, y ese enlace es el que decide quién deja de recibir correos.
///
/// Así que en producción la aplicación **no arranca** si siguen puestos. Es la única forma de que no
/// pase: un aviso por registro no lo lee nadie el día que se despliega, y el fallo no se nota nunca
/// desde fuera.
/// </summary>
public static class Secretos
{
    public const string ClaveJwtDeDesarrollo = "clave-de-desarrollo-no-usar-en-produccion-0123456789";

    public const string SecretoBajaDeDesarrollo = "secreto-de-desarrollo-para-enlaces-de-baja-0123456789";

    /// <summary>Longitud mínima. Una clave de firma corta se prueba a fuerza bruta.</summary>
    public const int LongitudMinima = 32;

    /// <summary>
    /// Comprueba los secretos y **revienta el arranque** si alguno no vale. Se llama una vez, al
    /// levantar la aplicación, y solo fuera de desarrollo.
    /// </summary>
    public static void Exigir(IConfiguration config, IHostEnvironment entorno)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(entorno);

        if (entorno.IsDevelopment())
        {
            return;
        }

        Uno(config["Jwt:Clave"], "Jwt:Clave", ClaveJwtDeDesarrollo,
            "con la clave de desarrollo, cualquiera puede firmar un token de sesión de cualquier empresa");

        Uno(config["Baja:Secreto"], "Baja:Secreto", SecretoBajaDeDesarrollo,
            "con el secreto de desarrollo, cualquiera puede fabricar el enlace de baja de cualquier contacto");

        // La URL base de las bajas no es un secreto, pero su valor por defecto apunta a otro dominio:
        // los enlaces de baja de los correos llevarían a un sitio que no es este, y la baja no llegaría.
        if (string.IsNullOrWhiteSpace(config["Baja:UrlBase"]))
        {
            throw new InvalidOperationException(
                "Falta Baja:UrlBase. Es el dominio que va en el enlace de baja de cada correo; sin " +
                "configurarlo apunta a otro sitio y nadie puede darse de baja.");
        }
    }

    private static void Uno(string? valor, string clave, string deDesarrollo, string porQue)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new InvalidOperationException(
                $"Falta {clave} y no hay valor por defecto aceptable en producción: {porQue}.");
        }

        if (string.Equals(valor, deDesarrollo, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{clave} sigue con el valor de desarrollo, que está publicado en el repositorio: {porQue}.");
        }

        if (valor.Length < LongitudMinima)
        {
            throw new InvalidOperationException(
                $"{clave} tiene {valor.Length} caracteres y necesita {LongitudMinima} o más. " +
                "Sácala de `openssl rand -base64 48`.");
        }
    }
}
