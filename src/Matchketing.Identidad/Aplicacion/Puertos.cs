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

    /// <summary>Las membresías de una empresa, activas y no activas. Es la lista del equipo.</summary>
    Task<IReadOnlyList<Membresia>> DeEmpresaAsync(Guid empresaId, CancellationToken ct = default);

    /// <summary>Una membresía por su identificador, dentro de la empresa que se dice. Nunca por el id a secas.</summary>
    Task<Membresia?> BuscarPorIdAsync(Guid id, Guid empresaId, CancellationToken ct = default);

    void Anadir(Membresia membresia);
}

public interface IRepositorioInvitaciones
{
    /// <summary>Busca por la huella del token. El token en claro no está guardado en ninguna parte.</summary>
    Task<Invitacion?> BuscarPorHuellaAsync(string huella, CancellationToken ct = default);

    Task<Invitacion?> BuscarPorIdAsync(Guid id, Guid empresaId, CancellationToken ct = default);

    /// <summary>Las que todavía sirven, para poder enseñarlas y retirarlas.</summary>
    Task<IReadOnlyList<Invitacion>> VivasDeEmpresaAsync(Guid empresaId, CancellationToken ct = default);

    void Anadir(Invitacion invitacion);
}

/// <summary>
/// Los nombres y correos de unos usuarios concretos. Va aparte de <see cref="IRepositorioUsuarios"/>
/// porque la lista del equipo necesita leer filas de `identidad.usuario`, que es una tabla **global**:
/// se pide por identificadores que ya salieron de las membresías de la empresa, nunca en abierto.
/// </summary>
public interface IConsultaPersonas
{
    Task<IReadOnlyList<Persona>> DeIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
}

public sealed record Persona(Guid Id, string Nombre, string Email, DateTimeOffset? UltimoAccesoEn);

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
