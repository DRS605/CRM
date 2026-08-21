using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoInformes(ApiDePrueba api)
{
    /// <summary>
    /// Hoy, contado **como lo cuenta la aplicación**: el día en hora española.
    ///
    /// Con `DateTime.UtcNow` estas pruebas fallaban entre medianoche y las dos de la mañana de aquí,
    /// que es cuando UTC va todavía en el día anterior. No era una prueba frágil: era la prueba
    /// avisando de que el producto tenía dos calendarios. Ahora hay uno, y las pruebas usan ese.
    /// </summary>
    private static DateOnly Hoy => HorasLaborables.DiaDeTrabajo(DateTimeOffset.UtcNow);
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync(string nombreEmpresa)
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"i{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        var empresa = await cliente.PostAsJsonAsync("/empresas", new { nombre = nombreEmpresa, provincia = "Valencia" });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(empresa)).GetProperty("token").GetString());
        return cliente;
    }

    private static async Task<Guid> ContactoAsync(HttpClient cliente) =>
        (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García", email = $"{Guid.NewGuid():N}@ribera.es",
        }))).GetProperty("id").GetGuid();

    private static async Task<Guid> OportunidadAsync(HttpClient cliente, Guid contacto, decimal importe)
    {
        var r = await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto, titulo = "Cámara frigorífica", importe,
        });
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task El_informe_de_embudo_de_una_empresa_nueva_no_inventa_ratios()
    {
        var cliente = await EnEmpresaAsync("Ribera Informe Vacío");

        var i = await LeerAsync(await cliente.GetAsync(new Uri("/informes/embudo", UriKind.Relative)));

        i.GetProperty("etapas").GetArrayLength().Should().Be(5);
        i.GetProperty("abiertas").GetInt32().Should().Be(0);

        // Sin cierres, «no se sabe» — no 0 %. Poner cero sería mentir.
        i.GetProperty("tasaCierre").ValueKind.Should().Be(JsonValueKind.Null);
        i.GetProperty("ticketMedio").ValueKind.Should().Be(JsonValueKind.Null);
        i.GetProperty("diasMediosParaCerrar").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task El_informe_de_embudo_reparte_por_etapa_y_pondera_la_prevision()
    {
        var cliente = await EnEmpresaAsync("Ribera Informe Embudo");
        var contacto = await ContactoAsync(cliente);
        await OportunidadAsync(cliente, contacto, 1000m);
        var segunda = await OportunidadAsync(cliente, contacto, 2000m);

        var tablero = await LeerAsync(await cliente.GetAsync(new Uri("/embudo/tablero", UriKind.Relative)));
        var propuesta = tablero.GetProperty("columnas")[2].GetProperty("etapaId").GetGuid();
        await cliente.PostAsJsonAsync($"/oportunidades/{segunda}/mover", new { etapaId = propuesta });

        var i = await LeerAsync(await cliente.GetAsync(new Uri("/informes/embudo", UriKind.Relative)));

        i.GetProperty("abiertas").GetInt32().Should().Be(2);
        i.GetProperty("importeAbierto").GetDecimal().Should().Be(3000m);
        i.GetProperty("previsionPonderada").GetDecimal().Should().Be(1100m, "1.000 × 10 % + 2.000 × 50 %");
        i.GetProperty("etapas")[0].GetProperty("abiertas").GetInt32().Should().Be(1);
        i.GetProperty("etapas")[2].GetProperty("importeAbierto").GetDecimal().Should().Be(2000m);
    }

    [Fact]
    public async Task El_informe_calcula_tasa_de_cierre_y_ticket_medio()
    {
        var cliente = await EnEmpresaAsync("Ribera Ratios");
        var contacto = await ContactoAsync(cliente);

        foreach (var importe in new[] { 3000m, 5000m })
        {
            var id = await OportunidadAsync(cliente, contacto, importe);
            await cliente.PostAsync(new Uri($"/oportunidades/{id}/ganar", UriKind.Relative), null);
        }

        var perdida = await OportunidadAsync(cliente, contacto, 1000m);
        await cliente.PostAsJsonAsync($"/oportunidades/{perdida}/perder", new { motivo = 1, detalle = (string?)null });

        var i = await LeerAsync(await cliente.GetAsync(new Uri("/informes/embudo", UriKind.Relative)));

        i.GetProperty("ganadas").GetInt32().Should().Be(2);
        i.GetProperty("perdidas").GetInt32().Should().Be(1);
        i.GetProperty("tasaCierre").GetDecimal().Should().Be(66.7m, "2 de 3");
        i.GetProperty("ticketMedio").GetDecimal().Should().Be(4000m, "(3.000 + 5.000) / 2");
        i.GetProperty("importeGanado").GetDecimal().Should().Be(8000m);
    }

    [Fact]
    public async Task El_informe_de_motivos_ordena_por_el_que_mas_duele_y_da_porcentajes()
    {
        var cliente = await EnEmpresaAsync("Ribera Motivos Informe");
        var contacto = await ContactoAsync(cliente);

        foreach (var motivo in new[] { 1, 1, 3 })
        {
            var id = await OportunidadAsync(cliente, contacto, 1000m);
            await cliente.PostAsJsonAsync($"/oportunidades/{id}/perder", new { motivo, detalle = (string?)null });
        }

        var ganada = await OportunidadAsync(cliente, contacto, 5000m);
        await cliente.PostAsync(new Uri($"/oportunidades/{ganada}/ganar", UriKind.Relative), null);

        var i = await LeerAsync(await cliente.GetAsync(new Uri("/informes/motivos-perdida", UriKind.Relative)));

        i.GetProperty("totalPerdidas").GetInt32().Should().Be(3);
        i.GetProperty("totalGanadas").GetInt32().Should().Be(1);
        i.GetProperty("importeGanado").GetDecimal().Should().Be(5000m);
        i.GetProperty("motivos")[0].GetProperty("motivo").GetString().Should().Be("Precio");
        i.GetProperty("motivos")[0].GetProperty("cuantas").GetInt32().Should().Be(2);
        i.GetProperty("motivos")[0].GetProperty("porcentaje").GetDecimal().Should().Be(66.7m);
    }

    [Fact]
    public async Task La_conversion_sale_del_historico_real_de_movimientos()
    {
        var cliente = await EnEmpresaAsync("Ribera Conversión");
        var contacto = await ContactoAsync(cliente);

        var tablero = await LeerAsync(await cliente.GetAsync(new Uri("/embudo/tablero", UriKind.Relative)));
        var contactado = tablero.GetProperty("columnas")[1].GetProperty("etapaId").GetGuid();
        var propuesta = tablero.GetProperty("columnas")[2].GetProperty("etapaId").GetGuid();

        // Tres nacen en «Nuevo»; dos llegan a «Contactado»; una sigue a «Propuesta».
        var uno = await OportunidadAsync(cliente, contacto, 1000m);
        var dos = await OportunidadAsync(cliente, contacto, 2000m);
        await OportunidadAsync(cliente, contacto, 3000m);

        await cliente.PostAsJsonAsync($"/oportunidades/{uno}/mover", new { etapaId = contactado });
        await cliente.PostAsJsonAsync($"/oportunidades/{dos}/mover", new { etapaId = contactado });
        await cliente.PostAsJsonAsync($"/oportunidades/{dos}/mover", new { etapaId = propuesta });

        var i = await LeerAsync(await cliente.GetAsync(new Uri("/informes/embudo", UriKind.Relative)));
        var etapas = i.GetProperty("etapas");

        etapas[0].GetProperty("hanLlegado").GetInt32().Should().Be(3, "las tres nacieron en Nuevo");
        etapas[1].GetProperty("hanLlegado").GetInt32().Should().Be(2);
        etapas[2].GetProperty("hanLlegado").GetInt32().Should().Be(1);

        etapas[0].GetProperty("conversionALaSiguiente").GetDecimal().Should().Be(66.7m, "2 de 3");
        etapas[1].GetProperty("conversionALaSiguiente").GetDecimal().Should().Be(50.0m, "1 de 2");
        etapas[3].GetProperty("conversionALaSiguiente").ValueKind.Should().Be(JsonValueKind.Null,
            "nadie llegó a Negociación, así que no hay porcentaje que dar");
    }

    [Fact]
    public async Task Una_oportunidad_que_se_queda_donde_esta_no_infla_la_conversion()
    {
        // El fallo que teníamos: dar por hecho que todo lo cerrado pasó por todas las etapas hacía
        // que el informe enseñara «100 % pasa a propuesta» sin que nada se hubiera movido.
        var cliente = await EnEmpresaAsync("Ribera Sin Mover");
        var contacto = await ContactoAsync(cliente);
        var id = await OportunidadAsync(cliente, contacto, 1000m);
        await cliente.PostAsJsonAsync($"/oportunidades/{id}/perder", new { motivo = 5, detalle = (string?)null });

        var etapas = (await LeerAsync(await cliente.GetAsync(new Uri("/informes/embudo", UriKind.Relative)))).GetProperty("etapas");

        etapas[0].GetProperty("hanLlegado").GetInt32().Should().Be(1);
        etapas[1].GetProperty("hanLlegado").GetInt32().Should().Be(0);
        etapas[0].GetProperty("conversionALaSiguiente").GetDecimal().Should().Be(0m, "se cayó en Nuevo");
    }

    [Fact]
    public async Task Saltarse_una_etapa_no_hace_que_la_conversion_pase_del_cien_por_cien()
    {
        // El fallo que se veía en la pantalla: «↓ 200 % pasa a propuesta».
        //
        // El tablero deja arrastrar una oportunidad de «Nuevo» a «Propuesta» sin pasar por
        // «Contactado», y eso es correcto: a veces una venta se salta un paso. Lo que estaba mal era
        // contar «cuántas estuvieron en esta etapa», porque entonces una etapa de más adelante podía
        // tener más oportunidades que la de antes y el porcentaje se salía de la escala.
        //
        // Un embudo con una conversión por encima del 100 % no es un dato raro, es un dato falso: quien
        // lo lee una vez deja de creerse el informe entero.
        var cliente = await EnEmpresaAsync("Ribera Salto");
        var contacto = await ContactoAsync(cliente);

        var tablero = await LeerAsync(await cliente.GetAsync(new Uri("/embudo/tablero", UriKind.Relative)));
        var contactado = tablero.GetProperty("columnas")[1].GetProperty("etapaId").GetGuid();
        var propuesta = tablero.GetProperty("columnas")[2].GetProperty("etapaId").GetGuid();

        // Tres nacen en «Nuevo». Una se queda en «Contactado» y **dos saltan directas a «Propuesta»**
        // sin pisar «Contactado». Es exactamente la forma que producía el «200 %»: contando quién
        // estuvo en cada etapa salían 1 en Contactado y 2 en Propuesta, y 2 de 1 es el 200 %.
        var quieta = await OportunidadAsync(cliente, contacto, 1000m);
        (await cliente.PostAsJsonAsync($"/oportunidades/{quieta}/mover", new { etapaId = contactado }))
            .IsSuccessStatusCode.Should().BeTrue();

        foreach (var _ in new[] { 1, 2 })
        {
            var id = await OportunidadAsync(cliente, contacto, 1000m);
            (await cliente.PostAsJsonAsync($"/oportunidades/{id}/mover", new { etapaId = propuesta }))
                .IsSuccessStatusCode.Should().BeTrue();
        }

        var etapas = (await LeerAsync(await cliente.GetAsync(
            new Uri("/informes/embudo", UriKind.Relative)))).GetProperty("etapas");

        // «Han llegado» es decreciente por construcción: quien llegó a Propuesta la dejó atrás,
        // pasando o no por Contactado.
        etapas[0].GetProperty("hanLlegado").GetInt32().Should().Be(3);
        etapas[1].GetProperty("hanLlegado").GetInt32().Should().Be(3, "se saltaron Contactado, pero lo dejaron atrás");
        etapas[2].GetProperty("hanLlegado").GetInt32().Should().Be(2);
        etapas[3].GetProperty("hanLlegado").GetInt32().Should().Be(0);

        // Ninguna conversión pasa del 100 %, que es la afirmación que importa, y la serie no crece.
        var anterior = int.MaxValue;
        foreach (var e in etapas.EnumerateArray())
        {
            e.GetProperty("hanLlegado").GetInt32().Should().BeLessThanOrEqualTo(anterior,
                "un embudo no puede ensanchar por el camino");
            anterior = e.GetProperty("hanLlegado").GetInt32();

            if (e.GetProperty("conversionALaSiguiente").ValueKind != JsonValueKind.Null)
            {
                e.GetProperty("conversionALaSiguiente").GetDecimal()
                    .Should().BeLessThanOrEqualTo(100m);
            }
        }

        etapas[0].GetProperty("conversionALaSiguiente").GetDecimal().Should().Be(100.0m);
        etapas[1].GetProperty("conversionALaSiguiente").GetDecimal().Should().Be(66.7m, "2 de 3");
        etapas[2].GetProperty("conversionALaSiguiente").GetDecimal().Should().Be(0m, "nadie llegó a Negociación");
    }

    [Fact]
    public async Task El_periodo_recorta_lo_que_entra_en_el_informe()
    {
        var cliente = await EnEmpresaAsync("Ribera Periodo");
        var contacto = await ContactoAsync(cliente);
        var id = await OportunidadAsync(cliente, contacto, 4000m);
        await cliente.PostAsync(new Uri($"/oportunidades/{id}/ganar", UriKind.Relative), null);

        var hoy = Hoy;

        var conHoy = await LeerAsync(await cliente.GetAsync(new Uri($"/informes/embudo?desde={hoy:yyyy-MM-dd}", UriKind.Relative)));
        conHoy.GetProperty("ganadas").GetInt32().Should().Be(1);

        var soloElMesPasado = await LeerAsync(await cliente.GetAsync(
            new Uri($"/informes/embudo?desde={hoy.AddDays(-40):yyyy-MM-dd}&hasta={hoy.AddDays(-10):yyyy-MM-dd}", UriKind.Relative)));
        soloElMesPasado.GetProperty("ganadas").GetInt32().Should().Be(0, "se cerró hoy, no el mes pasado");
    }

    [Fact]
    public async Task El_atajo_de_periodo_mes_dice_de_que_fechas_habla()
    {
        var cliente = await EnEmpresaAsync("Ribera Atajo");

        var i = await LeerAsync(await cliente.GetAsync(new Uri("/informes/embudo?periodo=mes", UriKind.Relative)));

        i.GetProperty("periodo").GetString().Should().StartWith("del ").And.Contain(" al ");
    }

    [Fact]
    public async Task El_csv_sale_con_BOM_para_que_Excel_no_se_coma_los_acentos()
    {
        var cliente = await EnEmpresaAsync("Ribera CSV");
        var contacto = await ContactoAsync(cliente);
        await OportunidadAsync(cliente, contacto, 1500m);

        var r = await cliente.GetAsync(new Uri("/informes/embudo.csv", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var bytes = await r.Content.ReadAsByteArrayAsync();
        bytes[..3].Should().Equal([0xEF, 0xBB, 0xBF], "sin BOM, Excel en Windows enseña «Hosteler¡a»");

        var texto = System.Text.Encoding.UTF8.GetString(bytes);
        texto.Should().Contain("Etapa;Probabilidad");
        texto.Should().Contain("Negociación", "los acentos van intactos");
    }

    [Fact]
    public async Task El_csv_de_motivos_tambien_se_descarga()
    {
        var cliente = await EnEmpresaAsync("Ribera CSV Motivos");
        var contacto = await ContactoAsync(cliente);
        var id = await OportunidadAsync(cliente, contacto, 1000m);
        await cliente.PostAsJsonAsync($"/oportunidades/{id}/perder", new { motivo = 3, detalle = (string?)null });

        var r = await cliente.GetAsync(new Uri("/informes/motivos-perdida.csv", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Competencia;1;1000,00;100,00");
    }

    [Fact]
    public async Task Una_empresa_no_ve_los_numeros_de_otra()
    {
        var unaEmpresa = await EnEmpresaAsync("Ribera Informe A");
        var otraEmpresa = await EnEmpresaAsync("Ribera Informe B");

        var contacto = await ContactoAsync(unaEmpresa);
        var id = await OportunidadAsync(unaEmpresa, contacto, 99999m);
        await unaEmpresa.PostAsync(new Uri($"/oportunidades/{id}/ganar", UriKind.Relative), null);

        var i = await LeerAsync(await otraEmpresa.GetAsync(new Uri("/informes/embudo", UriKind.Relative)));

        i.GetProperty("ganadas").GetInt32().Should().Be(0);
        i.GetProperty("importeGanado").GetDecimal().Should().Be(0m);
    }
}
