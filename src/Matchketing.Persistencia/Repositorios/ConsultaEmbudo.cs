using Matchketing.Embudo.Aplicacion;
using Matchketing.Embudo.Dominio;
using Matchketing.Nucleo.Tiempo;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

/// <summary>
/// Lecturas del tablero. Vive en persistencia porque cruza dos módulos —el embudo y los contactos—
/// y ninguno de los dos debe conocer al otro.
/// </summary>
public sealed class ConsultaEmbudo(ContextoMatchketing bd, IReloj reloj) : IConsultaEmbudo
{
    public async Task<Tablero?> TableroAsync(Guid? embudoId, CancellationToken ct = default)
    {
        var embudo = await bd.Embudos
            .Include(e => e.Etapas)
            .Where(e => embudoId == null || e.Id == embudoId)
            .OrderByDescending(e => e.PorDefecto)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (embudo is null)
        {
            return null;
        }

        var abiertas = await (
            from o in bd.Oportunidades
            where o.EmbudoId == embudo.Id && o.CerradaEn == null
            join c in bd.Contactos on o.ContactoId equals c.Id
            join cu in bd.Cuentas on o.CuentaId equals cu.Id into cuentas
            from cu in cuentas.DefaultIfEmpty()
            orderby o.Importe descending
            select new
            {
                o.Id, o.Titulo, o.Importe, o.ContactoId,
                NombreContacto = c.Nombre,
                NombreCuenta = cu != null ? cu.Nombre : null,
                o.EtapaId, o.PrevistaCierre, o.EntroEnEtapaEn,
            }).ToListAsync(ct).ConfigureAwait(false);

        var ahora = reloj.AhoraUtc;
        var columnas = new List<ColumnaEmbudo>();
        var estancadas = 0;
        decimal prevision = 0m;

        foreach (var etapa in embudo.Etapas)
        {
            var suyas = abiertas.Where(o => o.EtapaId == etapa.Id).ToList();

            var vistas = suyas.Select(o =>
            {
                var dias = (int)(ahora - o.EntroEnEtapaEn).TotalDays;
                var parada = dias > etapa.DiasAviso;
                if (parada)
                {
                    estancadas++;
                }

                return new OportunidadVista(
                    o.Id, o.Titulo, o.Importe, o.ContactoId, o.NombreContacto, o.NombreCuenta,
                    o.EtapaId, EstadoOportunidad.Abierta, o.PrevistaCierre, dias, parada, null);
            }).ToList();

            var importe = suyas.Sum(o => o.Importe);
            prevision += importe * etapa.Probabilidad / 100m;

            columnas.Add(new ColumnaEmbudo(etapa.Id, etapa.Nombre, etapa.Orden, etapa.Probabilidad, suyas.Count, importe, vistas));
        }

        return new Tablero(
            embudo.Id, embudo.Nombre, columnas,
            abiertas.Count, abiertas.Sum(o => o.Importe), decimal.Round(prevision, 2), estancadas);
    }

    public async Task<InformeMotivos> MotivosAsync(CancellationToken ct = default)
    {
        var cerradas = await bd.Oportunidades
            .Where(o => o.CerradaEn != null)
            .Select(o => new { o.Motivo, o.Importe })
            .ToListAsync(ct).ConfigureAwait(false);

        var perdidas = cerradas.Where(o => o.Motivo != null).ToList();
        var ganadas = cerradas.Where(o => o.Motivo == null).ToList();

        var motivos = perdidas
            .GroupBy(o => o.Motivo!.Value)
            .Select(g => new MotivoConteo(g.Key, g.Count(), g.Sum(x => x.Importe)))
            .OrderByDescending(m => m.Cuantas)
            .ThenByDescending(m => m.Importe)
            .ToList();

        return new InformeMotivos(
            motivos, perdidas.Count, perdidas.Sum(o => o.Importe), ganadas.Count, ganadas.Sum(o => o.Importe));
    }
}
