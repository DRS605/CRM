using FluentAssertions;
using Matchketing.Nucleo.Tiempo;
using Xunit;

namespace Matchketing.Nucleo.Tests;

/// <summary>
/// Las pruebas se escriben en hora local española (con su desplazamiento explícito) porque es la
/// única forma de leerlas: la franja laboral está definida en esa hora, no en UTC.
/// </summary>
public sealed class PruebasHorasLaborables
{
    /// <summary>Verano: UTC+2. Un miércoles cualquiera.</summary>
    private static DateTimeOffset Miercoles(int hora, int minuto = 0) =>
        new(2026, 8, 19, hora, minuto, 0, TimeSpan.FromHours(2));

    /// <summary>Viernes de la misma semana.</summary>
    private static DateTimeOffset Viernes(int hora, int minuto = 0) =>
        new(2026, 8, 21, hora, minuto, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Dentro_de_la_jornada_suma_horas_normales()
    {
        HorasLaborables.Sumar(Miercoles(10), 4).Should().Be(Miercoles(14));
    }

    [Fact]
    public void Lo_que_no_cabe_en_el_dia_pasa_a_la_manana_siguiente()
    {
        // De 16:00 quedan dos horas hasta las 18:00; la tercera se cuenta el jueves desde las 9:00.
        HorasLaborables.Sumar(Miercoles(16), 3).Should().Be(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Fuera_de_horario_el_plazo_empieza_en_la_siguiente_apertura()
    {
        // Un lead que entra a las once de la noche no lleva perdidas diez horas por la mañana.
        HorasLaborables.Sumar(Miercoles(23), 2).Should().Be(new DateTimeOffset(2026, 8, 20, 11, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void El_fin_de_semana_no_cuenta()
    {
        // Este es **el** caso que justifica la clase: un lead del viernes por la tarde con plazo de
        // cuatro horas vence el lunes por la mañana, no el sábado de madrugada.
        var vence = HorasLaborables.Sumar(Viernes(17), 4);

        vence.Should().Be(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.FromHours(2)));
        vence.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void Un_sabado_entero_no_consume_plazo()
    {
        var sabado = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.FromHours(2));
        var domingoNoche = new DateTimeOffset(2026, 8, 23, 23, 0, 0, TimeSpan.FromHours(2));

        HorasLaborables.Entre(sabado, domingoNoche).Should().Be(0);
    }

    [Fact]
    public void Entre_cuenta_solo_el_tiempo_de_trabajo()
    {
        // De miércoles 17:00 a jueves 10:00: una hora el miércoles y una el jueves.
        HorasLaborables.Entre(Miercoles(17), new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.FromHours(2)))
            .Should().BeApproximately(2, 0.001);
    }

    [Fact]
    public void Entre_nunca_es_negativo()
    {
        HorasLaborables.Entre(Miercoles(15), Miercoles(10)).Should().Be(0);
    }

    [Fact]
    public void Cero_horas_devuelve_la_siguiente_apertura()
    {
        HorasLaborables.Sumar(Miercoles(3), 0).Should().Be(Miercoles(9));
    }

    [Fact]
    public void La_jornada_es_de_nueve_horas()
    {
        HorasLaborables.JornadaCompleta.Should().Be(TimeSpan.FromHours(9));
    }

    [Fact]
    public void Un_plazo_de_cinco_jornadas_se_cumple_al_cerrar_el_quinto_dia()
    {
        // 45 horas son cinco jornadas exactas: miércoles, jueves, viernes, lunes y martes. Vence al
        // cerrar el martes, no al abrir el miércoles siguiente: el plazo se cumple cuando pasa la
        // hora número cuarenta y cinco, y esa hora es la última del martes.
        HorasLaborables.Sumar(Miercoles(9), 45)
            .Should().Be(new DateTimeOffset(2026, 8, 25, 18, 0, 0, TimeSpan.FromHours(2)));
    }

    // ---- El día ----------------------------------------------------------------------------

    [Fact]
    public void A_medianoche_de_aqui_ya_es_el_dia_siguiente_aunque_en_UTC_no_lo_sea()
    {
        // **El fallo que esto arregla.** En verano España va dos horas por delante de UTC, así que a las
        // 22:30 UTC aquí es la medianoche y media del día siguiente. Lo que creaba tareas contaba en hora
        // de aquí y lo que las enseñaba contaba en UTC: una tarea creada «para hoy» a esa hora salía con
        // fecha de mañana y no aparecía en Hoy. Dos horas de ventana cada noche en verano, una en
        // invierno —que es peor, porque se reproduce menos—.
        var deNoche = new DateTimeOffset(2026, 8, 21, 22, 30, 0, TimeSpan.Zero);

        HorasLaborables.DiaDeTrabajo(deNoche).Should().Be(new DateOnly(2026, 8, 22));
        DateOnly.FromDateTime(deNoche.UtcDateTime).Should().Be(new DateOnly(2026, 8, 21),
            "así se contaba antes, y de ahí venía el desfase");
    }

    [Fact]
    public void Durante_el_dia_las_dos_formas_de_contar_coinciden()
    {
        // Por eso el fallo era difícil de ver: el noventa por ciento del día no se nota.
        var deManana = new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero);

        HorasLaborables.DiaDeTrabajo(deManana).Should().Be(new DateOnly(2026, 8, 21));
    }

    [Fact]
    public void Los_limites_del_dia_van_de_medianoche_de_aqui_a_la_siguiente()
    {
        var mediodia = new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);

        var (desde, hasta) = HorasLaborables.LimitesDelDia(mediodia);

        // En agosto España es UTC+2: la medianoche de aquí son las 22:00 UTC del día anterior.
        desde.UtcDateTime.Should().Be(new DateTime(2026, 8, 20, 22, 0, 0, DateTimeKind.Utc));
        hasta.UtcDateTime.Should().Be(new DateTime(2026, 8, 21, 22, 0, 0, DateTimeKind.Utc));
        (hasta - desde).Should().Be(TimeSpan.FromHours(24));
    }

    [Fact]
    public void Un_instante_esta_siempre_dentro_de_los_limites_de_su_propio_dia()
    {
        // La propiedad que importa, comprobada a lo largo de un año entero y hora a hora: si un instante
        // se saliera de su propio día, «lo hecho hoy» perdería o duplicaría trabajo. Así se cubren de
        // paso los dos cambios de hora, que es donde esto se rompe.
        var momento = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 365 * 24; i++)
        {
            var (desde, hasta) = HorasLaborables.LimitesDelDia(momento);

            momento.Should().BeOnOrAfter(desde).And.BeBefore(hasta,
                $"el instante {momento:u} tiene que caer dentro de su propio día");

            momento = momento.AddHours(1);
        }
    }

    [Fact]
    public void El_dia_del_cambio_de_hora_no_dura_veinticuatro_horas_y_los_limites_lo_saben()
    {
        // Último domingo de marzo de 2026: el 29. Ese día España pasa de UTC+1 a UTC+2 y dura 23 horas.
        // Sumar «un día» al instante habría dado la 01:00 en vez de medianoche.
        var eseDia = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);

        var (desde, hasta) = HorasLaborables.LimitesDelDia(eseDia);

        (hasta - desde).Should().Be(TimeSpan.FromHours(23));
        HorasLaborables.DiaDeTrabajo(desde).Should().Be(new DateOnly(2026, 3, 29));

        // Y el último domingo de octubre dura 25.
        var enOctubre = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var (d, h) = HorasLaborables.LimitesDelDia(enOctubre);
        (h - d).Should().Be(TimeSpan.FromHours(25));
    }
}
