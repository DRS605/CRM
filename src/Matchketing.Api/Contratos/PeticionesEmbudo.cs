using Matchketing.Embudo.Dominio;

namespace Matchketing.Api.Contratos;

public sealed record PeticionOportunidad(Guid ContactoId, Guid? CuentaId, string? Titulo, decimal Importe, Guid? EtapaId, DateOnly? PrevistaCierre);

public sealed record PeticionActualizarOportunidad(string? Titulo, decimal Importe, DateOnly? PrevistaCierre, Guid? PropietarioId);

public sealed record PeticionMover(Guid EtapaId);

public sealed record PeticionPerder(MotivoPerdida? Motivo, string? Detalle);
