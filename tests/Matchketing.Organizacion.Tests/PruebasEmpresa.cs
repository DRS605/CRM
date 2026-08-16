using FluentAssertions;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Organizacion.Dominio;
using Xunit;

namespace Matchketing.Organizacion.Tests;

file sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc => ahora;
}

public sealed class PruebasEmpresa
{
    private static readonly IReloj Reloj = new RelojFijo(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Una_empresa_nueva_nace_con_el_match_mitad_y_mitad()
    {
        var r = Empresa.Crear("Instalaciones Ribera, S.L.", "B12345678", "Valencia", Reloj);

        r.Exito.Should().BeTrue();
        r.Valor.PesoEncaje.Should().Be(0.5m);
        r.Valor.HorasRebote.Should().Be(4);
        r.Valor.Activa.Should().BeTrue();
    }

    [Fact]
    public void Crear_emite_el_evento_de_alta()
    {
        var r = Empresa.Crear("Ribera", null, null, Reloj);

        r.Valor.Eventos.Should().ContainSingle().Which.Should().BeOfType<EmpresaCreada>();
    }

    [Fact]
    public void El_nombre_es_obligatorio()
    {
        var r = Empresa.Crear("  ", null, null, Reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("empresa.nombre_vacio");
    }

    [Theory]
    [InlineData(-0.1, 4, "empresa.peso_invalido")]
    [InlineData(1.5, 4, "empresa.peso_invalido")]
    [InlineData(0.5, 0, "empresa.rebote_invalido")]
    [InlineData(0.5, 500, "empresa.rebote_invalido")]
    public void Los_ajustes_del_match_se_validan(decimal peso, int horas, string codigo)
    {
        var empresa = Empresa.Crear("Ribera", null, null, Reloj).Valor;

        var r = empresa.AjustarMatch(peso, horas, Reloj);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be(codigo);
    }

    [Fact]
    public void Los_ajustes_validos_se_guardan()
    {
        var empresa = Empresa.Crear("Ribera", null, null, Reloj).Valor;

        empresa.AjustarMatch(0.65m, 6, Reloj).Exito.Should().BeTrue();

        empresa.PesoEncaje.Should().Be(0.65m);
        empresa.HorasRebote.Should().Be(6);
    }

    [Fact]
    public void Los_campos_opcionales_en_blanco_se_guardan_como_nulos()
    {
        var r = Empresa.Crear("Ribera", "   ", "", Reloj);

        r.Valor.Nif.Should().BeNull();
        r.Valor.Provincia.Should().BeNull();
    }
}
