using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoEmbudo(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync(string nombreEmpresa)
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"e{Guid.NewGuid():N}@ribera.es",
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

    private static async Task<Guid> ContactoAsync(HttpClient cliente, string nombre)
    {
        var r = await cliente.PostAsJsonAsync("/contactos", new { nombre, email = $"{Guid.NewGuid():N}@ribera.es" });
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> OportunidadAsync(HttpClient cliente, Guid contactoId, string titulo, decimal importe)
    {
        var r = await cliente.PostAsJsonAsync("/oportunidades", new { contactoId, titulo, importe });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> TableroAsync(HttpClient cliente) =>
        await LeerAsync(await cliente.GetAsync(new Uri("/embudo/tablero", UriKind.Relative)));

    [Fact]
    public async Task Una_empresa_nueva_ya_trae_su_embudo_de_cinco_etapas()
    {
        var cliente = await EnEmpresaAsync("Ribera Embudo");

        var tablero = await TableroAsync(cliente);

        tablero.GetProperty("nombre").GetString().Should().Be("Embudo comercial");
        tablero.GetProperty("columnas").GetArrayLength().Should().Be(5);
        tablero.GetProperty("columnas")[0].GetProperty("nombre").GetString().Should().Be("Nuevo");
        tablero.GetProperty("columnas")[4].GetProperty("probabilidad").GetInt32().Should().Be(90);
    }

    [Fact]
    public async Task Una_oportunidad_nueva_cae_en_la_primera_columna_y_suma()
    {
        var cliente = await EnEmpresaAsync("Ribera Alta");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        await OportunidadAsync(cliente, contacto, "Cámara frigorífica", 14280m);

        var tablero = await TableroAsync(cliente);
        var primera = tablero.GetProperty("columnas")[0];

        primera.GetProperty("cuantas").GetInt32().Should().Be(1);
        primera.GetProperty("importe").GetDecimal().Should().Be(14280m);
        tablero.GetProperty("importeAbierto").GetDecimal().Should().Be(14280m);
    }

    [Fact]
    public async Task La_prevision_pondera_cada_columna_por_su_probabilidad()
    {
        var cliente = await EnEmpresaAsync("Ribera Previsión");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var uno = await OportunidadAsync(cliente, contacto, "En Nuevo", 1000m);
        var dos = await OportunidadAsync(cliente, contacto, "En Propuesta", 2000m);

        var tablero = await TableroAsync(cliente);
        var propuesta = tablero.GetProperty("columnas")[2].GetProperty("etapaId").GetGuid();
        await cliente.PostAsJsonAsync($"/oportunidades/{dos}/mover", new { etapaId = propuesta });

        var despues = await TableroAsync(cliente);

        // 1.000 × 10 % + 2.000 × 50 % = 1.100
        despues.GetProperty("previsionPonderada").GetDecimal().Should().Be(1100m);
        uno.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Mover_cambia_de_columna_y_reinicia_los_dias_en_etapa()
    {
        var cliente = await EnEmpresaAsync("Ribera Mover");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var id = await OportunidadAsync(cliente, contacto, "Cámara", 1000m);

        var tablero = await TableroAsync(cliente);
        var negociacion = tablero.GetProperty("columnas")[3].GetProperty("etapaId").GetGuid();

        var r = await cliente.PostAsJsonAsync($"/oportunidades/{id}/mover", new { etapaId = negociacion });
        r.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var despues = await TableroAsync(cliente);
        despues.GetProperty("columnas")[0].GetProperty("cuantas").GetInt32().Should().Be(0);
        despues.GetProperty("columnas")[3].GetProperty("cuantas").GetInt32().Should().Be(1);
        despues.GetProperty("columnas")[3].GetProperty("oportunidades")[0].GetProperty("diasEnEtapa").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Perder_sin_motivo_se_rechaza()
    {
        var cliente = await EnEmpresaAsync("Ribera Sin Motivo");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var id = await OportunidadAsync(cliente, contacto, "Cámara", 1000m);

        var r = await cliente.PostAsJsonAsync($"/oportunidades/{id}/perder", new { motivo = (int?)null, detalle = "Pues eso" });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("oportunidad.sin_motivo");
    }

    [Fact]
    public async Task Ganar_marca_al_contacto_como_cliente_y_lo_deja_escrito_en_su_cronologia()
    {
        var cliente = await EnEmpresaAsync("Ribera Ganar");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var id = await OportunidadAsync(cliente, contacto, "Cámara frigorífica", 14280m);

        var r = await cliente.PostAsync(new Uri($"/oportunidades/{id}/ganar", UriKind.Relative), null);
        r.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{contacto}", UriKind.Relative)));

        ficha.GetProperty("contacto").GetProperty("estado").GetInt32().Should().Be(2, "quien compra deja de ser un lead");
        var textos = ficha.GetProperty("cronologia").EnumerateArray()
            .Select(a => a.GetProperty("cuerpo").GetString()!).ToList();
        textos.Should().Contain(t => t.Contains("ganada", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Una_oportunidad_ganada_ya_no_se_puede_perder()
    {
        var cliente = await EnEmpresaAsync("Ribera Cerrada");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var id = await OportunidadAsync(cliente, contacto, "Cámara", 1000m);
        await cliente.PostAsync(new Uri($"/oportunidades/{id}/ganar", UriKind.Relative), null);

        var r = await cliente.PostAsJsonAsync($"/oportunidades/{id}/perder", new { motivo = 1, detalle = (string?)null });

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("oportunidad.ya_cerrada");
    }

    [Fact]
    public async Task Una_empresa_no_ve_el_embudo_de_otra()
    {
        var unaEmpresa = await EnEmpresaAsync("Ribera Aislada A");
        var otraEmpresa = await EnEmpresaAsync("Ribera Aislada B");

        var contacto = await ContactoAsync(unaEmpresa, "Secreto");
        await OportunidadAsync(unaEmpresa, contacto, "Venta secreta", 99999m);

        var tablero = await TableroAsync(otraEmpresa);

        tablero.GetProperty("totalAbiertas").GetInt32().Should().Be(0);
        tablero.GetProperty("importeAbierto").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task No_se_puede_mover_una_oportunidad_a_una_etapa_de_otra_empresa()
    {
        var unaEmpresa = await EnEmpresaAsync("Ribera Etapa A");
        var otraEmpresa = await EnEmpresaAsync("Ribera Etapa B");

        var contacto = await ContactoAsync(unaEmpresa, "Manolo");
        var id = await OportunidadAsync(unaEmpresa, contacto, "Cámara", 1000m);

        var etapaAjena = (await TableroAsync(otraEmpresa)).GetProperty("columnas")[2].GetProperty("etapaId").GetGuid();

        var r = await unaEmpresa.PostAsJsonAsync($"/oportunidades/{id}/mover", new { etapaId = etapaAjena });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("oportunidad.etapa_invalida");
    }

    [Fact]
    public async Task Las_politicas_de_RLS_tambien_estan_en_el_embudo()
    {
        using var alcance = api.Services.CreateAsyncScope();
        var bd = alcance.ServiceProvider.GetRequiredService<Persistencia.ContextoMatchketing>();

        var conexion = bd.Database.GetDbConnection();
        await conexion.OpenAsync();
        await using var orden = conexion.CreateCommand();
        orden.CommandText = """
            SELECT count(*) FROM pg_policies
            WHERE schemaname = 'embudo' AND policyname = 'aislamiento_empresa'
            """;
        var politicas = Convert.ToInt32(await orden.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);

        politicas.Should().Be(2, "embudo y oportunidad; etapa se aísla por su clave ajena");
    }
}
