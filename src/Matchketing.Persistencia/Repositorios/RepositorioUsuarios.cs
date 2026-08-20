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

    /// <summary>
    /// Las de una empresa, activas y no activas: quien ya no entra sigue saliendo en la lista, porque
    /// sus contactos siguen asignados a su nombre y hay que poder verlo.
    ///
    /// `membresia` no lleva filtro global —ver la nota de `OnModelCreating`—, así que aquí el `WHERE`
    /// por empresa **es la única barrera de EF** y no puede faltar.
    /// </summary>
    public async Task<IReadOnlyList<Membresia>> DeEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        await bd.Membresias.Where(m => m.EmpresaId == empresaId).ToListAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Por identificador **y** empresa, nunca por identificador a secas: sin la empresa, un id de otra
    /// empresa devolvería su fila, porque esta tabla no tiene filtro global que lo impida.
    /// </summary>
    public Task<Membresia?> BuscarPorIdAsync(Guid id, Guid empresaId, CancellationToken ct = default) =>
        bd.Membresias.FirstOrDefaultAsync(m => m.Id == id && m.EmpresaId == empresaId, ct);

    public void Anadir(Membresia membresia) => bd.Membresias.Add(membresia);
}

public sealed class RepositorioInvitaciones(ContextoMatchketing bd) : IRepositorioInvitaciones
{
    /// <summary>
    /// Por la huella del token. La tabla **sí** tiene filtro global y política de RLS, así que quien
    /// llame desde un endpoint público tiene que haber fijado antes la empresa que va dentro del token
    /// (`Invitacion.EmpresaDelToken`); si no, aquí no aparece ninguna fila.
    /// </summary>
    public Task<Invitacion?> BuscarPorHuellaAsync(string huella, CancellationToken ct = default) =>
        bd.Invitaciones.FirstOrDefaultAsync(i => i.HuellaToken == huella, ct);

    public Task<Invitacion?> BuscarPorIdAsync(Guid id, Guid empresaId, CancellationToken ct = default) =>
        bd.Invitaciones.FirstOrDefaultAsync(i => i.Id == id && i.EmpresaId == empresaId, ct);

    public async Task<IReadOnlyList<Invitacion>> VivasDeEmpresaAsync(Guid empresaId, CancellationToken ct = default) =>
        await bd.Invitaciones
            .Where(i => i.EmpresaId == empresaId && i.AceptadaEn == null && i.RetiradaEn == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void Anadir(Invitacion invitacion) => bd.Invitaciones.Add(invitacion);
}

/// <summary>
/// Nombres y correos de personas concretas. `identidad.usuario` es una tabla **global** —una persona
/// puede trabajar en varias empresas—, así que aquí no hay filtro por empresa que aplicar: la
/// protección es que solo se piden identificadores que ya salieron de las membresías de la empresa.
/// </summary>
public sealed class ConsultaPersonas(ContextoMatchketing bd) : IConsultaPersonas
{
    public async Task<IReadOnlyList<Persona>> DeIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return [];
        }

        return await bd.Usuarios
            .Where(u => ids.Contains(u.Id))
            .Select(u => new Persona(u.Id, u.Nombre, u.Email, u.UltimoAccesoEn))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
