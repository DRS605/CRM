using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Tareas.Dominio;

public enum EstadoTarea
{
    Pendiente = 1,
    Hecha = 2,

    /// <summary>Se decidió que no hacía falta. Se guarda igual: enseña tanto como hacerla.</summary>
    Descartada = 3,
}

public enum OrigenTarea
{
    Manual = 1,

    /// <summary>La creó el sistema: una llamada que pedía volver a llamar, un lead nuevo…</summary>
    Automatica = 2,
}

public sealed record TareaCreada(Guid TareaId, Guid EmpresaId, Guid? ContactoId, DateTimeOffset OcurridoEn) : IEventoDominio;

public sealed record TareaCompletada(Guid TareaId, Guid EmpresaId, Guid? ContactoId, DateTimeOffset OcurridoEn) : IEventoDominio;

/// <summary>
/// Algo que hay que hacer, con fecha. Es la unidad de trabajo de la pantalla Hoy: sin tareas, Hoy
/// no tiene nada que enseñar.
/// </summary>
public sealed class Tarea : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaTitulo = 160;

    private Tarea(Guid id)
        : base(id, Guid.Empty) => Titulo = null!;

    private Tarea(Guid id, Guid empresaId, string titulo, Guid? contactoId, Guid? oportunidadId, DateOnly venceEl, Guid? responsableId, OrigenTarea origen, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        Titulo = titulo;
        ContactoId = contactoId;
        OportunidadId = oportunidadId;
        VenceEl = venceEl;
        ResponsableId = responsableId;
        Origen = origen;
        Estado = EstadoTarea.Pendiente;
        CreadoEn = ahora;
        ActualizadoEn = ahora;
    }

    public string Titulo { get; private set; }

    public Guid? ContactoId { get; private set; }

    public Guid? OportunidadId { get; private set; }

    /// <summary>Día en que toca. Sin hora: nadie planifica su semana al minuto.</summary>
    public DateOnly VenceEl { get; private set; }

    public Guid? ResponsableId { get; private set; }

    public OrigenTarea Origen { get; private set; }

    public EstadoTarea Estado { get; private set; }

    /// <summary>Cuántas veces se ha aplazado. Aplazar cinco veces es una señal, no un accidente.</summary>
    public int VecesAplazada { get; private set; }

    public DateTimeOffset? CerradaEn { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    public static Resultado<Tarea> Crear(
        Guid empresaId, string? titulo, Guid? contactoId, Guid? oportunidadId,
        DateOnly? venceEl, Guid? responsableId, IReloj reloj, OrigenTarea origen = OrigenTarea.Manual)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (string.IsNullOrWhiteSpace(titulo))
        {
            return Resultado.Fallo<Tarea>(Error.Validacion("tarea.titulo_vacio", "La tarea necesita un título."));
        }

        if (titulo.Trim().Length > LongitudMaximaTitulo)
        {
            return Resultado.Fallo<Tarea>(Error.Validacion("tarea.titulo_largo", "El título de la tarea es demasiado largo."));
        }

        // Sin fecha, hoy: una tarea sin día es una tarea que no se hace nunca.
        var fecha = venceEl ?? DateOnly.FromDateTime(reloj.AhoraUtc.UtcDateTime);

        var tarea = new Tarea(Guid.NewGuid(), empresaId, titulo.Trim(), contactoId, oportunidadId, fecha, responsableId, origen, reloj.AhoraUtc);
        tarea.RegistrarEvento(new TareaCreada(tarea.Id, empresaId, contactoId, reloj.AhoraUtc));
        return Resultado.Ok(tarea);
    }

    public Resultado Completar(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Estado != EstadoTarea.Pendiente)
        {
            return Resultado.Fallo(Error.Conflicto("tarea.ya_cerrada", "Esta tarea ya estaba cerrada."));
        }

        Estado = EstadoTarea.Hecha;
        CerradaEn = reloj.AhoraUtc;
        ActualizadoEn = reloj.AhoraUtc;
        RegistrarEvento(new TareaCompletada(Id, EmpresaId, ContactoId, reloj.AhoraUtc));
        return Resultado.Ok();
    }

    public Resultado Descartar(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Estado != EstadoTarea.Pendiente)
        {
            return Resultado.Fallo(Error.Conflicto("tarea.ya_cerrada", "Esta tarea ya estaba cerrada."));
        }

        Estado = EstadoTarea.Descartada;
        CerradaEn = reloj.AhoraUtc;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>
    /// H2: aplazar **exige fecha**, y tiene que ser posterior a la que ya tenía. No existe
    /// «aplazar indefinidamente»: eso es descartar, y descartar tiene su propio botón.
    /// </summary>
    public Resultado Aplazar(DateOnly? hasta, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Estado != EstadoTarea.Pendiente)
        {
            return Resultado.Fallo(Error.Conflicto("tarea.ya_cerrada", "Una tarea cerrada no se aplaza."));
        }

        if (hasta is not { } fecha)
        {
            return Resultado.Fallo(Error.Validacion("tarea.aplazar_sin_fecha", "Para aplazar hay que decir hasta cuándo."));
        }

        var hoy = DateOnly.FromDateTime(reloj.AhoraUtc.UtcDateTime);
        if (fecha <= hoy)
        {
            return Resultado.Fallo(Error.Validacion("tarea.aplazar_al_pasado", "Aplazar es para más adelante: elige un día posterior a hoy."));
        }

        VenceEl = fecha;
        VecesAplazada++;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    public Resultado Actualizar(string? titulo, DateOnly? venceEl, Guid? responsableId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (Estado != EstadoTarea.Pendiente)
        {
            return Resultado.Fallo(Error.Conflicto("tarea.ya_cerrada", "Una tarea cerrada ya no se edita."));
        }

        if (string.IsNullOrWhiteSpace(titulo))
        {
            return Resultado.Fallo(Error.Validacion("tarea.titulo_vacio", "La tarea necesita un título."));
        }

        Titulo = titulo.Trim();
        if (venceEl is { } f)
        {
            VenceEl = f;
        }

        ResponsableId = responsableId;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    /// <summary>¿Tocaba antes de hoy y sigue pendiente?</summary>
    public bool EstaVencida(DateOnly hoy) => Estado == EstadoTarea.Pendiente && VenceEl < hoy;

    /// <summary>¿Entra en la pila de hoy? Todo lo que vence hoy o antes.</summary>
    public bool TocaHoy(DateOnly hoy) => Estado == EstadoTarea.Pendiente && VenceEl <= hoy;
}
