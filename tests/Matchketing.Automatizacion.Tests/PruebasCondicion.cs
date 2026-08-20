using FluentAssertions;
using Matchketing.Automatizacion.Dominio;
using Xunit;

namespace Matchketing.Automatizacion.Tests;

public sealed class PruebasCondicion
{
    private static readonly Hechos Manolo = new("Valencia", "feria", "Hostelería", 18400m, null);

    [Fact]
    public void Es_compara_sin_distinguir_mayusculas()
    {
        // Quien escribe «valencia» en una regla quiere decir Valencia. Una regla que no dispara por una
        // mayúscula es una tarde perdida buscando por qué.
        new Condicion(Campo.Provincia, Operador.Es, "valencia").Cumple(Manolo).Should().BeTrue();
        new Condicion(Campo.Provincia, Operador.Es, "  VALENCIA  ").Cumple(Manolo).Should().BeTrue();
        new Condicion(Campo.Provincia, Operador.Es, "Alicante").Cumple(Manolo).Should().BeFalse();
    }

    [Fact]
    public void No_es_se_cumple_cuando_el_dato_falta()
    {
        var sinSector = Manolo with { Sector = null };

        // Es lo que se espera: «si el sector no es hostelería» tiene que incluir a quien no tiene sector
        // puesto. Lo contrario —que un hueco no cumpla nada— dejaría fuera justo a los contactos a medio
        // rellenar, que son la mayoría.
        new Condicion(Campo.Sector, Operador.NoEs, "Hostelería").Cumple(sinSector).Should().BeTrue();
        new Condicion(Campo.Sector, Operador.NoEs, "Hostelería").Cumple(Manolo).Should().BeFalse();
    }

    [Fact]
    public void Contiene_busca_dentro_y_un_hueco_no_contiene_nada()
    {
        new Condicion(Campo.Origen, Operador.Contiene, "fer").Cumple(Manolo).Should().BeTrue();
        new Condicion(Campo.Origen, Operador.Contiene, "web").Cumple(Manolo).Should().BeFalse();
        new Condicion(Campo.Origen, Operador.Contiene, "fer").Cumple(Manolo with { Origen = null }).Should().BeFalse();
    }

    [Theory]
    [InlineData(Operador.MayorQue, "10000", true)]
    [InlineData(Operador.MayorQue, "20000", false)]
    [InlineData(Operador.MenorQue, "20000", true)]
    [InlineData(Operador.MenorQue, "10000", false)]
    public void El_importe_se_compara_por_tamano(Operador operador, string valor, bool esperado) =>
        new Condicion(Campo.Importe, operador, valor).Cumple(Manolo).Should().Be(esperado);

    [Fact]
    public void Sin_importe_una_comparacion_de_tamano_no_se_cumple()
    {
        // Un disparador de contacto no trae importe. La condición no revienta: simplemente no se cumple.
        // Y guardarla ya se había impedido antes; esto es la red por si acaso.
        new Condicion(Campo.Importe, Operador.MayorQue, "1")
            .Cumple(Manolo with { Importe = null }).Should().BeFalse();
    }

    [Fact]
    public void Se_lee_en_castellano()
    {
        new Condicion(Campo.Provincia, Operador.Es, "Valencia").Leer().Should().Be("provincia es «Valencia»");
        new Condicion(Campo.Importe, Operador.MayorQue, "10000").Leer()
            .Should().Be("importe es mayor que «10000»");
    }

    // ---------- Validar: que no se pueda guardar una regla que no se cumple nunca ----------

    [Fact]
    public void Una_condicion_sin_valor_se_rechaza() =>
        new Condicion(Campo.Provincia, Operador.Es, "  ").Validar()
            .Error!.Codigo.Should().Be("regla.condicion_sin_valor");

    [Fact]
    public void Comparar_por_tamano_algo_que_no_es_una_cifra_se_rechaza() =>
        new Condicion(Campo.Importe, Operador.MayorQue, "mucho").Validar()
            .Error!.Codigo.Should().Be("regla.condicion_no_numerica");

    [Fact]
    public void Provincia_mayor_que_no_significa_nada()
    {
        var r = new Condicion(Campo.Provincia, Operador.MayorQue, "5").Validar();

        // Se rechaza al guardar y no al disparar, porque al disparar nadie lo estaría mirando: la regla
        // simplemente no haría nada y no habría forma de saber por qué.
        r.Error!.Codigo.Should().Be("regla.condicion_incoherente");
        r.Error.Mensaje.Should().Contain("importe");
    }

    [Fact]
    public void Un_importe_no_contiene_nada() =>
        new Condicion(Campo.Importe, Operador.Contiene, "1").Validar()
            .Error!.Codigo.Should().Be("regla.condicion_incoherente");

    [Fact]
    public void Las_condiciones_normales_pasan()
    {
        new Condicion(Campo.Provincia, Operador.Es, "Valencia").Validar().Exito.Should().BeTrue();
        new Condicion(Campo.Importe, Operador.MayorQue, "10000").Validar().Exito.Should().BeTrue();
        new Condicion(Campo.Origen, Operador.Contiene, "web").Validar().Exito.Should().BeTrue();
    }
}
