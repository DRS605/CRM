using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Identidad.Dominio;

/// <summary>Se ha registrado un usuario.</summary>
public sealed record UsuarioRegistrado(Guid UsuarioId, string Email, DateTimeOffset OcurridoEn) : IEventoDominio;

/// <summary>
/// Persona que accede al sistema. Es global, no pertenece a ninguna empresa: la pertenencia se
/// expresa con <see cref="Membresia"/>, para que una misma persona pueda trabajar en varias.
/// </summary>
public sealed class Usuario : RaizAgregado<Guid>
{
    public const int LongitudMinimaContrasena = 8;
    public const int LongitudMaximaNombre = 120;

    private Usuario(Guid id)
        : base(id)
    {
        Email = null!;
        HashContrasena = null!;
        Nombre = null!;
    }

    private Usuario(Guid id, string email, string hashContrasena, string nombre, DateTimeOffset ahora)
        : base(id)
    {
        Email = email;
        HashContrasena = hashContrasena;
        Nombre = nombre;
        Activo = true;
        EmailVerificado = false;
        CreadoEn = ahora;
        ActualizadoEn = ahora;
    }

    /// <summary>Correo normalizado. Es la identidad de acceso y es único en todo el sistema.</summary>
    public string Email { get; private set; }

    public string HashContrasena { get; private set; }

    public string Nombre { get; private set; }

    public bool EmailVerificado { get; private set; }

    public bool Activo { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    public DateTimeOffset? UltimoAccesoEn { get; private set; }

    public static Resultado<Usuario> Registrar(string? email, string? contrasenaEnClaro, string? nombre, Func<string, string> hashear, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(hashear);
        ArgumentNullException.ThrowIfNull(reloj);

        // Nombre completo: la propiedad `Email` de esta clase oculta al tipo `Email` del núcleo.
        var correo = Nucleo.Comun.Email.Crear(email);
        if (correo.Fallido)
        {
            return Resultado.Fallo<Usuario>(correo.Error!);
        }

        var errorNombre = ValidarNombre(nombre);
        if (errorNombre is not null)
        {
            return Resultado.Fallo<Usuario>(errorNombre);
        }

        var errorContrasena = ValidarContrasena(contrasenaEnClaro);
        if (errorContrasena is not null)
        {
            return Resultado.Fallo<Usuario>(errorContrasena);
        }

        var usuario = new Usuario(Guid.NewGuid(), correo.Valor.Valor, hashear(contrasenaEnClaro!), nombre!.Trim(), reloj.AhoraUtc);
        usuario.RegistrarEvento(new UsuarioRegistrado(usuario.Id, usuario.Email, reloj.AhoraUtc));
        return Resultado.Ok(usuario);
    }

    public void RegistrarAcceso(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        UltimoAccesoEn = reloj.AhoraUtc;
    }

    public Resultado CambiarNombre(string? nombre, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var error = ValidarNombre(nombre);
        if (error is not null)
        {
            return Resultado.Fallo(error);
        }

        Nombre = nombre!.Trim();
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    public void VerificarEmail(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);
        EmailVerificado = true;
        ActualizadoEn = reloj.AhoraUtc;
    }

    /// <summary>
    /// Requisitos deliberadamente modestos: longitud mínima y algo que no sea solo letras. Exigir
    /// símbolos raros empuja a la gente a apuntar la contraseña en un papel.
    /// </summary>
    public static Error? ValidarContrasena(string? contrasena)
    {
        if (string.IsNullOrWhiteSpace(contrasena) || contrasena.Length < LongitudMinimaContrasena)
        {
            return Error.Validacion("contrasena.corta", $"La contraseña debe tener al menos {LongitudMinimaContrasena} caracteres.");
        }

        if (!contrasena.Any(char.IsLetter) || !contrasena.Any(char.IsDigit))
        {
            return Error.Validacion("contrasena.debil", "La contraseña debe combinar letras y números.");
        }

        return null;
    }

    private static Error? ValidarNombre(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Error.Validacion("usuario.nombre_vacio", "El nombre es obligatorio.");
        }

        return nombre.Trim().Length > LongitudMaximaNombre
            ? Error.Validacion("usuario.nombre_largo", "El nombre es demasiado largo.")
            : null;
    }
}
