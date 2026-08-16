namespace Matchketing.Match.Dominio;

/// <summary>Una señal ya leída de la base, con lo justo para puntuar.</summary>
public readonly record struct SenalPuntuable(TipoSenal Tipo, DateTimeOffset OcurridaEn);

/// <summary>Lo que aporta un factor, con su frase ya escrita.</summary>
public readonly record struct Aporte(string Clave, double Puntos, string Frase);

/// <summary>
/// El **Momento**: qué ha pasado últimamente. Cada señal aporta su peso multiplicado por un
/// decaimiento exponencial de semivida siete días, así que el interés caduca solo y nadie tiene que
/// acordarse de enfriar una lista a mano.
/// </summary>
public static class CalculadoraMomento
{
    public static (int Momento, IReadOnlyList<Aporte> Aportes) Calcular(IEnumerable<SenalPuntuable> senales, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(senales);

        var lista = senales.Where(s => s.OcurridaEn <= ahora).ToList();
        var aportes = new List<Aporte>();

        if (lista.Count == 0)
        {
            return (0, aportes);
        }

        // Tope diario por tipo: nos quedamos con las más recientes de cada día.
        var contadas = lista
            .GroupBy(s => new { s.Tipo, Dia = s.OcurridaEn.UtcDateTime.Date })
            .SelectMany(g => g.OrderByDescending(s => s.OcurridaEn).Take(PesosSenal.TopeDiario(g.Key.Tipo)))
            .ToList();

        var porTipo = new Dictionary<TipoSenal, (double Puntos, DateTimeOffset MasReciente)>();

        foreach (var s in contadas)
        {
            var dias = (ahora - s.OcurridaEn).TotalDays;
            var aporte = PesosSenal.Peso(s.Tipo) * Math.Pow(0.5, dias / PesosSenal.SemividaDias);

            if (porTipo.TryGetValue(s.Tipo, out var previo))
            {
                porTipo[s.Tipo] = (previo.Puntos + aporte, s.OcurridaEn > previo.MasReciente ? s.OcurridaEn : previo.MasReciente);
            }
            else
            {
                porTipo[s.Tipo] = (aporte, s.OcurridaEn);
            }
        }

        var total = porTipo.Values.Sum(v => v.Puntos);

        foreach (var (tipo, valor) in porTipo.OrderByDescending(p => p.Value.Puntos))
        {
            var cuantas = contadas.Count(s => s.Tipo == tipo);
            var frase = cuantas > 1
                ? $"{Mayuscula(PesosSenal.Describir(tipo))} {cuantas} veces"
                : Mayuscula(PesosSenal.Describir(tipo));

            aportes.Add(new Aporte($"senal.{tipo}", valor.Puntos, $"{frase} {Cuando(ahora - valor.MasReciente)}"));
        }

        // Sin nada en un mes, el interés no es que sea bajo: es que se ha ido.
        var ultima = lista.Max(s => s.OcurridaEn);
        if ((ahora - ultima).TotalDays > PesosSenal.DiasParaInactividad)
        {
            total += PesosSenal.PenalizacionInactividad;
            aportes.Add(new Aporte("senal.inactivo", PesosSenal.PenalizacionInactividad,
                $"Sin señales desde hace {(int)(ahora - ultima).TotalDays} días"));
        }

        return ((int)Math.Round(Math.Clamp(total, 0, 100), MidpointRounding.AwayFromZero), aportes);
    }

    private static string Mayuscula(string texto) => char.ToUpperInvariant(texto[0]) + texto[1..];

    private static string Cuando(TimeSpan hace) => hace.TotalDays switch
    {
        < 1 => "hoy",
        < 2 => "ayer",
        < 8 => "esta semana",
        < 31 => $"hace {(int)(hace.TotalDays / 7)} semanas",
        _ => $"hace {(int)(hace.TotalDays / 30)} meses",
    };
}
