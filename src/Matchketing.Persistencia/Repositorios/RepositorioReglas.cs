using Matchketing.Automatizacion.Aplicacion;
using Matchketing.Automatizacion.Dominio;
using Matchketing.Contactos.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioReglas(ContextoMatchketing bd) : IRepositorioReglas
{
    public Task<Regla?> PorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Reglas.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<IReadOnlyList<Regla>> DeLaEmpresaAsync(CancellationToken ct = default) =>
        await bd.Reglas.ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<Regla>> ActivasParaAsync(Disparador disparador, CancellationToken ct = default) =>
        await bd.Reglas
            .Where(r => r.Activa && r.Disparador == disparador)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void Anadir(Regla regla) => bd.Reglas.Add(regla);

    public void Quitar(Regla regla) => bd.Reglas.Remove(regla);

    /// <summary>
    /// Mira **también lo que está pendiente de guardar**, no solo lo que hay en la base.
    ///
    /// Hace falta porque en un mismo guardado pueden llegar dos eventos del mismo sujeto —crear un
    /// contacto y asignarlo en la misma operación— y las filas de ejecución todavía no están escritas.
    /// Sin esto, la regla actuaría dos veces y el índice único haría fallar el `SaveChanges` entero,
    /// tumbando la operación de negocio por culpa de una automatización.
    /// </summary>
    public async Task<bool> YaActuoAsync(Guid reglaId, Guid sujetoId, CancellationToken ct = default)
    {
        var pendiente = bd.ChangeTracker.Entries<Ejecucion>()
            .Any(e => e.State == EntityState.Added && e.Entity.ReglaId == reglaId && e.Entity.SujetoId == sujetoId);

        return pendiente
            || await bd.Ejecuciones.AnyAsync(e => e.ReglaId == reglaId && e.SujetoId == sujetoId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Ejecucion>> UltimasDeAsync(Guid reglaId, int cuantas, CancellationToken ct = default) =>
        await bd.Ejecuciones
            .Where(e => e.ReglaId == reglaId)
            .OrderByDescending(e => e.CuandoEn)
            .Take(cuantas)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void AnadirEjecucion(Ejecucion ejecucion) => bd.Ejecuciones.Add(ejecucion);
}

/// <summary>
/// Los datos sobre los que se evalúan las condiciones. Una consulta por sujeto, no una por regla.
///
/// **Mira primero lo que está pendiente de guardar y solo después la base.** Es lo más importante de esta
/// clase y costó encontrarlo: el despachador de eventos corre **antes** de `SaveChanges` —a propósito,
/// para que lo que hagan las reglas entre en la misma transacción— así que cuando se crea un contacto y
/// una regla escucha «lead.creado», la fila de ese contacto **todavía no existe en la base**. Una consulta
/// normal devolvía nulo, la regla no encontraba sobre qué decidir y no pasaba nada. Sin ningún error, sin
/// ninguna traza: la regla simplemente no funcionaba, y solo con los disparadores de contacto —los de
/// oportunidad sí iban, porque esa fila ya estaba guardada—.
///
/// Va también en dos pasos en vez de en un `join` externo: es más código y es previsible, y esto corre
/// una vez por disparo, no una vez por regla.
/// </summary>
public sealed class ConsultaHechos(ContextoMatchketing bd) : IConsultaHechos
{
    public async Task<Hechos?> DeContactoAsync(Guid contactoId, CancellationToken ct = default)
    {
        var contacto = await BuscarAsync<Contacto>(contactoId, ct).ConfigureAwait(false);
        if (contacto is null)
        {
            return null;
        }

        var cuenta = await CuentaAsync(contacto.CuentaId, ct).ConfigureAwait(false);
        return new Hechos(cuenta?.Provincia, contacto.Origen, cuenta?.Sector, null, null);
    }

    public async Task<Hechos?> DeOportunidadAsync(Guid oportunidadId, CancellationToken ct = default)
    {
        var oportunidad = await BuscarAsync<Embudo.Dominio.Oportunidad>(oportunidadId, ct).ConfigureAwait(false);
        if (oportunidad is null)
        {
            return null;
        }

        var contacto = await BuscarAsync<Contacto>(oportunidad.ContactoId, ct).ConfigureAwait(false);
        var cuenta = await CuentaAsync(contacto?.CuentaId, ct).ConfigureAwait(false);

        return new Hechos(
            cuenta?.Provincia,
            contacto?.Origen,
            cuenta?.Sector,
            oportunidad.Importe,
            oportunidad.Motivo?.ToString());
    }

    private async Task<Cuenta?> CuentaAsync(Guid? cuentaId, CancellationToken ct) =>
        cuentaId is not { } id ? null : await BuscarAsync<Cuenta>(id, ct).ConfigureAwait(false);

    /// <summary>
    /// La entidad, esté ya guardada o solo rastreada. El orden importa: primero lo pendiente, porque es
    /// justo el caso que no existe en la base todavía.
    /// </summary>
    private async Task<T?> BuscarAsync<T>(Guid id, CancellationToken ct)
        where T : Nucleo.Dominio.EntidadBase<Guid>
    {
        var pendiente = bd.ChangeTracker.Entries<T>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            .Select(e => e.Entity)
            .FirstOrDefault(e => e.Id == id);

        return pendiente ?? await bd.Set<T>().FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);
    }
}
