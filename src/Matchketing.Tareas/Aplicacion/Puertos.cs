using Matchketing.Tareas.Dominio;

namespace Matchketing.Tareas.Aplicacion;

public interface IRepositorioTareas
{
    Task<Tarea?> BuscarPorIdAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Tarea>> PendientesDeContactoAsync(Guid contactoId, CancellationToken ct = default);

    void Anadir(Tarea tarea);
}

/// <summary>
/// Arma la pila de Hoy. Vive en persistencia porque cruza tareas, contactos y el embudo, y ninguno
/// de los tres debe conocer a los otros.
/// </summary>
public interface IConsultaHoy
{
    Task<PilaHoy> PilaAsync(CancellationToken ct = default);

    Task<IReadOnlyList<TareaVista>> ListarAsync(bool soloPendientes, CancellationToken ct = default);
}
