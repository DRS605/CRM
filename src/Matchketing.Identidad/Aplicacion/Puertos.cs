using Matchketing.Identidad.Dominio;

namespace Matchketing.Identidad.Aplicacion;

public interface IRepositorioUsuarios
{
    Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct = default);

    Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct = default);

    Task<bool> ExisteEmailAsync(string email, CancellationToken ct = default);

    void Anadir(Usuario usuario);
}

public interface IRepositorioMembresias
{
    Task<Membresia?> BuscarAsync(Guid usuarioId, Guid empresaId, CancellationToken ct = default);

    Task<IReadOnlyList<Membresia>> DeUsuarioAsync(Guid usuarioId, CancellationToken ct = default);

    Task<int> ContarPropietariosAsync(Guid empresaId, CancellationToken ct = default);

    void Anadir(Membresia membresia);
}

/// <summary>Hashing de contraseñas. Detrás hay PBKDF2; el dominio no lo sabe ni le importa.</summary>
public interface IHasherContrasena
{
    string Hashear(string contrasenaEnClaro);

    bool Verificar(string contrasenaEnClaro, string hash);
}

public interface IGeneradorTokens
{
    TokenGenerado Generar(Usuario usuario, Membresia? membresia, string? nombreEmpresa);
}

public interface IUnidadDeTrabajo
{
    Task<int> GuardarCambiosAsync(CancellationToken ct = default);
}

public sealed record TokenGenerado(string Token, DateTimeOffset ExpiraEn);
