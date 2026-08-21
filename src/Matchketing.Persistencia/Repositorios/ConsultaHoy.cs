using Matchketing.Contactos.Dominio;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Tareas.Aplicacion;
using Matchketing.Tareas.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>
/// Arma la pila de Hoy cruzando tareas, contactos, embudo y la puntuación Match.
///
/// **El orden lo pone el Match**: es la tesis del producto, que lo primero de la mañana lo decida
/// cuánto encaja el contacto y qué ha hecho últimamente, no la antigüedad de un recordatorio. A
/// igual Match, desempata la urgencia (lo vencido antes que lo de hoy, lo parado antes que lo que
/// solo está callado).
///
/// Un contacto **sin puntuar** va detrás de cualquiera que sí lo esté, aunque el otro puntúe bajo:
/// «no sé nada de este» no puede adelantar a «sé que este vale 30». Al principio, cuando nadie tiene
/// puntuación, todos empatan y manda la urgencia, que es el comportamiento del módulo 4.
/// </summary>
public sealed class ConsultaHoy(ContextoMatchketing bd, IReloj reloj) : IConsultaHoy
{
    public async Task<PilaHoy> PilaAsync(CancellationToken ct = default)
    {
        var hoy = HorasLaborables.DiaDeTrabajo(reloj.AhoraUtc);
        var tarjetas = new List<TarjetaHoy>();

        // 1. Lo que toca hoy o ya tocaba.
        var pendientes = await (
            from t in bd.Tareas
            where t.Estado == EstadoTarea.Pendiente && t.VenceEl <= hoy
            join c in bd.Contactos on t.ContactoId equals c.Id into cs
            from c in cs.DefaultIfEmpty()
            select new
            {
                t.Id, t.Titulo, t.ContactoId, t.OportunidadId, t.VenceEl, t.VecesAplazada,
                NombreContacto = c != null ? c.Nombre : null,
                Telefono = c != null ? c.Telefono : null,
                CuentaId = c != null ? c.CuentaId : null,
            }).ToListAsync(ct).ConfigureAwait(false);

        foreach (var t in pendientes)
        {
            var vencida = (hoy.ToDateTime(TimeOnly.MinValue) - t.VenceEl.ToDateTime(TimeOnly.MinValue)).Days;
            var motivo = vencida switch
            {
                0 => "Toca hoy.",
                1 => "Tenía que haberse hecho ayer.",
                _ => $"Lleva {vencida} días esperando.",
            };

            if (t.VecesAplazada >= 3)
            {
                motivo += $" Aplazada {t.VecesAplazada} veces.";
            }

            tarjetas.Add(new TarjetaHoy(
                TipoTarjeta.Tarea, t.Id, t.ContactoId, t.OportunidadId, t.Titulo,
                t.NombreContacto ?? "Sin contacto", null, t.Telefono, motivo, t.VenceEl, vencida, null,
                vencida > 0 ? 100 + Math.Min(vencida, 30) : 60, null, []));
        }

        // 2. Oportunidades paradas más días de los que su etapa tolera.
        var estancadas = await (
            from o in bd.Oportunidades
            where o.CerradaEn == null
            join e in bd.Etapas on o.EtapaId equals e.Id
            join c in bd.Contactos on o.ContactoId equals c.Id
            join cu in bd.Cuentas on o.CuentaId equals cu.Id into cus
            from cu in cus.DefaultIfEmpty()
            select new
            {
                o.Id, o.Titulo, o.Importe, o.ContactoId, o.EntroEnEtapaEn,
                e.DiasAviso, NombreEtapa = e.Nombre,
                NombreContacto = c.Nombre, Telefono = c.Telefono,
                NombreCuenta = cu != null ? cu.Nombre : null,
            }).ToListAsync(ct).ConfigureAwait(false);

        var ahora = reloj.AhoraUtc;
        foreach (var o in estancadas)
        {
            var dias = (int)(ahora - o.EntroEnEtapaEn).TotalDays;
            if (dias <= o.DiasAviso)
            {
                continue;
            }

            tarjetas.Add(new TarjetaHoy(
                TipoTarjeta.Estancada, null, o.ContactoId, o.Id, o.Titulo,
                o.NombreContacto, o.NombreCuenta, o.Telefono,
                $"Lleva {dias} días parada en «{o.NombreEtapa}».", null, 0, o.Importe,
                40 + Math.Min(dias - o.DiasAviso, 20), null, []));
        }

        // 3. La promesa del producto (H1): contactos vivos a los que nadie ha puesto próximo paso.
        var sinAccion = await (
            from c in bd.Contactos
            where c.Activo
                && (c.Estado == EstadoContacto.Lead || c.Estado == EstadoContacto.Cliente)
                && !bd.Tareas.Any(t => t.ContactoId == c.Id && t.Estado == EstadoTarea.Pendiente)
                && !bd.Oportunidades.Any(o => o.ContactoId == c.Id && o.CerradaEn == null)
            join cu in bd.Cuentas on c.CuentaId equals cu.Id into cus
            from cu in cus.DefaultIfEmpty()
            select new
            {
                c.Id, c.Nombre, c.Telefono, c.Estado,
                NombreCuenta = cu != null ? cu.Nombre : null,
            }).ToListAsync(ct).ConfigureAwait(false);

        foreach (var c in sinAccion)
        {
            tarjetas.Add(new TarjetaHoy(
                TipoTarjeta.SinProximaAccion, null, c.Id, null,
                c.Nombre, c.Nombre, c.NombreCuenta, c.Telefono,
                "Sin próximo paso. Un contacto sin próxima acción es un contacto que se pierde.",
                null, 0, null, 20, null, []));
        }

        // El Match de cada contacto, ya calculado y guardado. Se lee de una vez, no una por tarjeta.
        var contactosEnPila = tarjetas.Where(t => t.ContactoId is not null).Select(t => t.ContactoId!.Value).Distinct().ToList();
        var puntuaciones = await bd.Puntuaciones
            .Where(p => contactosEnPila.Contains(p.ContactoId))
            .Select(p => new { p.ContactoId, p.Match, p.Motivos })
            .ToListAsync(ct).ConfigureAwait(false);

        var porContacto = puntuaciones.ToDictionary(p => p.ContactoId, p => p);

        var conMatch = tarjetas.Select(t =>
        {
            if (t.ContactoId is not { } id || !porContacto.TryGetValue(id, out var p))
            {
                return t;
            }

            var motivos = string.IsNullOrEmpty(p.Motivos)
                ? Array.Empty<string>()
                : p.Motivos.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            return t with { Match = p.Match, Motivos = motivos };
        }).ToList();

        var ordenadas = conMatch
            .OrderByDescending(t => t.Match ?? -1)
            .ThenByDescending(t => t.Urgencia)
            .ThenByDescending(t => t.Importe ?? 0m)
            .ThenBy(t => t.NombreContacto, StringComparer.Ordinal)
            .ToList();

        // «Hoy» es el día de aquí, no el de UTC, así que se compara contra los instantes que lo limitan
        // en vez de contra una fecha: convertir zonas dentro del SQL no se puede, y comparar por fecha
        // UTC contaba como de ayer todo lo cerrado después de medianoche.
        var (desdeHoy, hastaHoy) = HorasLaborables.LimitesDelDia(ahora);
        var hechasHoy = await bd.Tareas
            .CountAsync(t => t.Estado == EstadoTarea.Hecha && t.CerradaEn != null
                && t.CerradaEn >= desdeHoy && t.CerradaEn < hastaHoy, ct)
            .ConfigureAwait(false);

        return new PilaHoy(
            ordenadas,
            ordenadas.Count,
            hechasHoy,
            ordenadas.Count(t => t.Tipo == TipoTarjeta.SinProximaAccion),
            ordenadas.Count(t => t.Tipo == TipoTarjeta.Estancada));
    }

    public async Task<IReadOnlyList<TareaVista>> ListarAsync(bool soloPendientes, CancellationToken ct = default) =>
        await (
            from t in bd.Tareas
            where !soloPendientes || t.Estado == EstadoTarea.Pendiente
            join c in bd.Contactos on t.ContactoId equals c.Id into cs
            from c in cs.DefaultIfEmpty()
            orderby t.VenceEl
            select new TareaVista(
                t.Id, t.Titulo, t.ContactoId, c != null ? c.Nombre : null,
                t.VenceEl, t.Estado, t.Origen, t.VecesAplazada))
            .ToListAsync(ct).ConfigureAwait(false);
}
