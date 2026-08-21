using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Objetivos.Aplicacion;
using Matchketing.Objetivos.Dominio;

namespace Matchketing.Objetivos.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; set; } = ahora;

    public void Avanzar(TimeSpan cuanto) => AhoraUtc = AhoraUtc.Add(cuanto);
}

public sealed class ContextoDePrueba(Guid? empresaId, Guid? usuarioId) : IContextoEmpresa
{
    public Guid? EmpresaId { get; } = empresaId;

    public Guid? UsuarioId { get; } = usuarioId;

    public IReadOnlyCollection<string> Permisos => [];

    public bool Tiene(string permiso) => true;
}

public sealed class RepositorioEnMemoria : IRepositorioObjetivos
{
    public List<Objetivo> Todos { get; } = [];

    public Task<Objetivo?> DeAsync(Guid usuarioId, DateOnly mes, CancellationToken ct = default) =>
        Task.FromResult(Todos.FirstOrDefault(o => o.UsuarioId == usuarioId && o.Mes == mes));

    public Task<IReadOnlyList<Objetivo>> DelMesAsync(DateOnly mes, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Objetivo>>(Todos.Where(o => o.Mes == mes).ToList());

    public Task<IReadOnlyList<Objetivo>> DePersonaAsync(Guid usuarioId, int cuantos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Objetivo>>(Todos
            .Where(o => o.UsuarioId == usuarioId)
            .OrderByDescending(o => o.Mes)
            .Take(cuantos)
            .ToList());

    public void Anadir(Objetivo objetivo) => Todos.Add(objetivo);

    public void Quitar(Objetivo objetivo) => Todos.Remove(objetivo);
}

public sealed class LogradoDePrueba : IConsultaLogrado
{
    public Dictionary<(Guid Usuario, DateOnly Mes), decimal> Ganado { get; } = [];

    public Task<IReadOnlyDictionary<Guid, decimal>> GanadoPorPersonaAsync(DateOnly mes, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(Ganado
            .Where(p => p.Key.Mes == mes)
            .ToDictionary(p => p.Key.Usuario, p => p.Value));

    public Task<decimal> GanadoDeAsync(Guid usuarioId, DateOnly mes, CancellationToken ct = default) =>
        Task.FromResult(Ganado.TryGetValue((usuarioId, mes), out var cuanto) ? cuanto : 0m);
}

public sealed class EquipoDePrueba : IConsultaEquipoObjetivos
{
    public List<QuienVende> Gente { get; } = [];

    public Task<IReadOnlyList<QuienVende>> ActivosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<QuienVende>>(Gente.ToList());
}
