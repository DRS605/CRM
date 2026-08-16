using Matchketing.Captacion.Aplicacion;
using Matchketing.Captacion.Dominio;
using Microsoft.EntityFrameworkCore;

namespace Matchketing.Persistencia.Repositorios;

public sealed class RepositorioFormularios(ContextoMatchketing bd) : IRepositorioFormularios
{
    public Task<Formulario?> BuscarPorIdAsync(Guid id, CancellationToken ct = default) =>
        bd.Formularios.FirstOrDefaultAsync(f => f.Id == id, ct);

    /// <summary>
    /// Salta el filtro global de empresa a propósito: la petición viene de la web de un visitante
    /// que no está autenticado, así que no hay empresa activa. **La clave es la que dice de qué
    /// empresa es**, y por eso es aleatoria de 22 caracteres y única.
    /// </summary>
    public Task<Formulario?> BuscarPorClaveAsync(string clave, CancellationToken ct = default) =>
        bd.Formularios.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Clave == clave && f.Activo, ct);

    public async Task<IReadOnlyList<Formulario>> ActivosAsync(CancellationToken ct = default) =>
        await bd.Formularios.Where(f => f.Activo).OrderBy(f => f.Nombre).ToListAsync(ct).ConfigureAwait(false);

    public void Anadir(Formulario formulario) => bd.Formularios.Add(formulario);
}

public sealed class RepositorioEnvios(ContextoMatchketing bd) : IRepositorioEnvios
{
    public void Anadir(EnvioFormulario envio) => bd.Envios.Add(envio);

    public Task<int> ContarDeFormularioAsync(Guid formularioId, CancellationToken ct = default) =>
        bd.Envios.IgnoreQueryFilters().CountAsync(e => e.FormularioId == formularioId, ct);
}
