using Matchketing.Cumplimiento.Dominio;

namespace Matchketing.Cumplimiento.Aplicacion;

/// <summary>
/// El secreto con el que se firman los enlaces de baja y la dirección donde vive la página que los
/// atiende. Cambiar el secreto invalida todos los enlaces emitidos: es la única forma de revocarlos
/// y no debería hacer falta nunca.
/// </summary>
public sealed record AjustesBaja(string Secreto, string UrlBase);

/// <summary>Un consentimiento, contado en castellano para la ficha del contacto.</summary>
public sealed record LineaConsentimiento(
    Guid Id, string Finalidad, string BaseLegal, string Canal, string? TextoAceptado,
    DateTimeOffset OtorgadoEn, DateTimeOffset? RetiradoEn, bool Vigente);

/// <summary>
/// Qué se le puede hacer a este contacto y por qué. Es lo que pinta el panel de privacidad de la
/// ficha: la respuesta a «¿puedo mandarle esta promoción?» dicha en una frase, no deducida a ojo
/// leyendo una lista de casillas.
/// </summary>
public sealed record FichaCumplimiento(
    Guid ContactoId,
    bool DeBaja,
    bool PuedeEnviarComercial,
    string Explicacion,
    string EnlaceBaja,
    IReadOnlyList<LineaConsentimiento> Consentimientos);

/// <summary>Lo que se llevó por delante la retención de una empresa.</summary>
public sealed record ResultadoRetencion(int Meses, int LeadsBorrados, int FilasBorradas);

/// <summary>Nombres en castellano de las enumeraciones del módulo. La interfaz va en castellano.</summary>
public static class Textos
{
    public static string De(FinalidadConsentimiento finalidad) => finalidad switch
    {
        FinalidadConsentimiento.AtenderSolicitud => "atender su solicitud",
        FinalidadConsentimiento.Comercial => "comunicaciones comerciales",
        _ => "sin especificar",
    };

    public static string De(BaseLegal baseLegal) => baseLegal switch
    {
        BaseLegal.Consentimiento => "consentimiento",
        BaseLegal.InteresLegitimo => "interés legítimo",
        BaseLegal.Contrato => "contrato",
        _ => "sin especificar",
    };
}
