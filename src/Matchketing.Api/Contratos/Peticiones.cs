namespace Matchketing.Api.Contratos;

public sealed record PeticionRegistro(string? Email, string? Contrasena, string? Nombre);

public sealed record PeticionLogin(string? Email, string? Contrasena);

public sealed record PeticionEmpresa(string? Nombre, string? Nif, string? Provincia);

public sealed record PeticionAjustesMatch(decimal PesoEncaje, int HorasRebote);

/// <summary>
/// El interruptor de la medición de aperturas. Va en su propia petición y no colgado de los datos de
/// la empresa: encenderlo es decidir que se mide el comportamiento de una persona, y una decisión así
/// no se toma de rebote al corregir una errata en el nombre.
/// </summary>
public sealed record PeticionAjustesCorreo(bool SigueAperturas);
