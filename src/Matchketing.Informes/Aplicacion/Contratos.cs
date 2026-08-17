namespace Matchketing.Informes.Aplicacion;

/// <summary>
/// Periodo del informe. Ambos extremos son opcionales: sin nada, «todo lo que hay», que es lo que
/// mira una pyme el primer día.
/// </summary>
public sealed record Periodo(DateOnly? Desde, DateOnly? Hasta)
{
    public static Periodo Todo => new(null, null);

    /// <summary>Los últimos N días contando hoy. El atajo que se usa el 90 % de las veces.</summary>
    public static Periodo UltimosDias(int dias, DateOnly hoy) => new(hoy.AddDays(-dias + 1), hoy);

    public string Descripcion => (Desde, Hasta) switch
    {
        (null, null) => "desde el principio",
        ({ } d, null) => $"desde el {d:dd/MM/yyyy}",
        (null, { } h) => $"hasta el {h:dd/MM/yyyy}",
        ({ } d, { } h) => $"del {d:dd/MM/yyyy} al {h:dd/MM/yyyy}",
    };
}

/// <summary>Una etapa del embudo con lo que tiene dentro y cuánto pasa de ella a la siguiente.</summary>
public sealed record EtapaEmbudo(
    string Nombre,
    int Orden,
    int Probabilidad,
    int Abiertas,
    decimal ImporteAbierto,
    int HanPasado,
    decimal? ConversionALaSiguiente);

public sealed record InformeEmbudo(
    string Periodo,
    IReadOnlyList<EtapaEmbudo> Etapas,
    int Abiertas,
    decimal ImporteAbierto,
    decimal PrevisionPonderada,
    int Ganadas,
    decimal ImporteGanado,
    int Perdidas,
    decimal ImportePerdido,
    decimal? TasaCierre,
    decimal? TicketMedio,
    decimal? DiasMediosParaCerrar);

public sealed record MotivoPerdidaConteo(string Motivo, int Cuantas, decimal Importe, decimal Porcentaje);

public sealed record InformeMotivos(
    string Periodo,
    IReadOnlyList<MotivoPerdidaConteo> Motivos,
    int TotalPerdidas,
    decimal ImportePerdido,
    int TotalGanadas,
    decimal ImporteGanado);
