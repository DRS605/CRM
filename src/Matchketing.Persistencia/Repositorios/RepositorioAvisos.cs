using Matchketing.Avisos.Aplicacion;
using Matchketing.Avisos.Dominio;
using Matchketing.Contactos.Dominio;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Tareas.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioSuscripciones(ContextoMatchketing bd) : IRepositorioSuscripciones
{
    /// <summary>
    /// Busca **saltando el filtro de empresa**, y a propósito. El endpoint es único en todo el sistema,
    /// y si alguien cambia de empresa su móvil sigue siendo el mismo: sin esto, al suscribirse en la
    /// segunda empresa se intentaría insertar un endpoint repetido y saltaría un error de clave única
    /// que no significa nada para quien lo ve.
    /// </summary>
    public Task<SuscripcionAviso?> PorEndpointAsync(string endpoint, CancellationToken ct = default) =>
        bd.Suscripciones.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);

    public async Task<IReadOnlyList<SuscripcionAviso>> DeUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        await bd.Suscripciones
            .Where(s => s.UsuarioId == usuarioId)
            .OrderBy(s => s.CreadoEn)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<SuscripcionAviso>> DeLaEmpresaAsync(CancellationToken ct = default) =>
        await bd.Suscripciones.OrderBy(s => s.UsuarioId).ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(SuscripcionAviso suscripcion) => bd.Suscripciones.Add(suscripcion);

    public void Quitar(SuscripcionAviso suscripcion) => bd.Suscripciones.Remove(suscripcion);
}

/// <summary>
/// Cuántas decisiones tiene pendiente cada comercial.
///
/// Es una consulta **aparte** de la del repaso y más tosca: cuenta, no redacta. Reutilizar
/// `ConsultaRepaso` habría obligado a ejecutar sus seis consultas por cada usuario de la empresa, y el
/// trabajo del viernes recorre todas las empresas. Aquí basta con un recuento aproximado: si el aviso
/// dice once y al abrir hay diez, nadie se ofende.
/// </summary>
public sealed class ConsultaPendientes(ContextoMatchketing bd, IReloj reloj) : IConsultaPendientes
{
    public async Task<IReadOnlyDictionary<Guid, int>> PorUsuarioAsync(CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(reloj.AhoraUtc.UtcDateTime);
        var ahora = reloj.AhoraUtc;

        var aparcadas = await bd.Pospuestas
            .Where(p => p.Hasta > hoy)
            .Select(p => p.Clave)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var aparcadasSet = aparcadas.ToHashSet(StringComparer.Ordinal);

        // Tareas vencidas por responsable.
        var tareas = await bd.Tareas
            .Where(t => t.Estado == EstadoTarea.Pendiente && t.VenceEl < hoy && t.ResponsableId != null)
            .Select(t => new { Usuario = t.ResponsableId!.Value, Clave = "tarea-vencida:" + t.Id })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Leads sin tocar, con las mismas exclusiones que el repaso: sin salida, sin oportunidad y sin
        // ninguna tarea. Si aquí se contara distinto, el aviso prometería un número que no existe.
        var leads = await bd.Contactos
            .Where(c => c.Activo && c.Estado == EstadoContacto.Lead && c.PropietarioId != null)
            .Where(c => !bd.Actividades.Any(a => a.ContactoId == c.Id && a.Sentido == SentidoActividad.Saliente))
            .Where(c => !bd.Oportunidades.Any(o => o.ContactoId == c.Id))
            .Where(c => !bd.Tareas.Any(t => t.ContactoId == c.Id))
            .Select(c => new { Usuario = c.PropietarioId!.Value, Clave = "lead-sin-tocar:" + c.Id })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Oportunidades abiertas con la fecha pasada o paradas más de lo que tolera su etapa.
        var oportunidades = await (
            from o in bd.Oportunidades
            where o.CerradaEn == null && o.PropietarioId != null
            join e in bd.Etapas on o.EtapaId equals e.Id
            select new { Usuario = o.PropietarioId!.Value, o.Id, o.PrevistaCierre, o.EntroEnEtapaEn, e.DiasAviso })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var deEmbudo = oportunidades
            .Select(o => o.PrevistaCierre is { } prevista && prevista < hoy
                ? new { o.Usuario, Clave = "cierre-pasado:" + o.Id }
                : (ahora - o.EntroEnEtapaEn).TotalDays > o.DiasAviso
                    ? new { o.Usuario, Clave = "oportunidad-estancada:" + o.Id }
                    : null)
            .Where(x => x is not null)
            .Select(x => x!);

        return tareas.Concat(leads).Concat(deEmbudo)
            .Where(x => !aparcadasSet.Contains(x.Clave))
            .GroupBy(x => x.Usuario)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
