using FluentAssertions;
using Matchketing.Informes.Aplicacion;
using Xunit;

namespace Matchketing.Informes.Tests;

public sealed class PruebasPeriodo
{
    private static readonly DateOnly Hoy = new(2026, 8, 16);

    [Fact]
    public void Sin_fechas_el_periodo_es_todo_lo_que_hay()
    {
        Periodo.Todo.Desde.Should().BeNull();
        Periodo.Todo.Hasta.Should().BeNull();
        Periodo.Todo.Descripcion.Should().Be("desde el principio");
    }

    [Fact]
    public void Los_ultimos_treinta_dias_incluyen_hoy()
    {
        var p = Periodo.UltimosDias(30, Hoy);

        p.Hasta.Should().Be(Hoy);
        p.Desde.Should().Be(new DateOnly(2026, 7, 18));
        (p.Hasta!.Value.DayNumber - p.Desde!.Value.DayNumber + 1).Should().Be(30);
    }

    [Theory]
    [InlineData(null, null, "desde el principio")]
    [InlineData("2026-08-01", null, "desde el 01/08/2026")]
    [InlineData(null, "2026-08-31", "hasta el 31/08/2026")]
    [InlineData("2026-08-01", "2026-08-31", "del 01/08/2026 al 31/08/2026")]
    public void El_periodo_se_describe_en_castellano(string? desde, string? hasta, string esperado)
    {
        var p = new Periodo(
            desde is null ? null : DateOnly.Parse(desde, System.Globalization.CultureInfo.InvariantCulture),
            hasta is null ? null : DateOnly.Parse(hasta, System.Globalization.CultureInfo.InvariantCulture));

        p.Descripcion.Should().Be(esperado);
    }
}
