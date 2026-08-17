using Matchketing.Cumplimiento.Dominio;

namespace Matchketing.Api.Contratos;

public sealed record PeticionConsentimiento(FinalidadConsentimiento Finalidad, BaseLegal Base, string? Canal, string? TextoAceptado);

public sealed record PeticionRetirada(FinalidadConsentimiento Finalidad);

public sealed record PeticionAjustesRetencion(int MesesRetencionLeads);

/// <summary>Borrar la empresa exige teclear su nombre. Ver <c>ServicioCumplimiento.BorrarEmpresaAsync</c>.</summary>
public sealed record PeticionBorrarEmpresa(string? Confirmacion);

public sealed record PeticionCambioContrasena(string? Actual, string? Nueva);
