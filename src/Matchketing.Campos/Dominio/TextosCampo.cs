namespace Matchketing.Campos.Dominio;

/// <summary>
/// Los nombres públicos del ámbito y del tipo.
///
/// La API habla de «contacto» y de «numero», no de 1 y de 2. Es la misma decisión que se tomó en
/// campañas y por el mismo motivo: cuando algo no funciona, lo primero que se hace es mirar la
/// respuesta, y un `2` obliga a ir a buscar el enumerado. Además el número ata el contrato al orden en
/// que se escribieron los valores, y ese orden no es un contrato: el nombre sí.
///
/// Se escriben sin tildes ni mayúsculas porque van en JSON y en rutas.
/// </summary>
public static class TextosCampo
{
    public static string De(Ambito ambito) => ambito switch
    {
        Ambito.Cuenta => "cuenta",
        _ => "contacto",
    };

    public static Ambito? AmbitoDe(string? texto) => (texto ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "contacto" => Ambito.Contacto,
        "cuenta" => Ambito.Cuenta,
        _ => null,
    };

    public static string De(TipoCampo tipo) => tipo switch
    {
        TipoCampo.Numero => "numero",
        TipoCampo.Fecha => "fecha",
        TipoCampo.SiNo => "si_no",
        TipoCampo.Lista => "lista",
        _ => "texto",
    };

    public static TipoCampo? TipoDe(string? texto) => (texto ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "texto" => TipoCampo.Texto,
        "numero" => TipoCampo.Numero,
        "fecha" => TipoCampo.Fecha,
        "si_no" => TipoCampo.SiNo,
        "lista" => TipoCampo.Lista,
        _ => null,
    };
}
