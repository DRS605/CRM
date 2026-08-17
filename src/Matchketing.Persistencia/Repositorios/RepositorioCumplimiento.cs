using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Cumplimiento.Dominio;
using Matchketing.Nucleo.Comun;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioConsentimientos(ContextoMatchketing bd) : IRepositorioConsentimientos
{
    public async Task<IReadOnlyList<Consentimiento>> DeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        await bd.Consentimientos
            .Where(c => c.ContactoId == contactoId)
            .OrderByDescending(c => c.OtorgadoEn)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <summary>
    /// El vigente de esa finalidad. Puede haber varios históricos —se pidió, se retiró, se volvió a
    /// pedir— y solo uno sin retirar; si por lo que sea hubiera dos, gana el más reciente.
    /// </summary>
    public Task<Consentimiento?> VigenteAsync(Guid contactoId, FinalidadConsentimiento finalidad, CancellationToken ct = default) =>
        bd.Consentimientos
            .Where(c => c.ContactoId == contactoId && c.Finalidad == finalidad && c.RetiradoEn == null)
            .OrderByDescending(c => c.OtorgadoEn)
            .FirstOrDefaultAsync(ct);

    public void Anadir(Consentimiento consentimiento) => bd.Consentimientos.Add(consentimiento);
}

public sealed class AjustesRetencion(ContextoMatchketing bd, IContextoEmpresa contexto) : IAjustesRetencion
{
    public async Task<int?> MesesRetencionAsync(CancellationToken ct = default) =>
        await bd.Empresas
            .Where(e => e.Id == contexto.EmpresaId)
            .Select(e => (int?)e.MesesRetencionLeads)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    /// <summary>
    /// Para el trabajo nocturno, que recorre todas las empresas sin ser ninguna. Funciona porque
    /// <c>publico.empresa</c> es la tabla que **define** los inquilinos, no una tabla de datos de
    /// inquilino: no lleva filtro global ni RLS, igual que <c>usuario</c> y <c>membresia</c>.
    /// </summary>
    public async Task<IReadOnlyList<(Guid EmpresaId, int Meses)>> DeTodasLasEmpresasAsync(CancellationToken ct = default)
    {
        var filas = await bd.Empresas
            .Where(e => e.Activa)
            .Select(e => new { e.Id, e.MesesRetencionLeads })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return filas.Select(f => (f.Id, f.MesesRetencionLeads)).ToList();
    }
}
