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

        // «Han pasado» sale del histórico real de movimientos (`paso_etapa`), no de una suposición:
        // cuántas oportunidades **estuvieron** en esa etapa, hayan seguido adelante o se hayan
        // caído allí. Es lo único con lo que un porcentaje de conversión significa algo.
        var pasosPorEtapa = await bd.PasosEtapa
            .Where(p => bd.Oportunidades.Any(o => o.Id == p.OportunidadId))
            .GroupBy(p => p.EtapaId)
            .Select(g => new { EtapaId = g.Key, Cuantas = g.Select(x => x.OportunidadId).Distinct().Count() })
            .ToListAsync(ct).ConfigureAwait(false);

        var filas = new List<EtapaEmbudo>(etapas.Count);
        var pasadas = etapas
            .Select(e => pasosPorEtapa.FirstOrDefault(p => p.EtapaId == e.Id)?.Cuantas ?? 0)
            .ToArray();

        for (var i = 0; i < etapas.Count; i++)
        {
            var e = etapas[i];
            var suyas = abiertas.Where(o => o.EtapaId == e.Id).ToList();

            decimal? conversion = i < etapas.Count - 1 && pasadas[i] > 0
                ? decimal.Round(pasadas[i + 1] * 100m / pasadas[i], 1)
                : null;

            filas.Add(new EtapaEmbudo(
                e.Nombre, e.Orden, e.Probabilidad,
                suyas.Count, suyas.Sum(o => o.Importe),
                pasadas[i], conversion));
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
