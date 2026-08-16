using Matchketing.Contactos.Aplicacion;
using Matchketing.Contactos.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioContactos(ContextoMatchketing bd) : IRepositorioContactos
{
    public Task<Contacto?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Contactos.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<Contacto?> BuscarPorEmailAsync(string email, CancellationToken ct = default) =>
        bd.Contactos.FirstOrDefaultAsync(c => c.Activo && c.Email == email, ct);

    public Task<Contacto?> BuscarPorTelefonoAsync(string telefono, CancellationToken ct = default) =>
        bd.Contactos.FirstOrDefaultAsync(c => c.Activo && c.Telefono == telefono, ct);

    public async Task<IReadOnlyList<Contacto>> ActivosAsync(CancellationToken ct = default) =>
        await bd.Contactos.Where(c => c.Activo).OrderBy(c => c.Nombre).ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(Contacto contacto) => bd.Contactos.Add(contacto);
}

public sealed class RepositorioCuentas(ContextoMatchketing bd) : IRepositorioCuentas
{
    public Task<Cuenta?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Cuentas.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Cuenta>> ActivasAsync(CancellationToken ct = default) =>
        await bd.Cuentas.Where(c => c.Activa).OrderBy(c => c.Nombre).ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(Cuenta cuenta) => bd.Cuentas.Add(cuenta);
}

public sealed class RepositorioActividades(ContextoMatchketing bd) : IRepositorioActividades
{
    public async Task<IReadOnlyList<Actividad>> DeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        await bd.Actividades.Where(a => a.ContactoId == contactoId)
            .OrderByDescending(a => a.OcurridaEn).ToListAsync(ct).ConfigureAwait(false);

    /// <summary>Mueve las actividades de un contacto a otro al fusionar. No se pierde ninguna (C4).</summary>
    public async Task<int> ReasignarAsync(Guid deContactoId, Guid aContactoId, CancellationToken ct = default)
    {
        var lista = await bd.Actividades.Where(a => a.ContactoId == deContactoId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var a in lista)
        {
            a.ReasignarA(aContactoId);
        }

        return lista.Count;
    }

    public void Anadir(Actividad actividad) => bd.Actividades.Add(actividad);
}
