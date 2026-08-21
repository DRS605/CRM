using Matchketing.Embudo.Dominio;
using Matchketing.Informes.Aplicacion;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>
/// Los dos informes del MVP. Cruzan el embudo, sus etapas y las oportunidades cerradas; se leen de
/// una vez y se agregan en memoria, porque una pyme no tiene volumen para necesitar otra cosa y así
/// la conversión entre etapas se calcula sin cinco consultas anidadas.
/// </summary>
public sealed class ConsultaInformes(ContextoMatchketing bd) : IConsultaInformes
{
    public async Task<InformeEmbudo> EmbudoAsync(Periodo periodo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(periodo);

        var etapas = await bd.Etapas
            .Where(e => bd.Embudos.Any(x => x.Id == e.EmbudoId))
            .OrderBy(e => e.Orden)
            .Select(e => new { e.Id, e.Nombre, e.Orden, e.Probabilidad })
            .ToListAsync(ct).ConfigureAwait(false);

        var abiertas = await bd.Oportunidades
            .Where(o => o.CerradaEn == null)
            .Select(o => new { o.EtapaId, o.Importe })
            .ToListAsync(ct).ConfigureAwait(false);

        var cerradas = await EnPeriodo(bd.Oportunidades.Where(o => o.CerradaEn != null), periodo)
            .Select(o => new { o.Importe, Ganada = o.Motivo == null, o.CreadoEn, o.CerradaEn })
            .ToListAsync(ct).ConfigureAwait(false);

        var ganadas = cerradas.Where(o => o.Ganada).ToList();
        var perdidas = cerradas.Where(o => !o.Ganada).ToList();

        // Cuántas **llegaron hasta aquí**, no cuántas «estuvieron aquí». Sale del histórico real de
        // movimientos (`paso_etapa`), no de una suposición.
        //
        // La diferencia entre las dos frases era un fallo, y se veía: el tablero deja arrastrar una
        // oportunidad de «Nuevo» a «Propuesta» saltándose «Contactado», así que contando quién estuvo
        // en cada etapa había etapas de más adelante con más oportunidades que las de antes, y el
        // informe llegó a enseñar «↓ 200 % pasa a propuesta». Un embudo con una conversión por encima
        // del 100 % no es un dato raro: es un dato falso, y quien lo lee deja de creerse el informe.
        //
        // Contando el punto **más lejano** al que llegó cada oportunidad, la serie es decreciente por
        // construcción —quien llegó a la etapa 3 llegó también a la 1 y a la 2, se las saltara o no— y
        // el porcentaje no puede pasar del 100 % haga lo que haga el comercial con el ratón.
        var ordenDeEtapa = etapas.ToDictionary(e => e.Id, e => e.Orden);

        var pasos = await bd.PasosEtapa
            .Where(p => bd.Oportunidades.Any(o => o.Id == p.OportunidadId))
            .Select(p => new { p.OportunidadId, p.EtapaId })
            .ToListAsync(ct).ConfigureAwait(false);

        // Una etapa borrada del embudo deja pasos que ya no se pueden situar. No cuentan: colocarlos
        // «al principio» inventaría un recorrido que nadie hizo, y colocarlos al final inflaría el
        // final del embudo, que es justo el número que se mira.
        var masLejos = pasos
            .Where(p => ordenDeEtapa.ContainsKey(p.EtapaId))
            .GroupBy(p => p.OportunidadId)
            .Select(g => g.Max(x => ordenDeEtapa[x.EtapaId]))
            .ToList();

        var filas = new List<EtapaEmbudo>(etapas.Count);
        var llegaron = etapas.Select(e => masLejos.Count(orden => orden >= e.Orden)).ToArray();

        for (var i = 0; i < etapas.Count; i++)
        {
            var e = etapas[i];
            var suyas = abiertas.Where(o => o.EtapaId == e.Id).ToList();

            decimal? conversion = i < etapas.Count - 1 && llegaron[i] > 0
                ? decimal.Round(llegaron[i + 1] * 100m / llegaron[i], 1)
                : null;

            filas.Add(new EtapaEmbudo(
                e.Nombre, e.Orden, e.Probabilidad,
                suyas.Count, suyas.Sum(o => o.Importe),
                llegaron[i], conversion));
        }

        var prevision = etapas.Sum(e =>
            abiertas.Where(o => o.EtapaId == e.Id).Sum(o => o.Importe) * e.Probabilidad / 100m);

        var diasMedios = ganadas.Count > 0
            ? decimal.Round((decimal)ganadas.Average(o => (o.CerradaEn!.Value - o.CreadoEn).TotalDays), 1)
            : (decimal?)null;

        return new InformeEmbudo(
            periodo.Descripcion,
            filas,
            abiertas.Count,
            abiertas.Sum(o => o.Importe),
            decimal.Round(prevision, 2),
            ganadas.Count,
            ganadas.Sum(o => o.Importe),
            perdidas.Count,
            perdidas.Sum(o => o.Importe),
            cerradas.Count > 0 ? decimal.Round(ganadas.Count * 100m / cerradas.Count, 1) : null,
            ganadas.Count > 0 ? decimal.Round(ganadas.Sum(o => o.Importe) / ganadas.Count, 2) : null,
            diasMedios);
    }

    public async Task<InformeMotivos> MotivosAsync(Periodo periodo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(periodo);

        var cerradas = await EnPeriodo(bd.Oportunidades.Where(o => o.CerradaEn != null), periodo)
            .Select(o => new { o.Motivo, o.Importe })
            .ToListAsync(ct).ConfigureAwait(false);

        var perdidas = cerradas.Where(o => o.Motivo != null).ToList();
        var ganadas = cerradas.Where(o => o.Motivo == null).ToList();

        var motivos = perdidas
            .GroupBy(o => o.Motivo!.Value)
            .Select(g => new MotivoPerdidaConteo(
                Nombrar(g.Key), g.Count(), g.Sum(x => x.Importe),
                decimal.Round(g.Count() * 100m / perdidas.Count, 1)))
            .OrderByDescending(m => m.Cuantas)
            .ThenByDescending(m => m.Importe)
            .ToList();

        return new InformeMotivos(
            periodo.Descripcion, motivos,
            perdidas.Count, perdidas.Sum(o => o.Importe),
            ganadas.Count, ganadas.Sum(o => o.Importe));
    }

    private static IQueryable<Oportunidad> EnPeriodo(IQueryable<Oportunidad> consulta, Periodo periodo)
    {
        if (periodo.Desde is { } desde)
        {
            var inicio = new DateTimeOffset(desde.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(o => o.CerradaEn >= inicio);
        }

        if (periodo.Hasta is { } hasta)
        {
            // Fin de día: quien pide «hasta el 31» espera que entre lo del 31.
            var fin = new DateTimeOffset(hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(o => o.CerradaEn < fin);
        }

        return consulta;
    }

    private static string Nombrar(MotivoPerdida motivo) => motivo switch
    {
        MotivoPerdida.Precio => "Precio",
        MotivoPerdida.Plazo => "Plazo",
        MotivoPerdida.Competencia => "Competencia",
        MotivoPerdida.NoEraElMomento => "No era el momento",
        MotivoPerdida.NoContesta => "No contesta",
        _ => "Otro",
    };
}
