using FluentAssertions;
using Matchketing.Contactos.Aplicacion;
using Xunit;

namespace Matchketing.Contactos.Tests;

public sealed class PruebasLectorCsv
{
    [Theory]
    [InlineData("nombre;email;telefono", ';')]
    [InlineData("nombre,email,telefono", ',')]
    [InlineData("nombre\temail\ttelefono", '\t')]
    public void El_separador_se_detecta_solo(string cabecera, char esperado)
    {
        LectorCsv.DetectarSeparador(cabecera).Should().Be(esperado);
    }

    [Fact]
    public void Los_campos_entrecomillados_pueden_llevar_el_separador_dentro()
    {
        var campos = LectorCsv.PartirLinea("\"García, Manolo\";manolo@casa.es", ';');

        campos.Should().HaveCount(2);
        campos[0].Should().Be("García, Manolo");
    }

    [Fact]
    public void Las_comillas_dobles_dentro_de_un_campo_se_escapan_duplicandolas()
    {
        var campos = LectorCsv.PartirLinea("\"Bar \"\"El Rincón\"\"\";x@y.es", ';');

        campos[0].Should().Be("Bar \"El Rincón\"");
    }

    [Theory]
    [InlineData("Teléfono", "telefono")]
    [InlineData("  CORREO ELECTRÓNICO  ", "correo electronico")]
    [InlineData("Razón Social", "razon social")]
    public void Las_cabeceras_se_comparan_sin_acentos_ni_mayusculas(string entrada, string esperado)
    {
        LectorCsv.NormalizarCabecera(entrada).Should().Be(esperado);
    }

    [Fact]
    public void La_columna_se_encuentra_por_cualquiera_de_sus_alias_y_en_cualquier_orden()
    {
        var cabeceras = new[] { "Móvil", "Nombre completo", "Correo" };

        LectorCsv.IndiceDe(cabeceras, "nombre", "nombre completo").Should().Be(1);
        LectorCsv.IndiceDe(cabeceras, "email", "correo").Should().Be(2);
        LectorCsv.IndiceDe(cabeceras, "telefono", "movil").Should().Be(0);
        LectorCsv.IndiceDe(cabeceras, "cargo").Should().Be(-1);
    }
}
