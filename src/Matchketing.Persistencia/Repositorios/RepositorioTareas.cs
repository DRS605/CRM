using Matchketing.Tareas.Aplicacion;
using Matchketing.Tareas.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioTareas(ContextoMatchketing bd) : IRepositorioTareas
{
    public Task<Tarea?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Tareas.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<Tarea>> PendientesDeContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        await bd.Tareas
            .Where(t => t.ContactoId == contactoId && t.Estado == EstadoTarea.Pendiente)
            .ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(Tarea tarea) => bd.Tareas.Add(tarea);
}
