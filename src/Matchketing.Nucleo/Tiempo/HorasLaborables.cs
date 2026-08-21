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
    /// El mismo instante, en hora española. Se expone porque hay más cosas que se cuentan en hora
    /// local y no en UTC: el aviso del viernes a las seis de la tarde es a las seis **de aquí**.
    /// </summary>
    public static DateTimeOffset EnHoraLocal(DateTimeOffset instante) => TimeZoneInfo.ConvertTime(instante, Zona);

    /// <summary>
    /// El día de hoy **como lo cuenta la persona que lo está viviendo**, no como lo cuenta UTC.
    ///
    /// Existe porque no existía, y eso era un fallo con dos horas de ventana cada noche. Lo que crea
    /// tareas usaba la hora española y lo que las enseña usaba UTC, así que entre medianoche y las dos
    /// de la mañana en verano el mismo instante era «hoy» en un sitio y «mañana» en otro: una tarea que
    /// el sistema creaba para hoy no aparecía en Hoy, y el trabajo hecho a las 00:30 se contaba como de
    /// ayer. En invierno la ventana es de una hora, que es peor: se reproduce menos.
    ///
    /// Un CRM tiene **un solo** concepto de día, y es el del comercial que lo abre. Todo lo que
    /// convierta un instante en fecha pasa por aquí.
    /// </summary>
    public static DateOnly DiaDeTrabajo(DateTimeOffset instante) =>
        DateOnly.FromDateTime(EnHoraLocal(instante).Date);

    /// <summary>
    /// Lo mismo, para una fecha que ya viene dada: de su medianoche de aquí a la siguiente.
    ///
    /// Es la sobrecarga que usan los filtros por fechas —«desde el 22 hasta el 31»— para que un rango
    /// escrito por una persona signifique lo que esa persona vivió y no lo que marcaba UTC.
    /// </summary>
    public static (DateTimeOffset Desde, DateTimeOffset Hasta) LimitesDelDia(DateOnly dia)
    {
        // A las dos medianoches se les pide el desfase **por su propia fecha**, cada una la suya. El día
        // del cambio de hora no dura veinticuatro horas: el 29 de marzo a mediodía el desfase ya es +2,
        // pero la medianoche de ese mismo día fue +1, y usar uno para las dos se come una hora.
        //
        // Y salen **en UTC**: es el mismo instante, pero Npgsql se niega a escribir un `DateTimeOffset`
        // con desfase distinto de cero en un `timestamp with time zone` —«only offset 0 (UTC) is
        // supported»—, y estos dos valores acaban en un `WHERE` como parámetros. Con +02:00 la consulta
        // revienta en tiempo de ejecución y no al compilar.
        var hoy = dia.ToDateTime(TimeOnly.MinValue);
        var manana = hoy.AddDays(1);

        return (
            new DateTimeOffset(hoy, Zona.GetUtcOffset(hoy)).ToUniversalTime(),
            new DateTimeOffset(manana, Zona.GetUtcOffset(manana)).ToUniversalTime());
    }

    /// <summary>
    /// El día local de un instante, en instantes: de la medianoche de aquí a la siguiente.
    ///
    /// Hace falta para contar en la base de datos «lo cerrado hoy» sin convertir zonas dentro del SQL:
    /// se comparan instantes contra un rango, que además usa índice. Abierto por arriba, igual que los
    /// límites de un mes: con `<=` se perdería lo que pasa exactamente a medianoche.
    /// </summary>
    public static (DateTimeOffset Desde, DateTimeOffset Hasta) LimitesDelDia(DateTimeOffset instante)
    {
        return LimitesDelDia(DiaDeTrabajo(instante));
    }

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
