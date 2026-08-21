using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Matchketing.Nucleo.Tiempo;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoHoy(ApiDePrueba api)
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
            email = $"h{Guid.NewGuid():N}@ribera.es",
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
        var r = await cliente.PostAsJsonAsync("/contactos", new { nombre, telefono = "961234567" });
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> HoyAsync(HttpClient cliente) =>
        await LeerAsync(await cliente.GetAsync(new Uri("/hoy", UriKind.Relative)));

    private static string Tipo(JsonElement tarjeta) => tarjeta.GetProperty("tipo").GetInt32() switch
    {
        1 => "tarea", 2 => "sinAccion", 3 => "estancada", _ => "?",
    };

    [Fact]
    public async Task Una_empresa_recien_creada_no_tiene_nada_que_hacer()
    {
        var cliente = await EnEmpresaAsync("Ribera Hoy Vacío");

        var pila = await HoyAsync(cliente);

        pila.GetProperty("pendientes").GetInt32().Should().Be(0);
        pila.GetProperty("tarjetas").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Un_contacto_vivo_sin_proximo_paso_aparece_en_Hoy()
    {
        var cliente = await EnEmpresaAsync("Ribera Sin Paso");
        await ContactoAsync(cliente, "Manolo García");

        var pila = await HoyAsync(cliente);

        pila.GetProperty("sinProximaAccion").GetInt32().Should().Be(1);
        var tarjeta = pila.GetProperty("tarjetas")[0];
        Tipo(tarjeta).Should().Be("sinAccion");
        tarjeta.GetProperty("motivo").GetString().Should().Contain("Sin próximo paso");
    }

    [Fact]
    public async Task Crear_una_tarea_quita_al_contacto_de_la_lista_de_sin_proximo_paso()
    {
        var cliente = await EnEmpresaAsync("Ribera Con Paso");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        await cliente.PostAsJsonAsync("/tareas", new { titulo = "Llamar a Manolo", contactoId = contacto });

        var pila = await HoyAsync(cliente);
        pila.GetProperty("sinProximaAccion").GetInt32().Should().Be(0);
        Tipo(pila.GetProperty("tarjetas")[0]).Should().Be("tarea");
    }

    [Fact]
    public async Task Lo_vencido_va_por_delante_de_lo_que_toca_hoy()
    {
        var cliente = await EnEmpresaAsync("Ribera Orden");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var ayer = Hoy.AddDays(-4);

        await cliente.PostAsJsonAsync("/tareas", new { titulo = "Lo de hoy", contactoId = contacto });
        await cliente.PostAsJsonAsync("/tareas", new { titulo = "Lo de hace días", contactoId = contacto, venceEl = ayer });

        var tarjetas = (await HoyAsync(cliente)).GetProperty("tarjetas");

        tarjetas[0].GetProperty("titulo").GetString().Should().Be("Lo de hace días");
        tarjetas[0].GetProperty("motivo").GetString().Should().Contain("4 días esperando");
        tarjetas[1].GetProperty("motivo").GetString().Should().Be("Toca hoy.");
    }

    [Fact]
    public async Task Completar_la_saca_de_la_pila_y_la_cuenta_como_hecha()
    {
        var cliente = await EnEmpresaAsync("Ribera Completar");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var id = (await LeerAsync(await cliente.PostAsJsonAsync("/tareas", new { titulo = "Llamar", contactoId = contacto })))
            .GetProperty("id").GetGuid();

        await cliente.PostAsync(new Uri($"/tareas/{id}/completar", UriKind.Relative), null);

        var pila = await HoyAsync(cliente);
        pila.GetProperty("hechasHoy").GetInt32().Should().Be(1);
        pila.GetProperty("tarjetas").EnumerateArray()
            .Should().NotContain(t => t.GetProperty("tareaId").GetString() == id.ToString());
    }

    [Fact]
    public async Task Aplazar_sin_fecha_se_rechaza()
    {
        var cliente = await EnEmpresaAsync("Ribera Aplazar Mal");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var id = (await LeerAsync(await cliente.PostAsJsonAsync("/tareas", new { titulo = "Llamar", contactoId = contacto })))
            .GetProperty("id").GetGuid();

        var r = await cliente.PostAsJsonAsync($"/tareas/{id}/aplazar", new { hasta = (DateOnly?)null });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("tarea.aplazar_sin_fecha");
    }

    [Fact]
    public async Task Aplazar_a_manana_la_saca_de_la_pila_de_hoy()
    {
        var cliente = await EnEmpresaAsync("Ribera Aplazar Bien");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var id = (await LeerAsync(await cliente.PostAsJsonAsync("/tareas", new { titulo = "Llamar", contactoId = contacto })))
            .GetProperty("id").GetGuid();

        var manana = Hoy.AddDays(1);
        var r = await cliente.PostAsJsonAsync($"/tareas/{id}/aplazar", new { hasta = manana });
        r.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var pila = await HoyAsync(cliente);
        pila.GetProperty("tarjetas").EnumerateArray().Should().NotContain(t => Tipo(t) == "tarea");
    }

    [Fact]
    public async Task Una_llamada_que_pide_volver_a_llamar_crea_la_tarea_sola()
    {
        var cliente = await EnEmpresaAsync("Ribera Seguimiento");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        await cliente.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 4, nota = "Estaba liado." });

        var tareas = await LeerAsync(await cliente.GetAsync(new Uri("/tareas", UriKind.Relative)));
        tareas.GetArrayLength().Should().Be(1);
        tareas[0].GetProperty("titulo").GetString().Should().Be("Volver a llamar");
        tareas[0].GetProperty("origen").GetInt32().Should().Be(2, "la creó el sistema, no una persona");
    }

    [Fact]
    public async Task El_seguimiento_automatico_no_se_duplica()
    {
        var cliente = await EnEmpresaAsync("Ribera Sin Duplicar");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        await cliente.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 4, nota = (string?)null });
        await cliente.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 4, nota = (string?)null });

        var tareas = await LeerAsync(await cliente.GetAsync(new Uri("/tareas", UriKind.Relative)));
        tareas.GetArrayLength().Should().Be(1, "Hoy debe ser una lista corta, no un montón de recordatorios repetidos");
    }

    [Fact]
    public async Task Una_llamada_contactado_no_crea_tarea_de_seguimiento()
    {
        var cliente = await EnEmpresaAsync("Ribera Contactado");
        var contacto = await ContactoAsync(cliente, "Manolo García");

        await cliente.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 1, nota = (string?)null });

        var tareas = await LeerAsync(await cliente.GetAsync(new Uri("/tareas", UriKind.Relative)));
        tareas.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Una_oportunidad_parada_mas_dias_de_los_que_tolera_su_etapa_sale_en_Hoy()
    {
        var cliente = await EnEmpresaAsync("Ribera Parada");
        var contacto = await ContactoAsync(cliente, "Manolo García");
        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades",
            new { contactoId = contacto, titulo = "Cámara frigorífica", importe = 14280m })))
            .GetProperty("id").GetGuid();

        // La primera etapa tolera 3 días; la envejecemos en la base para no esperar cuatro días.
        using (var alcance = api.Services.CreateAsyncScope())
        {
            var bd = alcance.ServiceProvider.GetRequiredService<Persistencia.ContextoMatchketing>();
            await bd.Database.ExecuteSqlRawAsync(
                "UPDATE embudo.oportunidad SET entro_en_etapa_en = now() - interval '9 days' WHERE id = {0}", oportunidad);
        }

        var pila = await HoyAsync(cliente);

        pila.GetProperty("estancadas").GetInt32().Should().Be(1);
        var parada = pila.GetProperty("tarjetas").EnumerateArray().First(t => Tipo(t) == "estancada");
        parada.GetProperty("motivo").GetString().Should().Contain("parada en «Nuevo»");
        parada.GetProperty("importe").GetDecimal().Should().Be(14280m);
    }

    [Fact]
    public async Task Una_empresa_no_ve_las_tareas_de_otra()
    {
        var unaEmpresa = await EnEmpresaAsync("Ribera Hoy A");
        var otraEmpresa = await EnEmpresaAsync("Ribera Hoy B");

        var contacto = await ContactoAsync(unaEmpresa, "Secreto");
        await unaEmpresa.PostAsJsonAsync("/tareas", new { titulo = "Tarea secreta", contactoId = contacto });

        var pila = await HoyAsync(otraEmpresa);

        pila.GetProperty("tarjetas").EnumerateArray().Should().NotContain(t => Tipo(t) == "tarea");
    }
}
