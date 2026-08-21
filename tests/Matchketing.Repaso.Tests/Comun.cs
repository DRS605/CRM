using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Repaso.Aplicacion;
using Matchketing.Repaso.Dominio;

namespace Matchketing.Repaso.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; set; } = ahora;
}

public sealed class ContextoDePrueba(Guid? empresaId, Guid? usuarioId = null) : IContextoEmpresa
{
    public Guid? EmpresaId { get; } = empresaId;

    public Guid? UsuarioId { get; } = usuarioId;

    public IReadOnlyCollection<string> Permisos => [];

    public bool Tiene(string permiso) => true;
}

/// <summary>Los hallazgos se ponen a mano: qué consulta los saca se prueba contra PostgreSQL.</summary>
public sealed class ConsultaDePrueba : IConsultaRepaso
{
    public List<Hallazgo> Hallazgos { get; } = [];

    public ResumenSemana Resumen { get; set; } = new(7, 0, 0, 0, 0, 0, 0m, 0, 0, 0);

    public Task<IReadOnlyList<Hallazgo>> HallazgosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Hallazgo>>(Hallazgos.ToList());

    public Task<ResumenSemana> ResumenAsync(int dias, CancellationToken ct = default) =>
        Task.FromResult(Resumen with { Dias = dias });
}

public sealed class PospuestasEnMemoria : IRepositorioPospuestas
{
    public List<Pospuesta> Todas { get; } = [];

    public Task<IReadOnlyCollection<string>> VigentesAsync(DateOnly hoy, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyCollection<string>>(
            Todas.Where(p => p.Hasta > hoy).Select(p => p.Clave).ToHashSet(StringComparer.Ordinal));

    public void Anadir(Pospuesta pospuesta) => Todas.Add(pospuesta);

    public Task<int> ResueltasDesdeAsync(DateOnly desde, CancellationToken ct = default) =>
        Task.FromResult(Todas.Count);
}

/// <summary>
/// Apunta qué le han pedido hacer, para poder comprobar que cada respuesta hace **exactamente** lo
/// que dice y nada más. Es donde se ve si «no le interesa» además descarta el contacto.
/// </summary>
public sealed class AccionesDePrueba : IAccionesRepaso
{
    public List<string> Hechas { get; } = [];

    public bool Falla { get; set; }

    public decimal ImporteGanado { get; set; } = 8400m;

    public Task<bool> CompletarTareaAsync(Guid tareaId, CancellationToken ct = default) => Apuntar($"completar:{tareaId}");

    public Task<bool> AplazarTareaAsync(Guid tareaId, DateOnly nuevaFecha, CancellationToken ct = default) =>
        Apuntar($"aplazar:{tareaId}:{nuevaFecha:yyyy-MM-dd}");

    public Task<bool> DescartarTareaAsync(Guid tareaId, CancellationToken ct = default) => Apuntar($"descartar-tarea:{tareaId}");

    public Task<bool> RegistrarLlamadaAsync(Guid contactoId, ResultadoDeLlamada resultado, CancellationToken ct = default) =>
        Apuntar($"llamada:{contactoId}:{resultado}");

    public Task<bool> DescartarContactoAsync(Guid contactoId, CancellationToken ct = default) =>
        Apuntar($"descartar-contacto:{contactoId}");

    /// <summary>Los títulos de las tareas creadas. El apunte no los lleva, y hay pruebas que los miran.</summary>
    public List<string> Titulos { get; } = [];

    public Task<bool> CrearTareaAsync(Guid contactoId, string titulo, DateOnly venceEl, CancellationToken ct = default)
    {
        Titulos.Add(titulo);
        return Apuntar($"tarea:{contactoId}:{venceEl:yyyy-MM-dd}");
    }

    public Task<bool> RegistrarRespuestaAsync(Guid contactoId, CancellationToken ct = default) =>
        Apuntar($"respuesta:{contactoId}");

    public Task<decimal?> GanarOportunidadAsync(Guid oportunidadId, CancellationToken ct = default)
    {
        Hechas.Add($"ganar:{oportunidadId}");
        return Task.FromResult(Falla ? null : (decimal?)ImporteGanado);
    }

    public Task<bool> PerderOportunidadAsync(Guid oportunidadId, int motivo, CancellationToken ct = default) =>
        Apuntar($"perder:{oportunidadId}:{motivo}");

    public Task<bool> MoverCierreAsync(Guid oportunidadId, DateOnly nuevaFecha, CancellationToken ct = default) =>
        Apuntar($"mover-cierre:{oportunidadId}:{nuevaFecha:yyyy-MM-dd}");

    private Task<bool> Apuntar(string que)
    {
        Hechas.Add(que);
        return Task.FromResult(!Falla);
    }
}
