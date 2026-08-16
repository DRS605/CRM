using FluentAssertions;
using Matchketing.Match.Dominio;
using Xunit;

namespace Matchketing.Match.Tests;

public sealed class PruebasEncaje
{
    private static PerfilGanadas Perfil(int cerradas = 40) => new(
        ["Hostelería", "Automoción", "Hotelería"],
        ["Valencia", "Alicante"],
        ["feria", "recomendación"],
        5, 50, cerradas);

    private static DatosContacto Contacto(
        string? sector = "Hostelería", string? provincia = "Valencia", string origen = "feria",
        int? tamano = 8, bool email = true, bool telefono = true) =>
        new(sector, provincia, origen, tamano, email, telefono);

    [Fact]
    public void Sin_veinte_cierres_el_encaje_es_neutro_y_se_dice()
    {
        var (encaje, aportes, sinHistorico) = CalculadoraEncaje.Calcular(Contacto(), Perfil(19));

        encaje.Should().Be(50);
        sinHistorico.Should().BeTrue();
        aportes.Should().ContainSingle().Which.Frase.Should().Contain("sin histórico");
    }

    [Fact]
    public void Con_todo_a_favor_el_encaje_es_cien()
    {
        var (encaje, aportes, sinHistorico) = CalculadoraEncaje.Calcular(Contacto(), Perfil());

        encaje.Should().Be(100);
        sinHistorico.Should().BeFalse();
        aportes.Should().HaveCount(5);
    }

    [Fact]
    public void Sin_nada_a_favor_el_encaje_es_cero()
    {
        var ajeno = new DatosContacto("Minería", "Lugo", "puerta fría", 5000, false, false);

        var (encaje, aportes, _) = CalculadoraEncaje.Calcular(ajeno, Perfil());

        encaje.Should().Be(0);
        aportes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Hostelería", 30)]
    [InlineData("Minería", 0)]
    public void El_sector_pesa_treinta(string sector, int esperado)
    {
        var datos = new DatosContacto(sector, null, "puerta fría", null, false, false);

        CalculadoraEncaje.Calcular(datos, Perfil()).Encaje.Should().Be(esperado);
    }

    [Fact]
    public void La_provincia_pesa_veinte()
    {
        var datos = new DatosContacto(null, "Alicante", "puerta fría", null, false, false);

        CalculadoraEncaje.Calcular(datos, Perfil()).Encaje.Should().Be(20);
    }

    [Fact]
    public void Tener_correo_y_telefono_suma_quince_pero_tener_solo_uno_no_suma_nada()
    {
        var soloCorreo = new DatosContacto(null, null, "puerta fría", null, true, false);
        var ambos = new DatosContacto(null, null, "puerta fría", null, true, true);

        CalculadoraEncaje.Calcular(soloCorreo, Perfil()).Encaje.Should().Be(0);
        CalculadoraEncaje.Calcular(ambos, Perfil()).Encaje.Should().Be(15);
    }

    [Fact]
    public void El_tamano_solo_cuenta_si_cae_dentro_del_rango_que_sueles_cerrar()
    {
        var dentro = new DatosContacto(null, null, "puerta fría", 20, false, false);
        var fuera = new DatosContacto(null, null, "puerta fría", 900, false, false);

        CalculadoraEncaje.Calcular(dentro, Perfil()).Encaje.Should().Be(15);
        CalculadoraEncaje.Calcular(fuera, Perfil()).Encaje.Should().Be(0);
    }

    [Fact]
    public void El_sector_se_compara_sin_importar_mayusculas()
    {
        var datos = new DatosContacto("HOSTELERÍA", null, "puerta fría", null, false, false);

        CalculadoraEncaje.Calcular(datos, Perfil()).Encaje.Should().Be(30);
    }
}
