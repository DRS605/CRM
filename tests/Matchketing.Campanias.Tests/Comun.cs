using Matchketing.Campanias.Aplicacion;
using Matchketing.Campanias.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Campanias.Tests;

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

public sealed class RepositorioEnMemoria : IRepositorioCampanias
{
    public List<Segmento> Segmentos { get; } = [];

    public List<Campania> Campanias { get; } = [];

    public List<EnvioCampania> Envios { get; } = [];

    public Task<Segmento?> SegmentoAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Segmentos.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<Segmento>> SegmentosAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Segmento>>(Segmentos.ToList());

    public void Anadir(Segmento segmento) => Segmentos.Add(segmento);

    public void Quitar(Segmento segmento) => Segmentos.Remove(segmento);

    public Task<Campania?> CampaniaAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Campanias.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Campania>> CampaniasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Campania>>(Campanias.ToList());

    public Task<IReadOnlyList<Campania>> EnMarchaAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Campania>>(
            Campanias.Where(c => c.Estado == EstadoCampania.Enviando).ToList());

    public Task<int> CuantasUsanAsync(Guid segmentoId, CancellationToken ct = default) =>
        Task.FromResult(Campanias.Count(c => c.SegmentoId == segmentoId));

    public void Anadir(Campania campania) => Campanias.Add(campania);

    public void Quitar(Campania campania) => Campanias.Remove(campania);

    public void Anadir(IReadOnlyList<EnvioCampania> envios) => Envios.AddRange(envios);

    public Task<IReadOnlyList<EnvioCampania>> PendientesAsync(Guid campaniaId, int tope, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EnvioCampania>>(Envios
            .Where(e => e.CampaniaId == campaniaId && e.Estado == EstadoEnvio.Pendiente)
            .Take(tope)
            .ToList());

    public Task<IReadOnlyList<EnvioCampania>> TodosLosPendientesAsync(Guid campaniaId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EnvioCampania>>(Envios
            .Where(e => e.CampaniaId == campaniaId && e.Estado == EstadoEnvio.Pendiente)
            .ToList());

    public Task<IReadOnlyList<EnvioCampania>> ExcluidosAsync(Guid campaniaId, int tope, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EnvioCampania>>(Envios
            .Where(e => e.CampaniaId == campaniaId && e.Estado == EstadoEnvio.Excluido)
            .Take(tope)
            .ToList());
}

/// <summary>
/// El buscador de contactos. Devuelve la lista que se le ponga, y **apunta con qué criterios se le
/// preguntó**: varias pruebas comprueban que se resuelve el segmento en el momento de lanzar y no antes.
/// </summary>
public sealed class BuscadorDePrueba : IBuscaContactosDelSegmento
{
    public List<Guid> Contactos { get; set; } = [];

    public List<CriteriosSegmento> Preguntas { get; } = [];

    public string? NombreEtapa { get; set; }

    public Task<IReadOnlyList<Guid>> ResolverAsync(CriteriosSegmento criterios, int tope, CancellationToken ct = default)
    {
        Preguntas.Add(criterios);
        return Task.FromResult<IReadOnlyList<Guid>>(Contactos.Take(tope).ToList());
    }

    public Task<int> ContarAsync(CriteriosSegmento criterios, CancellationToken ct = default) =>
        Task.FromResult(Contactos.Count);

    public Task<IReadOnlyList<QuienRecibe>> MuestraAsync(CriteriosSegmento criterios, int cuantos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<QuienRecibe>>(Contactos
            .Take(cuantos)
            .Select(c => new QuienRecibe(c, "Contacto " + c.ToString()[..4], "x@y.es"))
            .ToList());

    public Task<string?> NombreDeEtapaAsync(Guid etapaId, CancellationToken ct = default) =>
        Task.FromResult(NombreEtapa);
}

public sealed class PlantillasDePrueba : IPlantillaDeCampania
{
    public DatosPlantilla? Plantilla { get; set; } =
        new(Guid.NewGuid(), "Oferta de primavera", "Una oferta para ti", true);

    public Task<DatosPlantilla?> DeAsync(Guid plantillaId, CancellationToken ct = default) =>
        Task.FromResult(Plantilla);
}

/// <summary>
/// El gancho con el módulo de correo. <see cref="Niega"/> permite simular lo que en producción es el
/// caso más frecuente y no un fallo: que esa persona no tenga consentimiento comercial.
/// </summary>
public sealed class EncoladorDePrueba : IEncolaCorreoDeCampania
{
    public List<(Guid ContactoId, Guid PlantillaId, Guid EnNombreDe)> Encolados { get; } = [];

    public Func<Guid, Error?> Niega { get; set; } = _ => null;

    public Task<Resultado<Guid>> EncolarAsync(
        Guid contactoId, Guid plantillaId, Guid enNombreDe, CancellationToken ct = default)
    {
        if (Niega(contactoId) is { } error)
        {
            return Task.FromResult(Resultado.Fallo<Guid>(error));
        }

        Encolados.Add((contactoId, plantillaId, enNombreDe));
        return Task.FromResult(Resultado.Ok(Guid.NewGuid()));
    }
}

public sealed class ContadoresDePrueba : IConsultaEnviosDeCampania
{
    public ContadoresCorreo Contadores { get; set; } = new(0, 0, 0, 0, 0);

    public Task<ContadoresCorreo> ContadoresAsync(Guid campaniaId, CancellationToken ct = default) =>
        Task.FromResult(Contadores);
}
