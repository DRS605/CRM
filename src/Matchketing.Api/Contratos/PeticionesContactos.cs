using Matchketing.Contactos.Dominio;

namespace Matchketing.Api.Contratos;

public sealed record PeticionContacto(string? Nombre, string? Email, string? Telefono, string? Cargo, Guid? CuentaId, string? Origen);

public sealed record PeticionActualizarContacto(string? Nombre, string? Email, string? Telefono, string? Cargo, Guid? CuentaId, Guid? PropietarioId);

public sealed record PeticionEstado(EstadoContacto Estado);

public sealed record PeticionNota(string? Cuerpo);

public sealed record PeticionLlamada(ResultadoLlamada Resultado, string? Nota);

public sealed record PeticionFusion(Guid AbsorbidoId);

public sealed record PeticionImportacion(string? Contenido, bool Previsualizar);

public sealed record PeticionCuenta(string? Nombre, string? Nif, string? Sector, string? Provincia, int? Tamano, string? Web);
