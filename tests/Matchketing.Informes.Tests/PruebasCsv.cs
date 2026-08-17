using FluentAssertions;
using Matchketing.Informes.Aplicacion;
using Xunit;

namespace Matchketing.Informes.Tests;

/// <summary>Consulta falsa: aquí solo se comprueba cómo se escribe el CSV, no de dónde salen los datos.</summary>
file sealed class ConsultaFalsa(InformeEmbudo? embudo = null, InformeMotivos? motivos = null) : IConsultaInformes
{
    public Task<InformeEmbudo> EmbudoAsync(Periodo periodo, CancellationToken ct = default) =>
        Task.FromResult(embudo ?? new InformeEmbudo("desde el principio", [], 0, 0, 0, 0, 0, 0, 0, null, null, null));

    public Task<InformeMotivos> MotivosAsync(Periodo periodo, CancellationToken ct = default) =>
        Task.FromResult(motivos ?? new InformeMotivos("desde el principio", [], 0, 0, 0, 0));
}

public sealed class PruebasCsv
{
    [Fact]
    public async Task El_csv_del_embudo_usa_punto_y_coma_y_decimales_con_coma()
    {
        // Con separador de comas, Excel en español mete la fila entera en la primera celda y el
        // cliente cree que el programa está roto.
        var informe = new InformeEmbudo(
            "del 01/08/2026 al 31/08/2026",
            [new EtapaEmbudo("Propuesta", 3, 50, 2, 23880.50m, 5, 40.0m)],
            2, 23880.50m, 11940.25m, 1, 2900m, 3, 8100m, 25.0m, 2900m, 12.5m);

        var csv = await new ServicioInformes(new ConsultaFalsa(embudo: informe)).EmbudoCsvAsync(Periodo.Todo);

        csv.Should().Contain("Etapa;Probabilidad;Abiertas");
        csv.Should().Contain("Propuesta;50;2;23880,50;5;40,00");
        csv.Should().NotContain("23880.50", "los decimales van con coma");
    }

    [Fact]
    public async Task El_csv_lleva_al_pie_los_totales_del_periodo()
    {
        var informe = new InformeEmbudo(
            "del 01/08/2026 al 31/08/2026", [], 2, 23880.50m, 11940.25m, 1, 2900m, 3, 8100m, 25.0m, 2900m, 12.5m);

        var csv = await new ServicioInformes(new ConsultaFalsa(embudo: informe)).EmbudoCsvAsync(Periodo.Todo);

        csv.Should().Contain("Periodo;del 01/08/2026 al 31/08/2026");
        csv.Should().Contain("Previsión ponderada;11940,25");
        csv.Should().Contain("Tasa de cierre;25,00");
        csv.Should().Contain("Días medios para cerrar;12,50");
    }

    [Fact]
    public async Task Un_dato_que_no_se_puede_calcular_sale_vacio_no_como_cero()
    {
        // Sin cierres no hay tasa ni ticket medio. Poner 0 sería mentir: 0 % de cierre y «todavía no
        // se sabe» son cosas distintas.
        var informe = new InformeEmbudo("desde el principio", [], 0, 0, 0, 0, 0, 0, 0, null, null, null);

        var csv = await new ServicioInformes(new ConsultaFalsa(embudo: informe)).EmbudoCsvAsync(Periodo.Todo);

        csv.Should().Contain("Tasa de cierre;\n").And.NotContain("Tasa de cierre;0,00");
    }

    [Fact]
    public async Task El_csv_de_motivos_ordena_como_el_informe_y_lleva_porcentaje()
    {
        var informe = new InformeMotivos(
            "desde el principio",
            [new MotivoPerdidaConteo("Precio", 2, 5900m, 66.7m), new MotivoPerdidaConteo("Competencia", 1, 2200m, 33.3m)],
            3, 8100m, 1, 2900m);

        var csv = await new ServicioInformes(new ConsultaFalsa(motivos: informe)).MotivosCsvAsync(Periodo.Todo);

        var lineas = csv.Split('\n');
        lineas[0].Should().Be("Motivo;Cuántas;Importe;Porcentaje");
        lineas[1].Should().Be("Precio;2;5900,00;66,70");
        lineas[2].Should().Be("Competencia;1;2200,00;33,30");
    }

    [Fact]
    public async Task Un_motivo_con_punto_y_coma_dentro_se_entrecomilla()
    {
        var informe = new InformeMotivos(
            "desde el principio",
            [new MotivoPerdidaConteo("Precio; y plazo", 1, 100m, 100m)],
            1, 100m, 0, 0);

        var csv = await new ServicioInformes(new ConsultaFalsa(motivos: informe)).MotivosCsvAsync(Periodo.Todo);

        csv.Should().Contain("\"Precio; y plazo\";1;100,00");
    }
}
