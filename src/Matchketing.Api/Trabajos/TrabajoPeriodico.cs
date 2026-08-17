using Matchketing.Nucleo.Comun;
using Matchketing.Persistencia;

namespace Matchketing.Api.Trabajos;

/// <summary>
/// Andamio de los trabajos que se ejecutan solos. Hace las tres cosas que todos necesitan y que son
/// fáciles de hacer mal:
///
/// 1. **Un ámbito nuevo por empresa**, con su propio <see cref="ContextoMatchketing"/>. Reutilizar
///    uno entre empresas mezclaría entidades rastreadas de dos inquilinos en el mismo contexto: la
///    peor fuga imaginable, y silenciosa.
/// 2. **Fijar el inquilino** con <see cref="IContextoEmpresaPublico"/> y reaplicarlo en la conexión.
///    Sin empresa activa, el filtro global de EF falla cerrado y el trabajo no vería ni una fila; se
///    quedaría tan callado como si no hubiera nada que hacer.
/// 3. **Aislar los fallos**: si una empresa revienta, las demás siguen. Un trabajo nocturno que se
///    cae con la primera empresa problemática deja de funcionar sin que nadie se entere.
/// </summary>
public abstract class TrabajoPeriodico(IServiceProvider servicios, ILogger logger) : BackgroundService
{
    /// <summary>Nombre para los mensajes de registro. En castellano, como todo lo demás.</summary>
    protected abstract string Nombre { get; }

    /// <summary>Cada cuánto se repite.</summary>
    protected abstract TimeSpan Cada { get; }

    /// <summary>Cuánto se espera antes de la primera pasada, para no competir con el arranque.</summary>
    protected virtual TimeSpan Espera => TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(Espera, ct).ConfigureAwait(false);

            using var reloj = new PeriodicTimer(Cada);
            do
            {
                await PasadaAsync(ct).ConfigureAwait(false);
            }
            while (await reloj.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // La aplicación se está apagando. No es un fallo.
        }
    }

    /// <summary>Lo que hay que hacer para una empresa. El ámbito y el inquilino ya están puestos.</summary>
    protected abstract Task<string?> ParaEmpresaAsync(IServiceProvider ambito, Guid empresaId, CancellationToken ct);

    /// <summary>Las empresas sobre las que trabajar. Por defecto, todas las activas.</summary>
    protected virtual async Task<IReadOnlyList<Guid>> EmpresasAsync(IServiceProvider ambito, CancellationToken ct)
    {
        var ajustes = ambito.GetRequiredService<Cumplimiento.Aplicacion.IAjustesRetencion>();
        var todas = await ajustes.DeTodasLasEmpresasAsync(ct).ConfigureAwait(false);
        return todas.Select(e => e.EmpresaId).ToList();
    }

    private async Task PasadaAsync(CancellationToken ct)
    {
        IReadOnlyList<Guid> empresas;
        using (var ambito = servicios.CreateScope())
        {
            try
            {
                empresas = await EmpresasAsync(ambito.ServiceProvider, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "{Trabajo}: no se ha podido listar las empresas.", Nombre);
                return;
            }
        }

        foreach (var empresaId in empresas)
        {
            ct.ThrowIfCancellationRequested();

            using var ambito = servicios.CreateScope();
            try
            {
                ambito.ServiceProvider.GetRequiredService<IContextoEmpresaPublico>().FijarEmpresa(empresaId);
                var bd = ambito.ServiceProvider.GetRequiredService<ContextoMatchketing>();
                await bd.ReaplicarEmpresaAsync(ct).ConfigureAwait(false);

                var resumen = await ParaEmpresaAsync(ambito.ServiceProvider, empresaId, ct).ConfigureAwait(false);
                if (resumen is not null)
                {
                    logger.LogInformation("{Trabajo} [{Empresa}]: {Resumen}", Nombre, empresaId, resumen);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "{Trabajo} [{Empresa}]: ha fallado. Se sigue con las demás.", Nombre, empresaId);
            }
        }
    }
}
