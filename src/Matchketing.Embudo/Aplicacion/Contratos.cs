using Matchketing.Embudo.Dominio;

namespace Matchketing.Embudo.Aplicacion;

public sealed record OportunidadVista(
    Guid Id, string Titulo, decimal Importe, Guid ContactoId, string NombreContacto,
    string? NombreCuenta, Guid EtapaId, EstadoOportunidad Estado, DateOnly? PrevistaCierre,
    int DiasEnEtapa, bool Estancada, MotivoPerdida? Motivo);

public sealed record ColumnaEmbudo(
    Guid EtapaId, string Nombre, int Orden, int Probabilidad,
    int Cuantas, decimal Importe, IReadOnlyList<OportunidadVista> Oportunidades);

/// <summary>El tablero completo: columnas con sus sumas y la previsión ponderada por etapa.</summary>
public sealed record Tablero(
    Guid EmbudoId, string Nombre, IReadOnlyList<ColumnaEmbudo> Columnas,
    int TotalAbiertas, decimal ImporteAbierto, decimal PrevisionPonderada, int Estancadas);
