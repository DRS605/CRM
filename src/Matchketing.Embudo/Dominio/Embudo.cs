using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Embudo.Dominio;

/// <summary>
/// Una etapa del embudo. La <see cref="Probabilidad"/> vive aquí y no en la oportunidad: así no se
/// puede tocar a mano oportunidad por oportunidad (invariante O4) y la previsión sigue significando
/// algo.
/// </summary>
public sealed class Etapa : EntidadBase<Guid>
{
    public const int LongitudMaximaNombre = 60;

    private Etapa(Guid id)
        : base(id) => Nombre = null!;

    internal Etapa(Guid id, Guid embudoId, string nombre, int orden, int probabilidad, int diasAviso)
        : base(id)
    {
        EmbudoId = embudoId;
        Nombre = nombre;
        Orden = orden;
        Probabilidad = probabilidad;
        DiasAviso = diasAviso;
    }

    public Guid EmbudoId { get; private set; }

    public string Nombre { get; private set; }

    public int Orden { get; private set; }

    /// <summary>Probabilidad de cierre de esta etapa, 0–100. Base de la previsión ponderada.</summary>
    public int Probabilidad { get; private set; }

    /// <summary>Días en esta etapa antes de avisar de que la oportunidad está parada.</summary>
    public int DiasAviso { get; private set; }
}

/// <summary>
/// Un embudo con sus etapas ordenadas. Cada empresa nace con uno por defecto de cinco etapas: se
/// pueden crear más, pero no es lo primero que pide nadie.
/// </summary>
public sealed class Embudo : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaNombre = 80;

    private readonly List<Etapa> etapas = [];

    private Embudo(Guid id)
        : base(id, Guid.Empty) => Nombre = null!;

    private Embudo(Guid id, Guid empresaId, string nombre, bool porDefecto, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Nombre = nombre;
        PorDefecto = porDefecto;
        CreadoEn = ahora;
    }

    public string Nombre { get; private set; }

    public bool PorDefecto { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public IReadOnlyList<Etapa> Etapas => etapas.OrderBy(e => e.Orden).ToList();

    /// <summary>
    /// El embudo que se crea junto con la empresa. Cinco etapas, que es lo que cabe en la cabeza de
    /// un comercial sin mirar un manual.
    /// </summary>
    public static Embudo CrearPorDefecto(Guid empresaId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var embudo = new Embudo(Guid.NewGuid(), empresaId, "Embudo comercial", true, reloj.AhoraUtc);

        // (nombre, probabilidad, días antes de avisar de estancamiento)
        var plantilla = new (string Nombre, int Probabilidad, int Dias)[]
        {
            ("Nuevo", 10, 3),
            ("Contactado", 25, 7),
            ("Propuesta", 50, 10),
            ("Negociación", 75, 14),
            ("Cierre", 90, 7),
        };

        for (var i = 0; i < plantilla.Length; i++)
        {
            var (nombre, probabilidad, dias) = plantilla[i];
            embudo.etapas.Add(new Etapa(Guid.NewGuid(), embudo.Id, nombre, i + 1, probabilidad, dias));
        }

        return embudo;
    }

    public static Resultado<Embudo> Crear(Guid empresaId, string? nombre, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Resultado.Fallo<Embudo>(Error.Validacion("embudo.nombre_vacio", "El nombre del embudo es obligatorio."));
        }

        if (nombre.Trim().Length > LongitudMaximaNombre)
        {
            return Resultado.Fallo<Embudo>(Error.Validacion("embudo.nombre_largo", "El nombre del embudo es demasiado largo."));
        }

        return Resultado.Ok(new Embudo(Guid.NewGuid(), empresaId, nombre.Trim(), false, reloj.AhoraUtc));
    }

    public Resultado<Etapa> AnadirEtapa(string? nombre, int probabilidad, int diasAviso)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Resultado.Fallo<Etapa>(Error.Validacion("etapa.nombre_vacio", "El nombre de la etapa es obligatorio."));
        }

        if (nombre.Trim().Length > Etapa.LongitudMaximaNombre)
        {
            return Resultado.Fallo<Etapa>(Error.Validacion("etapa.nombre_largo", "El nombre de la etapa es demasiado largo."));
        }

        if (probabilidad is < 0 or > 100)
        {
            return Resultado.Fallo<Etapa>(Error.Validacion("etapa.probabilidad_invalida", "La probabilidad debe estar entre 0 y 100."));
        }

        if (diasAviso is < 1 or > 365)
        {
            return Resultado.Fallo<Etapa>(Error.Validacion("etapa.dias_invalidos", "Los días de aviso deben estar entre 1 y 365."));
        }

        var etapa = new Etapa(Guid.NewGuid(), Id, nombre.Trim(), etapas.Count + 1, probabilidad, diasAviso);
        etapas.Add(etapa);
        return Resultado.Ok(etapa);
    }

    public Etapa? Primera() => etapas.OrderBy(e => e.Orden).FirstOrDefault();

    public Etapa? EtapaCon(Guid etapaId) => etapas.Find(e => e.Id == etapaId);
}
