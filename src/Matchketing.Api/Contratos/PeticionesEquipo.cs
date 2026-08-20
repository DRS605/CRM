using Matchketing.Identidad.Dominio;

namespace Matchketing.Api.Contratos;

public sealed record PeticionInvitacion(string? Email, Rol Rol);

public sealed record PeticionRol(Rol Rol);

public sealed record PeticionZonas(string? Zonas);

/// <summary>
/// Aceptar una invitación. El nombre solo hace falta si la persona no tiene cuenta todavía; la
/// contraseña, siempre: si ya tiene cuenta es **su** contraseña la que prueba que es ella, y si no la
/// tiene es la que elige ahora. Quien invita no ve ninguna de las dos.
/// </summary>
public sealed record PeticionAceptar(string? Nombre, string? Contrasena);
