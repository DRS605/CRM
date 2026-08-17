namespace Matchketing.Nucleo.Tiempo;

/// <summary>
/// Cuenta el tiempo como lo cuenta un comercial: de lunes a viernes, de 9 a 18, hora de España.
///
/// Existe porque «cuatro horas para atender un lead» tiene que significar cuatro horas de trabajo.
/// Con horas de reloj, un lead que entra el viernes a las siete de la tarde habría rebotado el sábado
/// a las once de la noche, y el lunes por la mañana el comercial se encontraría con que le han
/// quitado un lead por no haberlo llamado en fin de semana. Eso no es una regla de servicio: es una
/// forma de que la gente deje de fiarse del sistema.
///
/// La franja es fija a propósito: un CRM que pide configurar el horario laboral antes de poder
/// repartir un lead ya ha perdido. Si algún día hace falta por empresa, se añade al ajuste; hasta
/// entonces, de nueve a seis es lo que hace todo el mundo aquí.
///
/// Los festivos no se contemplan. Cada comunidad y cada municipio tienen los suyos, y equivocarse un
/// día al año cuesta muchísimo menos que mantener catorce calendarios.
/// </summary>
public static class HorasLaborables
{
    public const int PrimeraHora = 9;
    public const int UltimaHora = 18;

    /// <summary>
    /// Zona horaria de trabajo. Si el sistema no trae la base de datos de zonas —contenedores muy
    /// pelados—, se usa UTC+1 fija: en verano la franja quedaría corrida una hora, que es un error
    /// mucho más pequeño que caerse al arrancar.
    /// </summary>
    private static readonly TimeZoneInfo Zona = BuscarZona();

    public static TimeSpan JornadaCompleta => TimeSpan.FromHours(UltimaHora - PrimeraHora);

    /// <summary>
    /// Instante en el que se cumplen <paramref name="horas"/> horas laborables contadas desde
    /// <paramref name="desde"/>. Si arranca fuera de horario, empieza a contar en la siguiente
    /// apertura: un lead que entra a medianoche tiene su plazo desde las nueve de la mañana.
    /// </summary>
    public static DateTimeOffset Sumar(DateTimeOffset desde, int horas)
    {
        var restante = TimeSpan.FromHours(Math.Max(horas, 0));
        var momento = SiguienteApertura(TimeZoneInfo.ConvertTime(desde, Zona));

        while (true)
        {
            var disponible = Cierre(momento) - momento;
            if (restante <= disponible)
            {
                return momento + restante;
            }

            restante -= disponible;
            momento = SiguienteApertura(Cierre(momento));
        }
    }

    /// <summary>Horas laborables transcurridas entre dos instantes. Nunca negativas.</summary>
    public static double Entre(DateTimeOffset desde, DateTimeOffset hasta)
    {
        if (hasta <= desde)
        {
            return 0;
        }

        var total = TimeSpan.Zero;
        var momento = SiguienteApertura(TimeZoneInfo.ConvertTime(desde, Zona));

        while (momento < hasta)
        {
            var cierre = Cierre(momento);
            total += (hasta < cierre ? hasta : cierre) - momento;
            momento = SiguienteApertura(cierre);
        }

        return total.TotalHours;
    }

    /// <summary>El primer instante laborable a partir de aquí. Si ya lo es, se devuelve tal cual.</summary>
    private static DateTimeOffset SiguienteApertura(DateTimeOffset momento)
    {
        while (true)
        {
            if (momento.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                momento = Apertura(momento.AddDays(1));
                continue;
            }

            var apertura = Apertura(momento);
            if (momento < apertura)
            {
                return apertura;
            }

            if (momento >= Cierre(momento))
            {
                momento = Apertura(momento.AddDays(1));
                continue;
            }

            return momento;
        }
    }

    private static DateTimeOffset Apertura(DateTimeOffset dia) => A(dia, PrimeraHora);

    private static DateTimeOffset Cierre(DateTimeOffset dia) => A(dia, UltimaHora);

    private static DateTimeOffset A(DateTimeOffset dia, int hora)
    {
        var local = new DateTime(dia.Year, dia.Month, dia.Day, hora, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Zona.GetUtcOffset(local));
    }

    private static TimeZoneInfo BuscarZona()
    {
        // `FindSystemTimeZoneById` lanza si no encuentra la zona; no hay una variante que devuelva
        // nulo, así que el try/catch es la única forma de preguntarle.
        foreach (var id in new[] { "Europe/Madrid", "Romance Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Se prueba el identificador siguiente.
            }
            catch (InvalidTimeZoneException)
            {
                // Base de datos de zonas corrupta: igual que si no estuviera.
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("matchketing-es", TimeSpan.FromHours(1), "España (aprox.)", "España (aprox.)");
    }
}
