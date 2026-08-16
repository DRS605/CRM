namespace Matchketing.Api.Contratos;

public sealed record PeticionTarea(string? Titulo, Guid? ContactoId, Guid? OportunidadId, DateOnly? VenceEl);

public sealed record PeticionAplazar(DateOnly? Hasta);
