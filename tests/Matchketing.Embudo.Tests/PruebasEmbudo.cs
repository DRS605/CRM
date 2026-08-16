using FluentAssertions;
using Matchketing.Embudo.Dominio;
using Matchketing.Nucleo.Tiempo;
using Xunit;

namespace Matchketing.Embudo.Tests;

public sealed class RelojFijo(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset AhoraUtc { get; private set; } = ahora;

    public void Avanzar(TimeSpan cuanto) => AhoraUtc = AhoraUtc.Add(cuanto);
}

public sealed class PruebasEmbudo
{
    private static readonly Guid Empresa = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static RelojFijo Reloj() => new(new DateTimeOffset(2026, 8, 16, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void El_embudo_por_defecto_trae_las_cinco_etapas_en_orden()
    {
        var embudo = Dominio.Embudo.CrearPorDefecto(Empresa, Reloj());

        embudo.PorDefecto.Should().BeTrue();
        embudo.Etapas.Select(e => e.Nombre).Should()
            .Equal("Nuevo", "Contactado", "Propuesta", "Negociación", "Cierre");
        embudo.Etapas.Select(e => e.Orden).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Las_probabilidades_suben_con_la_etapa()
    {
        var embudo = Dominio.Embudo.CrearPorDefecto(Empresa, Reloj());

        embudo.Etapas.Select(e => e.Probabilidad).Should().Equal(10, 25, 50, 75, 90);
        embudo.Etapas.Select(e => e.Probabilidad).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Un_embudo_a_medida_empieza_vacio_y_no_es_el_de_por_defecto()
    {
        var r = Dominio.Embudo.Crear(Empresa, "Mantenimientos", Reloj());

        r.Exito.Should().BeTrue();
        r.Valor.PorDefecto.Should().BeFalse();
        r.Valor.Etapas.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "embudo.nombre_vacio")]
    [InlineData("   ", "embudo.nombre_vacio")]
    public void El_embudo_necesita_nombre(string? nombre, string codigo)
    {
        Dominio.Embudo.Crear(Empresa, nombre, Reloj()).Error!.Codigo.Should().Be(codigo);
    }

    [Theory]
    [InlineData(-1, 7, "etapa.probabilidad_invalida")]
    [InlineData(101, 7, "etapa.probabilidad_invalida")]
    [InlineData(50, 0, "etapa.dias_invalidos")]
    [InlineData(50, 400, "etapa.dias_invalidos")]
    public void Las_etapas_se_validan(int probabilidad, int dias, string codigo)
    {
        var embudo = Dominio.Embudo.Crear(Empresa, "Mantenimientos", Reloj()).Valor;

        embudo.AnadirEtapa("Visita", probabilidad, dias).Error!.Codigo.Should().Be(codigo);
    }
}
