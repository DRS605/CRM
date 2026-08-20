using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Organizacion.Dominio;

namespace Matchketing.Organizacion.Aplicacion;

public sealed class ServicioEmpresas(IRepositorioEmpresas empresas, IReloj reloj)
{
    public Resultado<Empresa> Crear(string? nombre, string? nif, string? provincia)
    {
        var creada = Empresa.Crear(nombre, nif, provincia, reloj);
        if (creada.Fallido)
        {
            return creada;
        }

        empresas.Anadir(creada.Valor);
        return creada;
    }

    public async Task<Resultado<Empresa>> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var empresa = await empresas.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return empresa is null
            ? Resultado.Fallo<Empresa>(Error.NoEncontrado("empresa.no_encontrada", "La empresa no existe."))
            : Resultado.Ok(empresa);
    }

    public Task<IReadOnlyList<Empresa>> DeIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
        empresas.DeIdsAsync(ids, ct);

    public async Task<Resultado> AjustarMatchAsync(Guid id, decimal pesoEncaje, int horasRebote, CancellationToken ct = default)
    {
        var empresa = await empresas.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return empresa is null
            ? Resultado.Fallo(Error.NoEncontrado("empresa.no_encontrada", "La empresa no existe."))
            : empresa.AjustarMatch(pesoEncaje, horasRebote, reloj);
    }

    public async Task<Resultado> AjustarRetencionAsync(Guid id, int mesesRetencionLeads, CancellationToken ct = default)
    {
        var empresa = await empresas.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        return empresa is null
            ? Resultado.Fallo(Error.NoEncontrado("empresa.no_encontrada", "La empresa no existe."))
            : empresa.AjustarRetencion(mesesRetencionLeads, reloj);
    }

    public async Task<Resultado> AjustarSeguimientoAsync(Guid id, bool sigueAperturas, CancellationToken ct = default)
    {
        var empresa = await empresas.BuscarPorIdAsync(id, ct).ConfigureAwait(false);
        if (empresa is null)
        {
            return Resultado.Fallo(Error.NoEncontrado("empresa.no_encontrada", "La empresa no existe."));
        }

        empresa.AjustarSeguimiento(sigueAperturas, reloj);
        return Resultado.Ok();
    }

    /// <summary>
    /// Si esta empresa mide aperturas. Lo pregunta el trabajo de envío para decidir si el correo lleva
    /// píxel o va solo en texto plano.
    /// </summary>
    public async Task<bool> SigueAperturasAsync(Guid id, CancellationToken ct = default) =>
        await empresas.BuscarPorIdAsync(id, ct).ConfigureAwait(false) is { SigueAperturas: true };
}
