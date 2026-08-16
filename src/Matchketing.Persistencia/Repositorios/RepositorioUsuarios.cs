using Matchketing.Identidad.Aplicacion;
using Matchketing.Identidad.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioUsuarios(ContextoMatchketing bd) : IRepositorioUsuarios
{
    public Task<Usuario?> BuscarPorEmailAsync(string email, CancellationToken ct = default) =>
        bd.Usuarios.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<Usuario?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> ExisteEmailAsync(string email, CancellationToken ct = default) =>
        bd.Usuarios.AnyAsync(u => u.Email == email, ct);

    public void Anadir(Usuario usuario) => bd.Usuarios.Add(usuario);
}

public sealed class RepositorioMembresias(ContextoMatchketing bd) : IRepositorioMembresias
{
    public Task<Membresia?> BuscarAsync(Guid usuarioId, Guid empresaId, CancellationToken ct = default) =>
        bd.Membresias.FirstOrDefaultAsync(m => m.UsuarioId == usuarioId && m.EmpresaId == empresaId, ct);

    public async Task<IReadOnlyList<Membresia>> DeUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        await bd.Membresias.Where(m => m.UsuarioId == usuarioId && m.Activa).ToListAsync(ct).ConfigureAwait(false);

    public Task<int> ContarPropietariosAsync(Guid empresaId, CancellationToken ct = default) =>
        bd.Membresias.CountAsync(m => m.EmpresaId == empresaId && m.Activa && m.Rol == Rol.Propietario, ct);

    public void Anadir(Membresia membresia) => bd.Membresias.Add(membresia);
}
