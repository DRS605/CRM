namespace Matchketing.Informes.Aplicacion;

/// <summary>
/// Los dos informes del MVP. Viven en persistencia porque cruzan el embudo con los contactos, y
/// ninguno de los dos módulos debe conocer al otro.
/// </summary>
public interface IConsultaInformes
{
    Task<InformeEmbudo> EmbudoAsync(Periodo periodo, CancellationToken ct = default);

    Task<InformeMotivos> MotivosAsync(Periodo periodo, CancellationToken ct = default);
}
