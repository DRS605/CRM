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
public sealed class PruebasFlujoMatch(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync(string nombreEmpresa)
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"m{Guid.NewGuid():N}@ribera.es",
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

    private static async Task<Guid> ContactoAsync(HttpClient cliente, string nombre, string origen = "feria")
    {
        var r = await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre,
            email = $"{Guid.NewGuid():N}@ribera.es",
            telefono = "961234567",
            origen,
        });
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> MatchAsync(HttpClient cliente, Guid contacto) =>
        await LeerAsync(await cliente.GetAsync(new Uri($"/match/contactos/{contacto}", UriKind.Relative)));

    [Fact]
    public async Task Un_contacto_nuevo_sin_historico_lo_dice_en_vez_de_inventarse_un_numero()
    {
        var cliente = await EnEmpresaAsync("Ribera Match Nuevo");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        var m = await MatchAsync(cliente, contacto);

        m.GetProperty("sinHistorico").GetBoolean().Should().BeTrue();
        m.GetProperty("encaje").GetInt32().Should().Be(50, "el encaje neutro mientras no hay datos");
        m.GetProperty("explicacion").GetString().Should().Contain("sin histórico");
    }

    [Fact]
    public async Task Coger_el_telefono_deja_señal_y_sube_el_momento()
    {
        var cliente = await EnEmpresaAsync("Ribera Señal");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        (await MatchAsync(cliente, contacto)).GetProperty("momento").GetInt32().Should().Be(0);

        await cliente.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 1, nota = (string?)null });

        var m = await MatchAsync(cliente, contacto);
        m.GetProperty("momento").GetInt32().Should().Be(25, "una llamada contestada pesa 25");
        m.GetProperty("motivos").EnumerateArray().Select(x => x.GetString())
            .Should().Contain(x => x!.Contains("Cogió el teléfono", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_coger_el_telefono_no_es_señal_de_interes()
    {
        var cliente = await EnEmpresaAsync("Ribera No Contesta");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        await cliente.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 2, nota = (string?)null });

        (await MatchAsync(cliente, contacto)).GetProperty("momento").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Abrir_una_oportunidad_tambien_es_señal()
    {
        var cliente = await EnEmpresaAsync("Ribera Señal Oportunidad");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        await cliente.PostAsJsonAsync("/oportunidades", new { contactoId = contacto, titulo = "Cámara", importe = 1000m });

        (await MatchAsync(cliente, contacto)).GetProperty("momento").GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task El_numero_solo_aparece_cuando_hay_algo_que_contar()
    {
        var cliente = await EnEmpresaAsync("Ribera Sin Motivos");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        var sinNada = await MatchAsync(cliente, contacto);
        sinNada.GetProperty("match").ValueKind.Should().Be(JsonValueKind.Null, "sin motivos no hay número");

        await cliente.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 1, nota = (string?)null });

        var conSeñal = await MatchAsync(cliente, contacto);
        conSeñal.GetProperty("match").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task El_reparto_propone_comercial_y_explica_por_que()
    {
        var cliente = await EnEmpresaAsync("Ribera Reparto");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        var propuesta = await LeerAsync(await cliente.GetAsync(new Uri($"/match/contactos/{contacto}/comercial", UriKind.Relative)));

        propuesta.GetProperty("nombre").GetString().Should().Be("Marta Ruiz");
        propuesta.GetProperty("motivos").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Asignar_pone_propietario_deja_constancia_y_crea_la_primera_llamada()
    {
        var cliente = await EnEmpresaAsync("Ribera Asignar");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        var r = await cliente.PostAsync(new Uri($"/match/contactos/{contacto}/asignar", UriKind.Relative), null);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LeerAsync(r)).GetProperty("asignadoA").GetString().Should().Be("Marta Ruiz");

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{contacto}", UriKind.Relative)));
        ficha.GetProperty("cronologia").EnumerateArray().Select(a => a.GetProperty("cuerpo").GetString())
            .Should().Contain(c => c!.StartsWith("Asignado a Marta Ruiz", StringComparison.Ordinal));

        var tareas = await LeerAsync(await cliente.GetAsync(new Uri("/tareas", UriKind.Relative)));
        tareas.GetArrayLength().Should().Be(1);
        tareas[0].GetProperty("titulo").GetString().Should().Be("Primera llamada a Manolo García");
    }

    [Fact]
    public async Task Hoy_pone_delante_al_contacto_con_mejor_match()
    {
        var cliente = await EnEmpresaAsync("Ribera Orden Match");
        var frio = await ContactoAsync(cliente, "Contacto Frío");
        var caliente = await ContactoAsync(cliente, "Contacto Caliente");

        // Los dos con una tarea de hoy, para que la urgencia no decida.
        await cliente.PostAsJsonAsync("/tareas", new { titulo = "Llamar al frío", contactoId = frio });
        await cliente.PostAsJsonAsync("/tareas", new { titulo = "Llamar al caliente", contactoId = caliente });

        // Solo uno da señales de vida.
        await cliente.PostAsJsonAsync($"/contactos/{caliente}/llamada", new { resultado = 1, nota = (string?)null });

        var tarjetas = (await LeerAsync(await cliente.GetAsync(new Uri("/hoy", UriKind.Relative)))).GetProperty("tarjetas");

        tarjetas[0].GetProperty("nombreContacto").GetString().Should().Be("Contacto Caliente");
        tarjetas[0].GetProperty("match").GetInt32().Should().BeGreaterThan(0);
        tarjetas[0].GetProperty("motivos").GetArrayLength().Should().BeGreaterThan(0, "ninguna tarjeta se enseña sin motivo");
    }

    [Fact]
    public async Task El_barrido_recalcula_toda_la_empresa()
    {
        var cliente = await EnEmpresaAsync("Ribera Barrido");
        await ContactoAsync(cliente, "Uno");
        await ContactoAsync(cliente, "Dos");
        await ContactoAsync(cliente, "Tres");

        var r = await LeerAsync(await cliente.PostAsync(new Uri("/match/recalcular", UriKind.Relative), null));

        r.GetProperty("recalculados").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task El_momento_decae_solo_cuando_pasan_los_dias()
    {
        var cliente = await EnEmpresaAsync("Ribera Decaimiento");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        await cliente.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 1, nota = (string?)null });

        (await MatchAsync(cliente, contacto)).GetProperty("momento").GetInt32().Should().Be(25);

        // Envejecemos la señal siete días: debe valer la mitad, sin que nadie toque nada.
        using (var alcance = api.Services.CreateAsyncScope())
        {
            var bd = alcance.ServiceProvider.GetRequiredService<Persistencia.ContextoMatchketing>();
            await bd.Database.ExecuteSqlRawAsync(
                "UPDATE match.senal SET ocurrida_en = ocurrida_en - interval '7 days' WHERE contacto_id = {0}", contacto);
        }

        await cliente.PostAsync(new Uri("/match/recalcular", UriKind.Relative), null);

        // 25 × 0,5 = 12,5. El rango absorbe los milisegundos de reloj real que pasan entre que se
        // envejece la señal y se recalcula: exigir un valor exacto haría la prueba intermitente.
        (await MatchAsync(cliente, contacto)).GetProperty("momento").GetInt32()
            .Should().BeInRange(12, 13, "a los siete días una señal vale la mitad");
    }

    [Fact]
    public async Task Una_empresa_no_ve_las_señales_de_otra()
    {
        var unaEmpresa = await EnEmpresaAsync("Ribera Match A");
        var otraEmpresa = await EnEmpresaAsync("Ribera Match B");

        var contacto = await ContactoAsync(unaEmpresa, "Secreto");
        await unaEmpresa.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 1, nota = (string?)null });

        var r = await otraEmpresa.GetAsync(new Uri($"/match/contactos/{contacto}", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
