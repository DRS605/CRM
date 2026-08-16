using System.Security.Cryptography;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Captacion.Dominio;

/// <summary>
/// Un formulario de captación para pegar en la web del cliente. Los campos no se configuran uno a
/// uno: se elige **qué se pide además del nombre y el medio de contacto**, y ya. Un formulario con
/// doce campos no lo rellena nadie.
/// </summary>
public sealed class Formulario : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaNombre = 80;
    public const int LongitudMaximaTexto = 500;
    public const int LongitudClave = 22;

    private Formulario(Guid id)
        : base(id, Guid.Empty)
    {
        Nombre = null!;
        Clave = null!;
        TextoConsentimiento = null!;
    }

    private Formulario(Guid id, Guid empresaId, string nombre, string clave, string textoConsentimiento, bool pideTelefono, bool pideEmpresa, bool pideMensaje, string? paginaGracias, string origen, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Nombre = nombre;
        Clave = clave;
        TextoConsentimiento = textoConsentimiento;
        PideTelefono = pideTelefono;
        PideEmpresa = pideEmpresa;
        PideMensaje = pideMensaje;
        PaginaGracias = paginaGracias;
        Origen = origen;
        Activo = true;
        CreadoEn = ahora;
    }

    public string Nombre { get; private set; }

    /// <summary>Clave pública del formulario. Va en la URL del script; no es un secreto.</summary>
    public string Clave { get; private set; }

    /// <summary>
    /// El texto que la persona acepta. **Es obligatorio**: sin él no hay prueba de qué consintió, y
    /// sin prueba no se le puede escribir nada después.
    /// </summary>
    public string TextoConsentimiento { get; private set; }

    public bool PideTelefono { get; private set; }

    public bool PideEmpresa { get; private set; }

    public bool PideMensaje { get; private set; }

    public string? PaginaGracias { get; private set; }

    /// <summary>Origen que se pone a los contactos que entren por aquí. Alimenta el Encaje.</summary>
    public string Origen { get; private set; } = "formulario web";

    public bool Activo { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public static Resultado<Formulario> Crear(
        Guid empresaId, string? nombre, string? textoConsentimiento, bool pideTelefono,
        bool pideEmpresa, bool pideMensaje, string? paginaGracias, string? origen, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var error = Validar(nombre, textoConsentimiento, paginaGracias);
        if (error is not null)
        {
            return Resultado.Fallo<Formulario>(error);
        }

        return Resultado.Ok(new Formulario(
            Guid.NewGuid(), empresaId, nombre!.Trim(), NuevaClave(), textoConsentimiento!.Trim(),
            pideTelefono, pideEmpresa, pideMensaje,
            string.IsNullOrWhiteSpace(paginaGracias) ? null : paginaGracias.Trim(),
            string.IsNullOrWhiteSpace(origen) ? "formulario web" : origen.Trim().ToLowerInvariant(),
            reloj.AhoraUtc));
    }

    public Resultado Actualizar(string? nombre, string? textoConsentimiento, bool pideTelefono, bool pideEmpresa, bool pideMensaje, string? paginaGracias)
    {
        var error = Validar(nombre, textoConsentimiento, paginaGracias);
        if (error is not null)
        {
            return Resultado.Fallo(error);
        }

        Nombre = nombre!.Trim();
        TextoConsentimiento = textoConsentimiento!.Trim();
        PideTelefono = pideTelefono;
        PideEmpresa = pideEmpresa;
        PideMensaje = pideMensaje;
        PaginaGracias = string.IsNullOrWhiteSpace(paginaGracias) ? null : paginaGracias.Trim();
        return Resultado.Ok();
    }

    public void Desactivar() => Activo = false;

    /// <summary>Clave de URL: aleatoria, sin caracteres que se confundan al dictarla por teléfono.</summary>
    private static string NuevaClave()
    {
        const string alfabeto = "abcdefghijkmnpqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(LongitudClave);
        return string.Create(LongitudClave, bytes, (destino, origen) =>
        {
            for (var i = 0; i < destino.Length; i++)
            {
                destino[i] = alfabeto[origen[i] % alfabeto.Length];
            }
        });
    }

    private static Error? Validar(string? nombre, string? textoConsentimiento, string? paginaGracias)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Error.Validacion("formulario.nombre_vacio", "El formulario necesita un nombre.");
        }

        if (nombre.Trim().Length > LongitudMaximaNombre)
        {
            return Error.Validacion("formulario.nombre_largo", "El nombre del formulario es demasiado largo.");
        }

        if (string.IsNullOrWhiteSpace(textoConsentimiento))
        {
            return Error.Validacion("formulario.sin_consentimiento", "Hay que escribir el texto que la persona acepta.");
        }

        if (textoConsentimiento.Trim().Length > LongitudMaximaTexto)
        {
            return Error.Validacion("formulario.consentimiento_largo", "El texto de consentimiento es demasiado largo.");
        }

        if (!string.IsNullOrWhiteSpace(paginaGracias) && !EsDireccionWeb(paginaGracias))
        {
            return Error.Validacion("formulario.gracias_invalida", "La página de gracias tiene que ser una dirección web completa.");
        }

        return null;
    }

    private static bool EsDireccionWeb(string valor)
    {
        // Solo http/https: la página de gracias acaba en un `location.href` del navegador del
        // visitante, y ahí un `javascript:` sería un agujero abierto de par en par.
        return Uri.TryCreate(valor.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
