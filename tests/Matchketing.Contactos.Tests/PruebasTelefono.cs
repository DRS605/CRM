using FluentAssertions;
using Matchketing.Nucleo.Comun;
using Xunit;

namespace Matchketing.Contactos.Tests;

public sealed class PruebasTelefono
{
    [Theory]
    [InlineData("961234567", "+34961234567")]
    [InlineData("96 123 45 67", "+34961234567")]
    [InlineData("96-123-45-67", "+34961234567")]
    [InlineData("(96) 123 45 67", "+34961234567")]
    [InlineData("+34 961 234 567", "+34961234567")]
    [InlineData("0034961234567", "+34961234567")]
    [InlineData("  961234567  ", "+34961234567")]
    public void Todas_las_formas_de_escribir_el_mismo_numero_acaban_igual(string entrada, string esperado)
    {
        var r = Telefono.Crear(entrada);

        r.Exito.Should().BeTrue();
        r.Valor.Valor.Should().Be(esperado);
    }

    [Fact]
    public void Un_numero_extranjero_conserva_su_prefijo()
    {
        Telefono.Crear("+351 912 345 678").Valor.Valor.Should().Be("+351912345678");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_telefono_vacio_se_rechaza(string? entrada)
    {
        var r = Telefono.Crear(entrada);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("telefono.vacio");
    }

    [Theory]
    [InlineData("96123")]
    [InlineData("9612345678901234567")]
    [InlineData("no-es-un-telefono")]
    public void Un_numero_imposible_se_rechaza(string entrada)
    {
        Telefono.Crear(entrada).Error!.Codigo.Should().Be("telefono.invalido");
    }
}
