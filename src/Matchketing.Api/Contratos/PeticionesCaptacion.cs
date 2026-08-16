namespace Matchketing.Api.Contratos;

public sealed record PeticionFormulario(string? Nombre, string? TextoConsentimiento, bool PideTelefono, bool PideEmpresa, bool PideMensaje, string? PaginaGracias, string? Origen);

/// <summary>Lo que llega desde la web del cliente. <c>Consiente</c> tiene que venir en true.</summary>
public sealed record PeticionLead(string? Nombre, string? Email, string? Telefono, string? Empresa, string? Mensaje, bool Consiente);

public sealed record PeticionVisita(Guid ContactoId);
