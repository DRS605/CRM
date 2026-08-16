using FluentAssertions;
using Matchketing.Match.Dominio;
using Xunit;

namespace Matchketing.Match.Tests;

public sealed class PruebasMotorMatch
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static PerfilGanadas Perfil(int cerradas = 40) => new(
        ["Hostelería"], ["Valencia"], ["feria"], 5, 50, cerradas);

    private static DatosContacto Bueno() => new("Hostelería", "Valencia", "feria", 8, true, true);

    private static DatosContacto Malo() => new("Minería", "Lugo", "puerta fría", 5000, false, false);

    [Fact]
    public void El_match_es_la_media_ponderada_de_encaje_y_momento()
    {
        var senales = new[] { new SenalPuntuable(TipoSenal.FormularioEnviado, Ahora) };

        var r = MotorMatch.Calcular(Bueno(), Perfil(), senales, 0.5m, Ahora);

        r.Encaje.Should().Be(100);
        r.Momento.Should().Be(35);
        r.Match.Should().Be(68, "0,5 × 100 + 0,5 × 35 = 67,5");
    }

    [Fact]
    public void Con_peso_uno_solo_cuenta_el_encaje()
    {
        var senales = new[] { new SenalPuntuable(TipoSenal.FormularioEnviado, Ahora) };

        MotorMatch.Calcular(Bueno(), Perfil(), senales, 1m, Ahora).Match.Should().Be(100);
    }

    [Fact]
    public void Con_peso_cero_solo_cuenta_el_momento()
    {
        var senales = new[] { new SenalPuntuable(TipoSenal.FormularioEnviado, Ahora) };

        MotorMatch.Calcular(Bueno(), Perfil(), senales, 0m, Ahora).Match.Should().Be(35);
    }

    [Fact]
    public void Sin_ningun_motivo_que_contar_no_hay_numero()
    {
        var r = MotorMatch.Calcular(Malo(), Perfil(), [], 0.5m, Ahora);

        r.Match.Should().BeNull("un número sin porqué no lo usa nadie");
        r.Motivos.Should().BeEmpty();
        r.Explicacion.Should().Be("Sin datos suficientes.");
    }

    [Fact]
    public void Sin_historico_el_motivo_lo_dice_aunque_no_haya_señales()
    {
        var r = MotorMatch.Calcular(Malo(), Perfil(5), [], 0.5m, Ahora);

        r.SinHistorico.Should().BeTrue();
        r.Match.Should().BeNull();
        r.Motivos.Should().ContainSingle().Which.Should().Contain("sin histórico");
    }

    [Fact]
    public void Se_enseñan_como_mucho_tres_motivos()
    {
        var senales = new[]
        {
            new SenalPuntuable(TipoSenal.FormularioEnviado, Ahora),
            new SenalPuntuable(TipoSenal.RespuestaCorreo, Ahora),
            new SenalPuntuable(TipoSenal.LlamadaContestada, Ahora),
        };

        var r = MotorMatch.Calcular(Bueno(), Perfil(), senales, 0.5m, Ahora);

        r.Motivos.Should().HaveCount(3, "tres son los que caben en una frase que se lea de un vistazo");
    }

    [Fact]
    public void Los_motivos_se_ordenan_por_lo_que_de_verdad_aportan()
    {
        var senales = new[] { new SenalPuntuable(TipoSenal.VisitaWeb, Ahora) };

        var r = MotorMatch.Calcular(Bueno(), Perfil(), senales, 0.5m, Ahora);

        r.Motivos[0].Should().Contain("hostelería", "el sector aporta 30, la visita 6");
    }

    [Fact]
    public void El_silencio_largo_sale_como_motivo_porque_es_lo_primero_que_hay_que_saber()
    {
        var senales = new[] { new SenalPuntuable(TipoSenal.FormularioEnviado, Ahora.AddDays(-45)) };

        var r = MotorMatch.Calcular(Bueno(), Perfil(), senales, 0.5m, Ahora);

        r.Motivos.Should().Contain(m => m.Contains("Sin señales", StringComparison.Ordinal));
    }

    [Fact]
    public void La_explicacion_es_una_frase_con_los_motivos_separados_por_puntos()
    {
        var senales = new[] { new SenalPuntuable(TipoSenal.FormularioEnviado, Ahora) };

        var r = MotorMatch.Calcular(Bueno(), Perfil(), senales, 0.5m, Ahora);

        r.Explicacion.Should().EndWith(".");
        r.Explicacion.Should().Contain(" · ");
    }

    [Fact]
    public void Un_peso_fuera_de_rango_se_recorta_en_vez_de_reventar()
    {
        var senales = new[] { new SenalPuntuable(TipoSenal.FormularioEnviado, Ahora) };

        MotorMatch.Calcular(Bueno(), Perfil(), senales, 5m, Ahora).Match.Should().Be(100);
        MotorMatch.Calcular(Bueno(), Perfil(), senales, -3m, Ahora).Match.Should().Be(35);
    }
}
