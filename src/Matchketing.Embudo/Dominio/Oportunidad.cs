using Matchketing.Nucleo.Dominio;
using Matchketing.Nucleo.Resultados;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.Embudo.Dominio;

public sealed record OportunidadCreada(Guid OportunidadId, Guid EmpresaId, Guid ContactoId, DateTimeOffset OcurridoEn) : IEventoDominio;

public sealed record OportunidadGanada(Guid OportunidadId, Guid EmpresaId, Guid ContactoId, decimal Importe, DateTimeOffset OcurridoEn) : IEventoDominio;

public sealed record OportunidadPerdida(Guid OportunidadId, Guid EmpresaId, Guid ContactoId, MotivoPerdida Motivo, DateTimeOffset OcurridoEn) : IEventoDominio;

/// <summary>
/// Que una oportunidad estuvo en una etapa. Se anota al crearla y en cada movimiento, y no se borra
/// nunca: es lo único con lo que se puede decir de verdad cuántas llegaron a «Propuesta». Sin este
/// rastro, cualquier porcentaje de conversión sería inventado.
/// </summary>
public sealed class PasoEtapa : EntidadBase<Guid>
{
    private PasoEtapa(Guid id)
        : base(id)
    {
    }

    internal PasoEtapa(Guid id, Guid oportunidadId, Guid etapaId, DateTimeOffset entroEn)
        : base(id)
    {
        OportunidadId = oportunidadId;
        EtapaId = etapaId;
        EntroEn = entroEn;
    }

    public Guid OportunidadId { get; private set; }

    public Guid EtapaId { get; private set; }

    public DateTimeOffset EntroEn { get; private set; }
}

/// <summary>
/// Una venta en curso. Su estado se **deriva** de si está cerrada o no (invariante O2), su
/// probabilidad la pone la etapa (O4), y perderla **exige motivo** (O1).
/// </summary>
public sealed class Oportunidad : RaizAgregadoEmpresa<Guid>
{
    public const int LongitudMaximaTitulo = 160;
    public const int LongitudMaximaDetalle = 500;

    private readonly List<PasoEtapa> pasos = [];

    private Oportunidad(Guid id)
        : base(id, Guid.Empty) => Titulo = null!;

    private Oportunidad(Guid id, Guid empresaId, Guid contactoId, Guid? cuentaId, string titulo, decimal importe, Guid embudoId, Guid etapaId, DateOnly? previstaCierre, Guid? propietarioId, DateTimeOffset ahora)
        : base(id, empresaId)
    {
        ContactoId = contactoId;
        CuentaId = cuentaId;
        Titulo = titulo;
        Importe = importe;
        EmbudoId = embudoId;
        EtapaId = etapaId;
        PrevistaCierre = previstaCierre;
        PropietarioId = propietarioId;
        EntroEnEtapaEn = ahora;
        CreadoEn = ahora;
        ActualizadoEn = ahora;
        pasos.Add(new PasoEtapa(Guid.NewGuid(), id, etapaId, ahora));
    }

    public Guid ContactoId { get; private set; }

    public Guid? CuentaId { get; private set; }

    public string Titulo { get; private set; }

    /// <summary>Importe estimado. Sin ALXOR Core conectado, aquí se para: esto no factura.</summary>
    public decimal Importe { get; private set; }

    public Guid EmbudoId { get; private set; }

    public Guid EtapaId { get; private set; }

    /// <summary>Por qué etapas ha pasado, en orden. Append-only.</summary>
    public IReadOnlyList<PasoEtapa> Pasos => pasos.OrderBy(p => p.EntroEn).ToList();

    /// <summary>Cuándo entró en la etapa actual. Es la base del aviso de estancamiento.</summary>
    public DateTimeOffset EntroEnEtapaEn { get; private set; }

    public DateOnly? PrevistaCierre { get; private set; }

    public Guid? PropietarioId { get; private set; }

    public MotivoPerdida? Motivo { get; private set; }

    public string? DetalleMotivo { get; private set; }

    public DateTimeOffset? CerradaEn { get; private set; }

    public DateTimeOffset CreadoEn { get; private set; }

    public DateTimeOffset ActualizadoEn { get; private set; }

    /// <summary>O2: el estado no se guarda, se deduce. No hay forma de que se descuadre.</summary>
    public EstadoOportunidad Estado => CerradaEn is null
        ? EstadoOportunidad.Abierta
        : Motivo is null ? EstadoOportunidad.Ganada : EstadoOportunidad.Perdida;

    public static Resultado<Oportunidad> Crear(
        Guid empresaId, Guid contactoId, Guid? cuentaId, string? titulo, decimal importe,
        Embudo embudo, Guid? etapaId, DateOnly? previstaCierre, Guid? propietarioId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(embudo);
        ArgumentNullException.ThrowIfNull(reloj);

        if (string.IsNullOrWhiteSpace(titulo))
        {
            return Resultado.Fallo<Oportunidad>(Error.Validacion("oportunidad.titulo_vacio", "La oportunidad necesita un título."));
        }

        if (titulo.Trim().Length > LongitudMaximaTitulo)
        {
            return Resultado.Fallo<Oportunidad>(Error.Validacion("oportunidad.titulo_largo", "El título es demasiado largo."));
        }

        if (importe < 0m)
        {
            return Resultado.Fallo<Oportunidad>(Error.Validacion("oportunidad.importe_negativo", "El importe no puede ser negativo."));
        }

        var etapa = etapaId is { } id ? embudo.EtapaCon(id) : embudo.Primera();
        if (etapa is null)
        {
            return Resultado.Fallo<Oportunidad>(Error.Validacion("oportunidad.etapa_invalida", "La etapa no pertenece a este embudo."));
        }

        var oportunidad = new Oportunidad(
            Guid.NewGuid(), empresaId, contactoId, cuentaId, titulo.Trim(), decimal.Round(importe, 2),
            embudo.Id, etapa.Id, previstaCierre, propietarioId, reloj.AhoraUtc);

        oportunidad.RegistrarEvento(new OportunidadCreada(oportunidad.Id, empresaId, contactoId, reloj.AhoraUtc));
        return Resultado.Ok(oportunidad);
    }

    /// <summary>Mueve la oportunidad de etapa y reinicia el contador de estancamiento.</summary>
    public Resultado Mover(Embudo embudo, Guid etapaId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(embudo);
        ArgumentNullException.ThrowIfNull(reloj);

        if (CerradaEn is not null)
        {
            return Resultado.Fallo(Error.Conflicto("oportunidad.cerrada", "Una oportunidad cerrada no se mueve. Si vuelve, se crea otra."));
        }

        if (embudo.Id != EmbudoId || embudo.EtapaCon(etapaId) is null)
        {
            return Resultado.Fallo(Error.Validacion("oportunidad.etapa_invalida", "La etapa no pertenece a este embudo."));
        }

        if (etapaId == EtapaId)
        {
            return Resultado.Ok();
        }

        EtapaId = etapaId;
        EntroEnEtapaEn = reloj.AhoraUtc;
        ActualizadoEn = reloj.AhoraUtc;
        pasos.Add(new PasoEtapa(Guid.NewGuid(), Id, etapaId, reloj.AhoraUtc));
        return Resultado.Ok();
    }

    public Resultado Actualizar(string? titulo, decimal importe, DateOnly? previstaCierre, Guid? propietarioId, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (CerradaEn is not null)
        {
            return Resultado.Fallo(Error.Conflicto("oportunidad.cerrada", "Una oportunidad cerrada ya no se edita."));
        }

        if (string.IsNullOrWhiteSpace(titulo))
        {
            return Resultado.Fallo(Error.Validacion("oportunidad.titulo_vacio", "La oportunidad necesita un título."));
        }

        if (importe < 0m)
        {
            return Resultado.Fallo(Error.Validacion("oportunidad.importe_negativo", "El importe no puede ser negativo."));
        }

        Titulo = titulo.Trim();
        Importe = decimal.Round(importe, 2);
        PrevistaCierre = previstaCierre;
        PropietarioId = propietarioId;
        ActualizadoEn = reloj.AhoraUtc;
        return Resultado.Ok();
    }

    public Resultado Ganar(IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (CerradaEn is not null)
        {
            return Resultado.Fallo(Error.Conflicto("oportunidad.ya_cerrada", "Esta oportunidad ya está cerrada. Si el cliente vuelve, se crea otra."));
        }

        CerradaEn = reloj.AhoraUtc;
        ActualizadoEn = reloj.AhoraUtc;
        RegistrarEvento(new OportunidadGanada(Id, EmpresaId, ContactoId, Importe, reloj.AhoraUtc));
        return Resultado.Ok();
    }

    /// <summary>
    /// O1: perder **exige motivo**. Sin él la transición falla, y no por rigidez: sin motivo no hay
    /// informe de pérdidas, que es lo único que enseña a vender mejor.
    /// </summary>
    public Resultado Perder(MotivoPerdida? motivo, string? detalle, IReloj reloj)
    {
        ArgumentNullException.ThrowIfNull(reloj);

        if (CerradaEn is not null)
        {
            return Resultado.Fallo(Error.Conflicto("oportunidad.ya_cerrada", "Esta oportunidad ya está cerrada. Si el cliente vuelve, se crea otra."));
        }

        if (motivo is null || !Enum.IsDefined(motivo.Value))
        {
            return Resultado.Fallo(Error.Validacion("oportunidad.sin_motivo", "Para dar una oportunidad por perdida hay que decir por qué."));
        }

        if (detalle?.Trim().Length > LongitudMaximaDetalle)
        {
            return Resultado.Fallo(Error.Validacion("oportunidad.detalle_largo", "El detalle del motivo es demasiado largo."));
        }

        Motivo = motivo;
        DetalleMotivo = string.IsNullOrWhiteSpace(detalle) ? null : detalle.Trim();
        CerradaEn = reloj.AhoraUtc;
        ActualizadoEn = reloj.AhoraUtc;
        RegistrarEvento(new OportunidadPerdida(Id, EmpresaId, ContactoId, motivo.Value, reloj.AhoraUtc));
        return Resultado.Ok();
    }

    /// <summary>¿Lleva más días parada en esta etapa de los que la etapa tolera?</summary>
    public bool EstaEstancada(int diasAviso, DateTimeOffset ahora) =>
        CerradaEn is null && (ahora - EntroEnEtapaEn).TotalDays > diasAviso;

    public int DiasEnEtapa(DateTimeOffset ahora) => (int)(ahora - EntroEnEtapaEn).TotalDays;
}
