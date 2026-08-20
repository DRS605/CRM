using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;

namespace Matchketing.Identidad.Tests;

/// <summary>
/// Repositorios en memoria para probar <see cref="ServicioEquipo"/> sin base de datos. Lo que se
/// prueba aquí son las reglas —quién puede cambiar qué, y qué no se puede dejar sin propietario—, y
/// esas no dependen de PostgreSQL. El aislamiento y la RLS se prueban en integración.
/// </summary>
internal sealed class RepoUsuarios : IRepositorioUsuarios, IConsultaPersonas
{
    private readonly List<Usuario> lista = [];

    public IReadOnlyList<Usuario> Todos => lista;

    public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(lista.FirstOrDefault(u => u.Email == email));

    public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(lista.FirstOrDefault(u => u.Id == id));

    public Task<bool> ExisteEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult(lista.Any(u => u.Email == email));

    public void Anadir(Usuario usuario) => lista.Add(usuario);

    public Task<IReadOnlyList<Persona>> DeIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Persona>>(lista
            .Where(u => ids.Contains(u.Id))
            .Select(u => new Persona(u.Id, u.Nombre, u.Email, u.UltimoAccesoEn))
            .ToList());
}

internal sealed class RepoMembresias : IRepositorioMembresias
{
    private readonly List<Membresia> lista = [];

    public Task<Membresia?> BuscarAsync(Guid usuarioId, Guid empresaId, CancellationToken ct = default) =>
        Task.FromResult(lista.FirstOrDefault(m => m.UsuarioId == usuarioId && m.EmpresaId == empresaId));

    public Task<IReadOnlyList<Membresia>> DeUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Membresia>>(lista.Where(m => m.UsuarioId == usuarioId && m.Activa).ToList());

    public Task<int> ContarPropietariosAsync(Guid empresaId, CancellationToken ct = default) =>
        Task.FromResult(lista.Count(m => m.EmpresaId == empresaId && m.Activa && m.Rol == Rol.Propietario));

    public Task<IReadOnlyList<Membresia>> DeEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Membresia>>(lista.Where(m => m.EmpresaId == empresaId).ToList());

    public Task<Membresia?> BuscarPorIdAsync(Guid id, Guid empresaId, CancellationToken ct = default) =>
        Task.FromResult(lista.FirstOrDefault(m => m.Id == id && m.EmpresaId == empresaId));

    public void Anadir(Membresia membresia) => lista.Add(membresia);
}

internal sealed class RepoInvitaciones : IRepositorioInvitaciones
{
    private readonly List<Invitacion> lista = [];

    public Task<Invitacion?> BuscarPorHuellaAsync(string huella, CancellationToken ct = default) =>
        Task.FromResult(lista.FirstOrDefault(i => i.HuellaToken == huella));

    public Task<Invitacion?> BuscarPorIdAsync(Guid id, Guid empresaId, CancellationToken ct = default) =>
        Task.FromResult(lista.FirstOrDefault(i => i.Id == id && i.EmpresaId == empresaId));

    public Task<IReadOnlyList<Invitacion>> VivasDeEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Invitacion>>(lista
            .Where(i => i.EmpresaId == empresaId && i.AceptadaEn is null && i.RetiradaEn is null)
            .ToList());

    public void Anadir(Invitacion invitacion) => lista.Add(invitacion);
}

/// <summary>Hasher de juguete: reversible a propósito, para poder afirmar sobre el resultado.</summary>
internal sealed class HasherDeJuguete : IHasherContrasena
{
    public string Hashear(string contrasenaEnClaro) => "hash:" + contrasenaEnClaro;

    public bool Verificar(string contrasenaEnClaro, string hash) => hash == Hashear(contrasenaEnClaro);
}
