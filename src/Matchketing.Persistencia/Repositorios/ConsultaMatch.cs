using Matchketing.Contactos.Dominio;
using Matchketing.Identidad.Dominio;
using Matchketing.Match.Aplicacion;
using Matchketing.Match.Dominio;
using Matchketing.Nucleo.Comun;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>
/// Todo lo que el motor de Match necesita saber y que vive repartido por otros módulos: qué se gana,
/// cómo es un contacto y qué histórico tiene cada comercial.
/// </summary>
public sealed class ConsultaMatch(ContextoMatchketing bd, IContextoEmpresa contexto) : IConsultaMatch
{
    /// <summary>Cuántos sectores entran en el «top» del perfil.</summary>
    private const int SectoresTop = 3;

    public async Task<PerfilGanadas> PerfilAsync(CancellationToken ct = default)
    {
        var cerradas = await (
            from o in bd.Oportunidades
            where o.CerradaEn != null
            join c in bd.Contactos on o.ContactoId equals c.Id
            join cu in bd.Cuentas on c.CuentaId equals cu.Id into cus
            from cu in cus.DefaultIfEmpty()
            select new
            {
                Ganada = o.Motivo == null,
                c.Origen,
                Sector = cu != null ? cu.Sector : null,
                Provincia = cu != null ? cu.Provincia : null,
                Tamano = cu != null ? cu.Tamano : null,
            }).ToListAsync(ct).ConfigureAwait(false);

        var ganadas = cerradas.Where(x => x.Ganada).ToList();

        var sectores = ganadas
            .Where(g => g.Sector is not null)
            .GroupBy(g => g.Sector!)
            .OrderByDescending(g => g.Count())
            .Take(SectoresTop)
            .Select(g => g.Key)
            .ToList();

        var provincias = ganadas
            .Where(g => g.Provincia is not null)
            .Select(g => g.Provincia!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Un origen es «bueno» si convierte por encima de la media de la empresa. Con menos de tres
        // cierres no se juzga: dos casualidades no son una tendencia.
        var tasaGlobal = cerradas.Count > 0 ? (double)ganadas.Count / cerradas.Count : 0;
        var origenes = cerradas
            .GroupBy(x => x.Origen)
            .Where(g => g.Count() >= 3 && (double)g.Count(x => x.Ganada) / g.Count() > tasaGlobal)
            .Select(g => g.Key)
            .ToList();

        var tamanos = ganadas.Where(g => g.Tamano is not null).Select(g => g.Tamano!.Value).ToList();

        return new PerfilGanadas(
            sectores, provincias, origenes,
            tamanos.Count > 0 ? tamanos.Min() : null,
            tamanos.Count > 0 ? tamanos.Max() : null,
            cerradas.Count);
    }

    public async Task<DatosContacto?> DatosDeAsync(Guid contactoId, CancellationToken ct = default)
    {
        var fila = await ConsultaDatos(bd.Contactos.Where(c => c.Id == contactoId))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return fila is null
            ? null
            : new DatosContacto(fila.Sector, fila.Provincia, fila.Origen, fila.Tamano, fila.Email != null, fila.Telefono != null);
    }

    public async Task<IReadOnlyDictionary<Guid, DatosContacto>> DatosDeVariosAsync(IReadOnlyCollection<Guid> contactos, CancellationToken ct = default)
    {
        var filas = await ConsultaDatos(bd.Contactos.Where(c => contactos.Contains(c.Id)))
            .ToListAsync(ct).ConfigureAwait(false);

        return filas.ToDictionary(
            f => f.Id,
            f => new DatosContacto(f.Sector, f.Provincia, f.Origen, f.Tamano, f.Email != null, f.Telefono != null));
    }

    public async Task<IReadOnlyList<Guid>> ContactosActivosAsync(CancellationToken ct = default) =>
        await bd.Contactos
            .Where(c => c.Activo && (c.Estado == EstadoContacto.Lead || c.Estado == EstadoContacto.Cliente))
            .Select(c => c.Id)
            .ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<CandidatoComercial>> ComercialesAsync(string? sector, CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return [];
        }

        var miembros = await (
            from m in bd.Membresias
            where m.EmpresaId == empresaId && m.Activa && (m.Rol == Rol.Propietario || m.Rol == Rol.Comercial)
            join u in bd.Usuarios on m.UsuarioId equals u.Id
            select new { m.UsuarioId, u.Nombre, m.Zonas }).ToListAsync(ct).ConfigureAwait(false);

        if (miembros.Count == 0)
        {
            return [];
        }

        var ids = miembros.Select(m => m.UsuarioId).ToList();

        // Histórico de cierres por comercial, limitado al sector del lead cuando se conoce.
        var cierres = await (
            from o in bd.Oportunidades
            where o.CerradaEn != null && o.PropietarioId != null && ids.Contains(o.PropietarioId.Value)
            join c in bd.Contactos on o.ContactoId equals c.Id
            join cu in bd.Cuentas on c.CuentaId equals cu.Id into cus
            from cu in cus.DefaultIfEmpty()
            select new { Propietario = o.PropietarioId!.Value, Ganada = o.Motivo == null, Sector = cu != null ? cu.Sector : null })
            .ToListAsync(ct).ConfigureAwait(false);

        var abiertas = await bd.Oportunidades
            .Where(o => o.CerradaEn == null && o.PropietarioId != null)
            .GroupBy(o => o.PropietarioId!.Value)
            .Select(g => new { Propietario = g.Key, Cuantas = g.Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        // Velocidad de primera respuesta: cuánto tarda cada uno desde que el contacto entra hasta
        // que hay la primera actividad suya. Es el indicador que más ventas mueve y casi nadie mide.
        var respuestas = await (
            from a in bd.Actividades
            where a.AutorId != null && ids.Contains(a.AutorId.Value)
            join c in bd.Contactos on a.ContactoId equals c.Id
            group new { a.OcurridaEn, c.CreadoEn, c.Id } by new { Autor = a.AutorId!.Value, Contacto = c.Id } into g
            select new { g.Key.Autor, Primera = g.Min(x => x.OcurridaEn), Alta = g.Min(x => x.CreadoEn) })
            .ToListAsync(ct).ConfigureAwait(false);

        var velocidades = respuestas
            .GroupBy(r => r.Autor)
            .ToDictionary(g => g.Key, g => g.Average(r => Math.Max(0, (r.Primera - r.Alta).TotalHours)));

        return miembros.Select(m =>
        {
            var suyos = cierres.Where(x => x.Propietario == m.UsuarioId);
            if (sector is not null)
            {
                var delSector = suyos.Where(x => string.Equals(x.Sector, sector, StringComparison.OrdinalIgnoreCase)).ToList();
                if (delSector.Count > 0)
                {
                    suyos = delSector;
                }
            }

            var lista = suyos.ToList();

            return new CandidatoComercial(
                m.UsuarioId,
                m.Nombre,
                string.IsNullOrWhiteSpace(m.Zonas) ? [] : m.Zonas.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                lista.Count(x => x.Ganada),
                lista.Count,
                abiertas.FirstOrDefault(a => a.Propietario == m.UsuarioId)?.Cuantas ?? 0,
                velocidades.TryGetValue(m.UsuarioId, out var h) ? h : null);
        }).ToList();
    }

    public async Task<decimal> PesoEncajeAsync(CancellationToken ct = default)
    {
        if (contexto.EmpresaId is not { } empresaId)
        {
            return Organizacion.Dominio.Empresa.PesoEncajePorDefecto;
        }

        var peso = await bd.Empresas
            .Where(e => e.Id == empresaId)
            .Select(e => (decimal?)e.PesoEncaje)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return peso ?? Organizacion.Dominio.Empresa.PesoEncajePorDefecto;
    }

    /// <summary>
    /// «Sin atender» quiere decir sin **ninguna** actividad saliente: ni una llamada, ni un correo,
    /// ni una reunión. Las entrantes no cuentan —que el lead insista no significa que le hayamos
    /// atendido— y las internas tampoco: una nota que dice «parece interesante» no es atender a nadie.
    ///
    /// Se excluyen los que ya llevan un <see cref="TipoActividad.Rebote"/>: el rebote es una segunda
    /// oportunidad, no una rueda que gira cada noche hasta que el lead haya pasado por toda la
    /// plantilla y nadie se sienta responsable de él.
    ///
    /// El plazo corre desde el alta del contacto, que en el flujo que de verdad importa —un lead que
    /// entra por un formulario y se asigna en el mismo instante— es cuando se le asignó.
    /// </summary>
    public async Task<IReadOnlyList<LeadSinAtender>> LeadsSinAtenderAsync(CancellationToken ct = default) =>
        await bd.Contactos
            .Where(c => c.Activo && c.Estado == EstadoContacto.Lead && c.PropietarioId != null)
            .Where(c => !bd.Actividades.Any(a => a.ContactoId == c.Id
                && (a.Sentido == SentidoActividad.Saliente || a.Tipo == TipoActividad.Rebote)))
            .OrderBy(c => c.CreadoEn)
            .Select(c => new LeadSinAtender(c.Id, c.Nombre, c.PropietarioId!.Value, c.CreadoEn))
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>
    /// El filtro se aplica sobre <paramref name="origen"/>, nunca sobre la proyección: EF no sabe
    /// traducir un WHERE contra un registro ya proyectado.
    /// </summary>
    private IQueryable<FilaDatos> ConsultaDatos(IQueryable<Contacto> origen) =>
        from c in origen
        join cu in bd.Cuentas on c.CuentaId equals cu.Id into cus
        from cu in cus.DefaultIfEmpty()
        select new FilaDatos(
            c.Id, c.Origen, c.Email, c.Telefono,
            cu != null ? cu.Sector : null,
            cu != null ? cu.Provincia : null,
            cu != null ? cu.Tamano : null);

    private sealed record FilaDatos(Guid Id, string Origen, string? Email, string? Telefono, string? Sector, string? Provincia, int? Tamano);
}
