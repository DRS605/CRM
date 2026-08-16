using FluentAssertions;
using Matchketing.Match.Dominio;
using Xunit;

namespace Matchketing.Match.Tests;

public sealed class PruebasMomento
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static SenalPuntuable Hace(TipoSenal tipo, double dias) => new(tipo, Ahora.AddDays(-dias));

    [Fact]
    public void Sin_señales_el_momento_es_cero()
    {
        var (momento, aportes) = CalculadoraMomento.Calcular([], Ahora);

        momento.Should().Be(0);
        aportes.Should().BeEmpty();
    }

    [Fact]
    public void Una_señal_de_hoy_aporta_practicamente_todo_su_peso()
    {
        var (momento, _) = CalculadoraMomento.Calcular([Hace(TipoSenal.FormularioEnviado, 0)], Ahora);

        momento.Should().Be(35);
    }

    [Fact]
    public void A_los_siete_dias_una_señal_vale_la_mitad()
    {
        var (momento, _) = CalculadoraMomento.Calcular([Hace(TipoSenal.FormularioEnviado, 7)], Ahora);

        momento.Should().Be(18, "35 × 0,5 = 17,5, que redondea a 18");
    }

    [Fact]
    public void A_los_catorce_dias_vale_la_cuarta_parte()
    {
        var (momento, _) = CalculadoraMomento.Calcular([Hace(TipoSenal.FormularioEnviado, 14)], Ahora);

        momento.Should().Be(9, "35 × 0,25 = 8,75");
    }

    [Fact]
    public void Responder_pesa_mas_que_abrir_y_abrir_mas_que_pasar_por_la_web()
    {
        int M(TipoSenal t) => CalculadoraMomento.Calcular([Hace(t, 0)], Ahora).Momento;

        M(TipoSenal.RespuestaCorreo).Should().BeGreaterThan(M(TipoSenal.CorreoAbierto));
        M(TipoSenal.CorreoAbierto).Should().BeGreaterThan(M(TipoSenal.VisitaWeb));
    }

    [Fact]
    public void El_tope_diario_impide_que_un_robot_infle_la_puntuacion()
    {
        var veinteAperturas = Enumerable.Range(0, 20)
            .Select(i => new SenalPuntuable(TipoSenal.CorreoAbierto, Ahora.AddMinutes(-i)))
            .ToList();

        var (momento, _) = CalculadoraMomento.Calcular(veinteAperturas, Ahora);

        momento.Should().Be(24, "solo cuentan 3 al día: 3 × 8");
    }

    [Fact]
    public void El_tope_diario_es_por_dia_no_en_total()
    {
        var senales = new List<SenalPuntuable>();
        for (var dia = 0; dia < 3; dia++)
        {
            for (var i = 0; i < 5; i++)
            {
                senales.Add(new SenalPuntuable(TipoSenal.CorreoAbierto, Ahora.AddDays(-dia).AddMinutes(-i)));
            }
        }

        var (momento, _) = CalculadoraMomento.Calcular(senales, Ahora);

        momento.Should().BeGreaterThan(24, "tres días de actividad valen más que uno");
    }

    [Fact]
    public void El_momento_nunca_pasa_de_cien()
    {
        // Un contacto que lo ha hecho todo hoy: formulario, respuesta, reunión, llamada, oportunidad.
        var muchas = new[]
        {
            Hace(TipoSenal.FormularioEnviado, 0), Hace(TipoSenal.FormularioEnviado, 0.1),
            Hace(TipoSenal.RespuestaCorreo, 0), Hace(TipoSenal.RespuestaCorreo, 0.1),
            Hace(TipoSenal.ReunionRealizada, 0), Hace(TipoSenal.LlamadaContestada, 0),
            Hace(TipoSenal.OportunidadCreada, 0),
        };

        CalculadoraMomento.Calcular(muchas, Ahora).Momento.Should().Be(100);
    }

    [Fact]
    public void Abrir_diez_oportunidades_en_un_dia_cuenta_como_una()
    {
        // Es el caso de una importación o de un comercial que se pone al día de golpe: un solo
        // hecho de interés, no diez.
        var diez = Enumerable.Range(0, 10)
            .Select(i => new SenalPuntuable(TipoSenal.OportunidadCreada, Ahora.AddMinutes(-i)))
            .ToList();

        CalculadoraMomento.Calcular(diez, Ahora).Momento.Should().Be(20);
    }

    [Fact]
    public void Todas_las_señales_tienen_tope_diario()
    {
        foreach (var tipo in Enum.GetValues<TipoSenal>())
        {
            PesosSenal.TopeDiario(tipo).Should().BeLessThan(int.MaxValue, $"«{tipo}» debe tener tope");
        }
    }

    [Fact]
    public void Sin_nada_en_un_mes_se_penaliza_y_se_dice()
    {
        var (momento, aportes) = CalculadoraMomento.Calcular([Hace(TipoSenal.FormularioEnviado, 40)], Ahora);

        momento.Should().Be(0, "lo poco que quedaba del formulario menos la penalización, con suelo en 0");
        aportes.Should().Contain(a => a.Clave == "senal.inactivo");
        aportes.Single(a => a.Clave == "senal.inactivo").Frase.Should().Contain("40 días");
    }

    [Fact]
    public void Las_señales_del_futuro_se_ignoran()
    {
        var (momento, _) = CalculadoraMomento.Calcular([new SenalPuntuable(TipoSenal.FormularioEnviado, Ahora.AddDays(3))], Ahora);

        momento.Should().Be(0);
    }

    [Fact]
    public void Los_motivos_se_redactan_con_cuantas_veces_y_cuando()
    {
        var senales = new[]
        {
            new SenalPuntuable(TipoSenal.CorreoAbierto, Ahora.AddDays(-1)),
            new SenalPuntuable(TipoSenal.CorreoAbierto, Ahora.AddDays(-2)),
            new SenalPuntuable(TipoSenal.CorreoAbierto, Ahora.AddDays(-3)),
        };

        var (_, aportes) = CalculadoraMomento.Calcular(senales, Ahora);

        aportes[0].Frase.Should().Be("Abrió tu correo 3 veces ayer");
    }

    [Fact]
    public void Los_aportes_van_ordenados_por_lo_que_pesan()
    {
        var senales = new[] { Hace(TipoSenal.VisitaWeb, 0), Hace(TipoSenal.RespuestaCorreo, 0) };

        var (_, aportes) = CalculadoraMomento.Calcular(senales, Ahora);

        aportes[0].Clave.Should().Be("senal.RespuestaCorreo");
    }
}
