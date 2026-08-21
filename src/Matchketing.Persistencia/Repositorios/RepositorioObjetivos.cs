using Matchketing.Identidad.Dominio;
using Matchketing.Objetivos.Aplicacion;
using Matchketing.Objetivos.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioObjetivos(ContextoMatchketing bd) : IRepositorioObjetivos
{
    public Task<Objetivo?> DeAsync(Guid usuarioId, DateOnly mes, CancellationToken ct = default) =>
        bd.Objetivos.FirstOrDefaultAsync(o => o.UsuarioId == usuarioId && o.Mes == mes, ct);

    public async Task<IReadOnlyList<Objetivo>> DelMesAsync(DateOnly mes, CancellationToken ct = default) =>
        await bd.Objetivos.Where(o => o.Mes == mes).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<Objetivo>> DePersonaAsync(
        Guid usuarioId, int cuantos, CancellationToken ct = default) =>
        await bd.Objetivos
            .Where(o => o.UsuarioId == usuarioId)
            .OrderByDescending(o => o.Mes)
            .Take(cuantos)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void Anadir(Objetivo objetivo) => bd.Objetivos.Add(objetivo);

    public void Quitar(Objetivo objetivo) => bd.Objetivos.Remove(objetivo);
}

/// <summary>
/// Cuánto ha cerrado cada persona en un mes.
///
/// Vive aquí porque cruza el embudo con la identidad, y el módulo de objetivos no conoce ninguno de los
/// dos. Tres decisiones dentro, y las tres cambian el número:
///
/// · **Solo ganadas.** `Motivo == null` en una oportunidad cerrada es lo que significa «ganada» en este
///   sistema —perderla exige motivo, invariante O1—. Contar las cerradas a secas sumaría las perdidas.
/// · **Por fecha de cierre**, no de creación. Un objetivo de venta se cumple cuando se firma, no cuando
///   se empieza a hablar.
/// · **Al propietario de la oportunidad**, que es quien la cerró, no a quien creó el contacto hace ocho
///   meses. Una oportunidad sin propietario no cuenta para nadie: repartirla o dársela a alguien por
///   defecto sería inventar un mérito.
/// </summary>
public sealed class ConsultaLogrado(ContextoMatchketing bd) : IConsultaLogrado
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> GanadoPorPersonaAsync(
        DateOnly mes, CancellationToken ct = default)
    {
        var (desde, hasta) = Limites(mes);

        var filas = await bd.Oportunidades
            .Where(o => o.CerradaEn != null && o.Motivo == null && o.PropietarioId != null)
            .Where(o => o.CerradaEn >= desde && o.CerradaEn < hasta)
            .GroupBy(o => o.PropietarioId!.Value)
            .Select(g => new { UsuarioId = g.Key, Importe = g.Sum(o => o.Importe) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return filas.ToDictionary(f => f.UsuarioId, f => f.Importe);
    }

    public async Task<decimal> GanadoDeAsync(Guid usuarioId, DateOnly mes, CancellationToken ct = default)
    {
        var (desde, hasta) = Limites(mes);

        // `SumAsync` sobre un conjunto vacío devuelve 0, que es lo que se quiere: quien no ha cerrado
        // nada este mes lleva cero, no «no se sabe».
        return await bd.Oportunidades
            .Where(o => o.CerradaEn != null && o.Motivo == null && o.PropietarioId == usuarioId)
            .Where(o => o.CerradaEn >= desde && o.CerradaEn < hasta)
            .SumAsync(o => o.Importe, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// El mes en instantes, abierto por arriba: `>= día 1` y `< día 1 del siguiente`.
    ///
    /// Con `<=` al último día se perdería todo lo cerrado ese día después de medianoche, que es
    /// justamente cuando se firma lo que se firma a final de mes.
    /// </summary>
    private static (DateTimeOffset Desde, DateTimeOffset Hasta) Limites(DateOnly mes)
    {
        var primero = Objetivo.MesDe(mes);
        return (
            new DateTimeOffset(primero.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(primero.AddMonths(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }
}

/// <summary>
/// Quién hay en el equipo, para poder ponerles objetivo.
///
/// <c>Vende</c> se decide por el permiso de gestionar oportunidades y no por el rol: un objetivo de
/// venta para quien no puede tocar una oportunidad es un objetivo que no puede cumplir. Si algún día se
/// añade un rol nuevo que venda, esto lo recoge sin tocarlo.
/// </summary>
public sealed class ConsultaEquipoObjetivos(ContextoMatchketing bd) : IConsultaEquipoObjetivos
{
    public async Task<IReadOnlyList<QuienVende>> ActivosAsync(CancellationToken ct = default)
    {
        var empresa = bd.EmpresaActual;
        if (empresa is null)
        {
            return [];
        }

        // `identidad.membresia` no lleva filtro global por empresa —es la tabla que decide a qué
        // empresas puede entrar alguien—, así que aquí se filtra a mano. Es la excepción documentada en
        // `ContextoMatchketing`, y olvidarla aquí habría dado el equipo de todas las empresas.
        var miembros = await bd.Membresias
            .Where(m => m.EmpresaId == empresa && m.Activa)
            .Select(m => new { m.UsuarioId, m.Rol })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (miembros.Count == 0)
        {
            return [];
        }

        var ids = miembros.Select(m => m.UsuarioId).ToArray();
        var nombres = await bd.Usuarios
            .IgnoreQueryFilters()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.Nombre })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var porId = nombres.ToDictionary(n => n.Id, n => n.Nombre);

        return miembros
            .Where(m => porId.ContainsKey(m.UsuarioId))
            .Select(m => new QuienVende(
                m.UsuarioId,
                porId[m.UsuarioId],
                PermisosDeRol.De(m.Rol).Contains(Permisos.OportunidadGestionar, StringComparer.Ordinal)))
            .ToList();
    }
}
