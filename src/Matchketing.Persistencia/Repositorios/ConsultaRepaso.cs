using Matchketing.Contactos.Dominio;
using Matchketing.Embudo.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Repaso.Aplicacion;
using Matchketing.Repaso.Dominio;
using Matchketing.Tareas.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>
/// Deriva las preguntas del repaso de lo que ya hay en la base. **Seis consultas y ninguna por
/// contacto**: si el repaso hiciera una consulta por cada ficha, con doscientos contactos tardaría más
/// en pintarse que en contestarse, y una pantalla lenta no se abre los viernes.
///
/// Cada consulta es la definición ejecutable de un tipo de pregunta. Si alguien discute qué cuenta
/// como «lead sin tocar», la respuesta está aquí y no en un documento.
/// </summary>
public sealed class ConsultaRepaso(ContextoMatchketing bd, IContextoEmpresa contexto, IReloj reloj) : IConsultaRepaso
{
    /// <summary>Días de silencio a partir de los cuales se pregunta por un contacto caliente.</summary>
    private const int DiasDeSilencio = 21;

    /// <summary>Match mínimo para que el silencio merezca una pregunta. Por debajo, sería ruido.</summary>
    private const int MatchCaliente = 65;

    /// <summary>Días tras una venta antes de sugerir el siguiente paso. Menos sería incómodo.</summary>
    private const int DiasTrasVender = 45;

    public async Task<IReadOnlyList<Hallazgo>> HallazgosAsync(CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(reloj.AhoraUtc.UtcDateTime);
        var ahora = reloj.AhoraUtc;
        var mio = contexto.UsuarioId;

        var hallazgos = new List<Hallazgo>();

        // 1. Tareas vencidas sin cerrar. Lo que rompió una promesa va primero.
        //
        //    Solo las **mías**: un repaso que me pregunta por las tareas de un compañero es un repaso
        //    que no puedo vaciar. Las que no tienen responsable también entran, porque si no son de
        //    nadie no las va a repasar nadie.
        var tareas = await bd.Tareas
            .Where(t => t.Estado == EstadoTarea.Pendiente && t.VenceEl < hoy)
            .Where(t => t.ResponsableId == null || t.ResponsableId == mio)
            .OrderBy(t => t.VenceEl)
            .Select(t => new
            {
                t.Id,
                t.Titulo,
                t.ContactoId,
                t.VenceEl,
                Nombre = bd.Contactos.Where(c => c.Id == t.ContactoId).Select(c => c.Nombre).FirstOrDefault(),
                Telefono = bd.Contactos.Where(c => c.Id == t.ContactoId).Select(c => c.Telefono).FirstOrDefault(),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        hallazgos.AddRange(tareas.Select(t => new Hallazgo(
            TipoPregunta.TareaVencida, t.Id, t.ContactoId, t.Nombre, t.Telefono,
            null, t.Id, t.Titulo, null, null, Dias(t.VenceEl, hoy), t.VenceEl)));

        // 2. Leads asignados a mí sin **ninguna** actividad saliente. Ni una llamada, ni un correo, ni
        //    una reunión. Las entrantes no cuentan: que el lead insista no es que le hayamos atendido.
        //
        //    «Sin tocar» quiere decir que **no existe absolutamente nada** sobre esa persona: ni una
        //    salida, ni una oportunidad, ni una tarea —ni pendiente, ni hecha, ni descartada—. Esto es
        //    lo que separa un repaso útil de uno que se abandona, y las dos exclusiones que faltaban
        //    salieron del test que cuenta los toques:
        //
        //    * Si le he abierto una oportunidad, es evidente que he hablado con él. Preguntármelo me
        //      dice que el sistema no se entera de nada.
        //    * Si hay una tarea suya, la tarea ya es la pregunta, unas líneas más arriba. Y si la
        //      **acabo de cerrar** en este mismo repaso, preguntarme acto seguido si he hablado con él
        //      es el mata-topos que hace que la gente cierre la pestaña: contestas una tarjeta y
        //      aparece otra sobre lo mismo.
        var leads = await bd.Contactos
            .Where(c => c.Activo && c.Estado == EstadoContacto.Lead && c.PropietarioId == mio)
            .Where(c => !bd.Actividades.Any(a => a.ContactoId == c.Id && a.Sentido == SentidoActividad.Saliente))
            .Where(c => !bd.Oportunidades.Any(o => o.ContactoId == c.Id))
            .Where(c => !bd.Tareas.Any(t => t.ContactoId == c.Id))
            .Select(c => new { c.Id, c.Nombre, c.Telefono, c.CreadoEn })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        hallazgos.AddRange(leads.Select(c => new Hallazgo(
            TipoPregunta.LeadSinTocar, c.Id, c.Id, c.Nombre, c.Telefono,
            null, null, null, null, null, (int)(ahora - c.CreadoEn).TotalDays, null)));

        // 3 y 4. Oportunidades abiertas mías: con la fecha de cierre pasada, o paradas más días de los
        //        que su etapa tolera. Una misma oportunidad puede dar las dos preguntas; se queda con
        //        la de la fecha, que es la que lleva una decisión explícita incumplida.
        var abiertas = await (
            from o in bd.Oportunidades
            where o.CerradaEn == null && (o.PropietarioId == null || o.PropietarioId == mio)
            join e in bd.Etapas on o.EtapaId equals e.Id
            select new
            {
                o.Id,
                o.Titulo,
                o.Importe,
                o.ContactoId,
                o.PrevistaCierre,
                o.EntroEnEtapaEn,
                e.DiasAviso,
                Nombre = bd.Contactos.Where(c => c.Id == o.ContactoId).Select(c => c.Nombre).FirstOrDefault(),
                Telefono = bd.Contactos.Where(c => c.Id == o.ContactoId).Select(c => c.Telefono).FirstOrDefault(),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var o in abiertas)
        {
            if (o.PrevistaCierre is { } prevista && prevista < hoy)
            {
                hallazgos.Add(new Hallazgo(
                    TipoPregunta.CierrePasado, o.Id, o.ContactoId, o.Nombre, o.Telefono,
                    o.Id, null, o.Titulo, o.Importe, null, Dias(prevista, hoy), prevista));
                continue;
            }

            var parada = (int)(ahora - o.EntroEnEtapaEn).TotalDays;
            if (parada > o.DiasAviso)
            {
                hallazgos.Add(new Hallazgo(
                    TipoPregunta.OportunidadEstancada, o.Id, o.ContactoId, o.Nombre, o.Telefono,
                    o.Id, null, o.Titulo, o.Importe, null, parada, null));
            }
        }

        // 5. Silencio caliente: Match alto y mucho tiempo sin actividad. Es el aviso que más dinero
        //    recupera, porque son contactos que ya encajan y que se han enfriado solos.
        //
        //    Se exige que **tenga** puntuación: preguntar por un contacto sin puntuar sería pedir una
        //    decisión sin darle a la persona ningún motivo, y aquí no se enseña nada sin motivo.
        var limite = ahora.AddDays(-DiasDeSilencio);
        var calientes = await (
            from c in bd.Contactos
            where c.Activo && c.Estado == EstadoContacto.Lead && c.PropietarioId == mio
            join p in bd.Puntuaciones on c.Id equals p.ContactoId
            where p.Match >= MatchCaliente
            let ultima = bd.Actividades.Where(a => a.ContactoId == c.Id).Max(a => (DateTimeOffset?)a.OcurridaEn)
            where ultima == null || ultima < limite
            where !bd.Oportunidades.Any(o => o.ContactoId == c.Id && o.CerradaEn == null)
            select new { c.Id, c.Nombre, c.Telefono, c.CreadoEn, p.Match, Ultima = ultima })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        hallazgos.AddRange(calientes.Select(c => new Hallazgo(
            TipoPregunta.SilencioCaliente, c.Id, c.Id, c.Nombre, c.Telefono,
            null, null, null, null, c.Match, (int)(ahora - (c.Ultima ?? c.CreadoEn)).TotalDays, null)));

        // 6. Clientes a los que ya se vendió y con los que no hay nada previsto. En una pyme la
        //    recomendación es el primer canal, y este es el único sitio del sistema que lo recuerda.
        var deVenta = ahora.AddDays(-DiasTrasVender);
        var clientes = await (
            from c in bd.Contactos
            where c.Activo && c.Estado == EstadoContacto.Cliente && c.PropietarioId == mio
            let gano = bd.Oportunidades
                .Where(o => o.ContactoId == c.Id && o.CerradaEn != null && o.Motivo == null)
                .Max(o => o.CerradaEn)
            where gano != null && gano < deVenta
            where !bd.Tareas.Any(t => t.ContactoId == c.Id && t.Estado == EstadoTarea.Pendiente)
            where !bd.Oportunidades.Any(o => o.ContactoId == c.Id && o.CerradaEn == null)
            select new { c.Id, c.Nombre, c.Telefono, Gano = gano })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        hallazgos.AddRange(clientes.Select(c => new Hallazgo(
            TipoPregunta.ClienteSinSiguientePaso, c.Id, c.Id, c.Nombre, c.Telefono,
            null, null, null, null, null, (int)(ahora - c.Gano!.Value).TotalDays, null)));

        return hallazgos;
    }

    /// <summary>
    /// La semana del comercial. Solo lo suyo: es un espejo, no un cuadro de mando, y por eso se filtra
    /// por autor de la actividad y por propietario de la oportunidad.
    /// </summary>
    public async Task<ResumenSemana> ResumenAsync(int dias, CancellationToken ct = default)
    {
        var mio = contexto.UsuarioId;
        var desde = reloj.AhoraUtc.AddDays(-dias);
        var anterior = reloj.AhoraUtc.AddDays(-dias * 2);

        var llamadas = await bd.Actividades
            .CountAsync(a => a.Tipo == TipoActividad.Llamada && a.AutorId == mio && a.OcurridaEn >= desde, ct)
            .ConfigureAwait(false);

        var llamadasAntes = await bd.Actividades
            .CountAsync(a => a.Tipo == TipoActividad.Llamada && a.AutorId == mio && a.OcurridaEn >= anterior && a.OcurridaEn < desde, ct)
            .ConfigureAwait(false);

        var nuevos = await bd.Contactos
            .CountAsync(c => c.PropietarioId == mio && c.CreadoEn >= desde, ct)
            .ConfigureAwait(false);

        var abiertas = await bd.Oportunidades
            .CountAsync(o => o.PropietarioId == mio && o.CreadoEn >= desde, ct)
            .ConfigureAwait(false);

        var cerradas = await bd.Oportunidades
            .Where(o => o.PropietarioId == mio && o.CerradaEn >= desde)
            .Select(o => new { o.Importe, Ganada = o.Motivo == null })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var tareas = await bd.Tareas
            .CountAsync(t => t.ResponsableId == mio && t.Estado == EstadoTarea.Hecha && t.CerradaEn >= desde, ct)
            .ConfigureAwait(false);

        var resueltas = await bd.Pospuestas
            .CountAsync(p => p.UsuarioId == mio && p.En >= desde, ct)
            .ConfigureAwait(false);

        return new ResumenSemana(
            dias, llamadas, llamadasAntes, nuevos, abiertas,
            cerradas.Count(c => c.Ganada),
            cerradas.Where(c => c.Ganada).Sum(c => c.Importe),
            cerradas.Count(c => !c.Ganada),
            tareas, resueltas);
    }

    private static int Dias(DateOnly desde, DateOnly hasta) =>
        (hasta.ToDateTime(TimeOnly.MinValue) - desde.ToDateTime(TimeOnly.MinValue)).Days;
}

public sealed class RepositorioPospuestas(ContextoMatchketing bd) : IRepositorioPospuestas
{
    public async Task<IReadOnlyCollection<string>> VigentesAsync(DateOnly hoy, CancellationToken ct = default) =>
        await bd.Pospuestas
            .Where(p => p.Hasta > hoy)
            .Select(p => p.Clave)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    public void Anadir(Pospuesta pospuesta) => bd.Pospuestas.Add(pospuesta);

    public Task<int> ResueltasDesdeAsync(DateOnly desde, CancellationToken ct = default)
    {
        var instante = new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return bd.Pospuestas.CountAsync(p => p.En >= instante, ct);
    }
}
