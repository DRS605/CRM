using Matchketing.Avisos.Aplicacion;
using Matchketing.Avisos.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Avisos.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; set; } = ahora;
}

public sealed class ContextoDePrueba(Guid? empresaId, Guid? usuarioId) : IContextoEmpresa
{
    public Guid? EmpresaId { get; } = empresaId;

    public Guid? UsuarioId { get; } = usuarioId;

    public IReadOnlyCollection<string> Permisos => [];

    public bool Tiene(string permiso) => true;
}

public sealed class SuscripcionesEnMemoria : IRepositorioSuscripciones
{
    public List<SuscripcionAviso> Todas { get; } = [];

    public Task<SuscripcionAviso?> PorEndpointAsync(string endpoint, CancellationToken ct = default) =>
        Task.FromResult(Todas.FirstOrDefault(s => s.Endpoint == endpoint));

    public Task<IReadOnlyList<SuscripcionAviso>> DeUsuarioAsync(Guid usuarioId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SuscripcionAviso>>(Todas.Where(s => s.UsuarioId == usuarioId).ToList());

    public Task<IReadOnlyList<SuscripcionAviso>> DeLaEmpresaAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SuscripcionAviso>>(Todas.ToList());

    public void Anadir(SuscripcionAviso suscripcion) => Todas.Add(suscripcion);

    public void Quitar(SuscripcionAviso suscripcion) => Todas.Remove(suscripcion);
}

public sealed class PendientesDePrueba : IConsultaPendientes
{
    public Dictionary<Guid, int> Cuantas { get; } = [];

    public Task<IReadOnlyDictionary<Guid, int>> PorUsuarioAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(Cuantas);
}

public sealed class EmisorDePrueba : IEmisorAvisos
{
    public List<(string Endpoint, Aviso Aviso)> Enviados { get; } = [];

    public Dictionary<string, ResultadoEnvio> Respuestas { get; } = [];

    public Task<ResultadoEnvio> EnviarAsync(SuscripcionAviso suscripcion, Aviso aviso, CancellationToken ct = default)
    {
        Enviados.Add((suscripcion.Endpoint, aviso));
        return Task.FromResult(Respuestas.TryGetValue(suscripcion.Endpoint, out var r) ? r : ResultadoEnvio.Entregado);
    }
}
