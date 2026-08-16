namespace Matchketing.Api.Contratos;

public sealed record PeticionRegistro(string? Email, string? Contrasena, string? Nombre);

public sealed record PeticionLogin(string? Email, string? Contrasena);

public sealed record PeticionEmpresa(string? Nombre, string? Nif, string? Provincia);

public sealed record PeticionAjustesMatch(decimal PesoEncaje, int HorasRebote);
