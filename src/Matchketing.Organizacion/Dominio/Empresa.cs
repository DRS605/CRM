using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Organizacion.Dominio;

/// <summary>Se ha creado una empresa.</summary>
public sealed record EmpresaCreada(Guid EmpresaId, string Nombre, DateTimeOffset OcurridoEn) : IEventoDominio;

/// <summary>
/// Empresa: el inquilino (tenant). Todo dato de negocio del sistema cuelga de una, y el aislamiento
/// entre empresas se garantiza con filtro global de EF Core y RLS de PostgreSQL.
/// </summary>
public sealed class Empresa : RaizAgregado<Guid>
{
    public const int LongitudMaximaNombre = 160;

    /// <summary>Peso del Encaje frente al Momento en la puntuación Match. Por defecto, mitad y mitad.</summary>
    public const decimal PesoEncajePorDefecto = 0.5m;

    /// <summary>
    /// Meses que se conserva un lead que nunca llegó a nada. Dos años: bastante para que una
    /// oportunidad lenta madure, poco para acabar con una base de datos de gente que preguntó un
    /// precio hace media década y ya no se acuerda de haberlo hecho.
    /// </summary>
    public const int MesesRetencionPorDefecto = 24;

    private Empresa(Guid id)
        : base(id)
    {
        Nombre = null!;
    }

    private Empresa(Guid id, string nombre, string? nif, string? provincia, DateTimeOffset ahora)
        : base(id)
    {
        Nombre = nombre;
        Nif = nif;
        Provincia = provincia;
        PesoEncaje = PesoEncajePorDefecto;
        HorasRebote = 4;
        MesesRetencionLeads = MesesRetencionPorDefecto;
        Activa = true;
        CreadoEn = ahora;
        ActualizadoEn = ahora;
    }

    public string Nombre { get; private set; }

    public string? Nif { get; private set; }

    /// <summary>Provincia principal. Se usa como valor por defecto al repartir leads por zona.</summary>
    public string? Provincia { get; private set; }

    /// <summary>Peso del Encaje en el Match (0–1). El Momento pesa el resto.</summary>
    public decimal PesoEncaje { get; private set; }

    /// <summary>Horas laborables sin primera acción antes de que un lead rebote a otro comercial.</summary>
    public int HorasRebote { get; private set; }

    /// <summary>Meses tras los que se borra un lead que sigue siendo lead y nadie ha tocado.</summary>
    public int MesesRetencionLeads { get; private set; }

    /// <summary>
    /// Si se mide quién abre los correos. **Apagado por defecto, y eso es una decisión.**
    ///
    /// Saber si alguien ha abierto tu correo es medir su comportamiento, no gestionar un dato que te dio.
    /// Que sea una decisión explícita de la empresa —y no algo que ya está puesto cuando se abre la
    /// cuenta— es la diferencia entre una herramienta que se puede defender delante de un cliente y una
    /// que hay que explicar. Con esto apagado, los correos salen **solo en texto plano**: sin parte HTML,
    /// sin imagen y sin nada que cargar.
    /// </summary>
    public bool SigueAperturas { get; private set; }

    public bool Activa { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    public static Resultado<Empresa> Crear(string? nombre, string? nif, string? provincia, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var error = ValidarNombre(nombre);
        if (error is not null)
        {
            return Resultado.Fallo<Empresa>(error);
        }

        var empresa = new Empresa(Guid.NewGuid(), nombre!.Trim(), Normalizar(nif), Normalizar(provincia), reloj.AhoraUtc);
        empresa.RegistrarEvento(new EmpresaCreada(empresa.Id, empresa.Nombre, reloj.AhoraUtc));
        return Resultado.Ok(empresa);
    }

    public Resultado Actualizar(string? nombre, string? nif, string? provincia, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        var error = ValidarNombre(nombre);
        if (error is not null)
        {
            return Resultado.Fallo(error);
        }

        Nombre = nombre!.Trim();
        Nif = Normalizar(nif);
        Provincia = Normalizar(provincia);
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>Ajustes del motor Match. Opción avanzada: en la interfaz va plegada.</summary>
    public Resultado AjustarMatch(decimal pesoEncaje, int horasRebote, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (pesoEncaje is < 0m or > 1m)
        {
            return Resultado.Fallo(Error.Validacion("empresa.peso_invalido", "El peso del encaje debe estar entre 0 y 1."));
        }

        if (horasRebote is < 1 or > 240)
        {
            return Resultado.Fallo(Error.Validacion("empresa.rebote_invalido", "Las horas de rebote deben estar entre 1 y 240."));
        }

        PesoEncaje = pesoEncaje;
        HorasRebote = horasRebote;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>
    /// Plazo de conservación de leads. El mínimo son 3 meses: por debajo de eso el sistema borraría
    /// leads que todavía se están trabajando, y un CRM que se come los leads no es un CRM. El máximo
    /// son 10 años, el plazo mercantil más largo que puede justificar conservarlos.
    /// </summary>
    public Resultado AjustarRetencion(int mesesRetencionLeads, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (mesesRetencionLeads is < 3 or > 120)
        {
            return Resultado.Fallo(Error.Validacion(
                "empresa.retencion_invalida", "El plazo de conservación de leads debe estar entre 3 y 120 meses."));
        }

        MesesRetencionLeads = mesesRetencionLeads;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>
    /// Enciende o apaga la medición de aperturas. No devuelve `Resultado` porque no hay nada que
    /// validar: es un sí o un no, y los dos son válidos.
    /// </summary>
    public void AjustarSeguimiento(bool sigueAperturas, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        SigueAperturas = sigueAperturas;
        ActualizadoEn = reloj.AhoraUtc;
    }

    private static Error? ValidarNombre(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            return Error.Validacion("empresa.nombre_vacio", "El nombre de la empresa es obligatorio.");
        }

        return nombre.Trim().Length > LongitudMaximaNombre
            ? Error.Validacion("empresa.nombre_largo", "El nombre de la empresa es demasiado largo.")
            : null;
    }

    private static string? Normalizar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
