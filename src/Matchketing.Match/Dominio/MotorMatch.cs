namespace Matchketing.Match.Dominio;

/// <summary>
/// La puntuación con sus motivos. **Nunca se construye sin motivos**: si no hay nada que contar, el
/// resultado es <see cref="Desconocido"/> y la interfaz enseña un guion, no un número (invariante M1).
/// </summary>
public sealed record ResultadoMatch(int? Match, int Encaje, int Momento, IReadOnlyList<string> Motivos, bool SinHistorico)
{
    public static ResultadoMatch Desconocido(int encaje, int momento, bool sinHistorico) =>
        new(null, encaje, momento, [], sinHistorico);

    /// <summary>La frase de una línea que acompaña siempre al número.</summary>
    public string Explicacion => Motivos.Count == 0 ? "Sin datos suficientes." : string.Join(" · ", Motivos) + ".";
}

/// <summary>
/// Junta Encaje y Momento en el Match, y **redacta el porqué**. El número sin la frase no se
/// enseña: una puntuación que no se puede explicar no la usa nadie.
/// </summary>
public static class MotorMatch
{
    /// <summary>Cuántos motivos se enseñan. Tres: los que caben en una frase que se lea de un vistazo.</summary>
    public const int MotivosQueSeEnsenan = 3;

    public static ResultadoMatch Calcular(
        DatosContacto datos,
        PerfilGanadas perfil,
        IEnumerable<SenalPuntuable> senales,
        decimal pesoEncaje,
        DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(datos);
        ArgumentNullException.ThrowIfNull(perfil);
        ArgumentNullException.ThrowIfNull(senales);

        var (encaje, aportesEncaje, sinHistorico) = CalculadoraEncaje.Calcular(datos, perfil);
        var (momento, aportesMomento) = CalculadoraMomento.Calcular(senales, ahora);

        var peso = (double)Math.Clamp(pesoEncaje, 0m, 1m);
        var match = (int)Math.Round((peso * encaje) + ((1 - peso) * momento), MidpointRounding.AwayFromZero);

        var candidatos = aportesEncaje.Concat(aportesMomento).Where(a => Math.Abs(a.Puntos) > 0.5).ToList();

        // Un aviso —que lleva dos meses en silencio— tiene plaza reservada aunque haya factores
        // positivos que puntúen más alto. Es el dato que cambia lo que haces, y esconderlo detrás
        // de tres buenas noticias sería justo el tipo de puntuación bonita e inútil que no queremos.
        var avisos = candidatos.Where(a => a.Puntos < 0).OrderBy(a => a.Puntos).Take(1).ToList();

        var motivos = avisos
            .Concat(candidatos
                .Where(a => a.Puntos > 0)
                .OrderByDescending(a => a.Puntos)
                .Take(MotivosQueSeEnsenan - avisos.Count))
            .Select(a => a.Frase)
            .ToList();

        // M1: sin ningún motivo redactable no hay número.
        if (motivos.Count == 0)
        {
            return sinHistorico
                ? new ResultadoMatch(null, encaje, momento, ["Todavía sin histórico para calibrar el encaje"], true)
                : ResultadoMatch.Desconocido(encaje, momento, false);
        }

        return new ResultadoMatch(Math.Clamp(match, 0, 100), encaje, momento, motivos, sinHistorico);
    }
}
