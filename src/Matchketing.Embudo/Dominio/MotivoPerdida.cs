namespace Matchketing.Embudo.Dominio;

/// <summary>
/// Por qué se pierde una oportunidad. Lista corta y cerrada a propósito: de aquí sale el informe
/// más útil que tendrá el gerente, y con texto libre no habría informe que valga.
/// </summary>
public enum MotivoPerdida
{
    Precio = 1,
    Plazo = 2,
    Competencia = 3,
    NoEraElMomento = 4,
    NoContesta = 5,
    Otro = 6,
}

public enum EstadoOportunidad
{
    Abierta = 1,
    Ganada = 2,
    Perdida = 3,
}
