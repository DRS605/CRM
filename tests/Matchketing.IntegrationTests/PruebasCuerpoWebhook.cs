using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// Qué va exactamente dentro de un webhook.
///
/// La regla del módulo es corta: **dice qué ha pasado y a quién apunta. Ni teléfonos, ni correos, ni
/// texto libre escrito por personas.** El motivo es que la URL la elige el cliente y muchas veces no es
/// un servidor suyo sino una plataforma de automatización que guarda cada carga útil para siempre. Un
/// teléfono que se escapa por ahí se ha escapado por nuestra culpa.
///
/// Se lee la fila directamente porque la pantalla de historial **no devuelve el cuerpo** —también a
/// propósito—, así que es el único sitio desde el que se puede comprobar.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasCuerpoWebhook(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync()
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"cw{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        var empresa = await cliente.PostAsJsonAsync("/empresas", new { nombre = "Ribera Cuerpos", provincia = "Valencia" });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(empresa)).GetProperty("token").GetString());
        return cliente;
    }

    private async Task<JsonElement> CuerpoAsync(Guid webhookId, string evento)
    {
        using var alcance = api.Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();

        // `IgnoreQueryFilters` porque este alcance no tiene empresa activa: el filtro global falla
        // cerrado y sin esto no se vería ninguna fila. Se acota por el webhook, que ya es de una sola.
        var cuerpos = await bd.EntregasWebhook
            .IgnoreQueryFilters()
            .Where(e => e.SuscripcionId == webhookId)
            .Select(e => e.Cuerpo)
            .ToListAsync();

        var elegido = cuerpos
            .Select(c => JsonDocument.Parse(c).RootElement.Clone())
            .Single(c => c.GetProperty("tipo").GetString() == evento);

        return elegido;
    }

    private static async Task<Guid> AltaAsync(HttpClient cliente, params string[] eventos) =>
        (await LeerAsync(await cliente.PostAsJsonAsync("/webhooks", new
        {
            url = $"https://erp.ejemplo.es/hooks/{Guid.NewGuid():N}",
            descripcion = "Pedidos al ERP",
            eventos,
        }))).GetProperty("id").GetGuid();

    [Fact]
    public async Task El_sobre_lleva_siempre_las_mismas_cuatro_cosas()
    {
        var cliente = await EnEmpresaAsync();
        var webhook = await AltaAsync(cliente, "lead.creado");

        await cliente.PostAsJsonAsync("/contactos", new { nombre = "Empar Beltrán", telefono = "961223344" });

        var cuerpo = await CuerpoAsync(webhook, "lead.creado");

        // Es el contrato: identificador para deduplicar, tipo para encaminar, cuándo pasó para ordenar,
        // y de qué empresa. Cambiar esto le rompe la integración a alguien.
        cuerpo.GetProperty("id").GetGuid().Should().NotBeEmpty();
        cuerpo.GetProperty("tipo").GetString().Should().Be("lead.creado");
        cuerpo.GetProperty("ocurridoEn").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-5));
        cuerpo.GetProperty("empresaId").GetGuid().Should().NotBeEmpty();
        cuerpo.TryGetProperty("datos", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Un_lead_creado_no_lleva_el_telefono_ni_el_correo()
    {
        var cliente = await EnEmpresaAsync();
        var webhook = await AltaAsync(cliente, "lead.creado");

        await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García",
            email = "manolo@casamanolo.es",
            telefono = "961234567",
            origen = "feria",
        });

        var cuerpo = await CuerpoAsync(webhook, "lead.creado");
        var texto = cuerpo.GetRawText();

        texto.Should().NotContain("961234567");
        texto.Should().NotContain("manolo@casamanolo.es");

        // Lo que sí lleva: a quién apunta y de dónde vino. Con el identificador y la API se puede pedir
        // el resto cuando de verdad haga falta; lo que se manda sin pensar no se puede recuperar.
        cuerpo.GetProperty("datos").GetProperty("contactoId").GetGuid().Should().NotBeEmpty();
        cuerpo.GetProperty("datos").GetProperty("origen").GetString().Should().Be("feria");
    }

    [Fact]
    public async Task Una_oportunidad_ganada_lleva_el_importe_porque_es_el_dato_del_evento()
    {
        var cliente = await EnEmpresaAsync();
        var webhook = await AltaAsync(cliente, "oportunidad.ganada");

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Rosa Miralles",
            telefono = "965112233",
        }))).GetProperty("id").GetGuid();

        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Climatización de 30 habitaciones",
            importe = 42000m,
        }))).GetProperty("id").GetGuid();

        await cliente.PostAsync(new Uri($"/oportunidades/{oportunidad}/ganar", UriKind.Relative), null);

        var datos = (await CuerpoAsync(webhook, "oportunidad.ganada")).GetProperty("datos");

        // El importe es el motivo de existir de este evento: al otro lado se emite un pedido. Sin él
        // habría que llamar de vuelta a la API para todo, y entonces nadie lo usa.
        datos.GetProperty("importe").GetDecimal().Should().Be(42000m);
        datos.GetProperty("oportunidadId").GetGuid().Should().Be(oportunidad);
        datos.GetProperty("contactoId").GetGuid().Should().Be(contacto);
    }

    [Fact]
    public async Task Una_oportunidad_perdida_lleva_el_motivo()
    {
        var cliente = await EnEmpresaAsync();
        var webhook = await AltaAsync(cliente, "oportunidad.perdida");

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Toni Escrivà",
            telefono = "961334455",
        }))).GetProperty("id").GetGuid();

        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Cocina",
            importe = 18400m,
        }))).GetProperty("id").GetGuid();

        await cliente.PostAsJsonAsync($"/oportunidades/{oportunidad}/perder", new { motivo = 1 });

        var datos = (await CuerpoAsync(webhook, "oportunidad.perdida")).GetProperty("datos");

        // El motivo va como texto, no como número: `2` no significa nada al otro lado y obligaría a
        // mantener nuestra tabla de códigos en su sistema.
        datos.GetProperty("motivo").GetString().Should().NotBeNullOrWhiteSpace();
        datos.GetProperty("motivo").GetString().Should().NotMatchRegex("^[0-9]+$");
    }

    [Fact]
    public async Task Un_movimiento_dice_de_donde_a_donde()
    {
        var cliente = await EnEmpresaAsync();
        var webhook = await AltaAsync(cliente, "oportunidad.movida");

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Neus Aparici",
            telefono = "961445566",
        }))).GetProperty("id").GetGuid();

        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Cámara frigorífica",
            importe = 7800m,
        }))).GetProperty("id").GetGuid();

        var columnas = (await LeerAsync(await cliente.GetAsync(new Uri("/embudo/tablero", UriKind.Relative))))
            .GetProperty("columnas").EnumerateArray().ToList();

        await cliente.PostAsJsonAsync($"/oportunidades/{oportunidad}/mover", new
        {
            etapaId = columnas[1].GetProperty("etapaId").GetGuid(),
        });

        var datos = (await CuerpoAsync(webhook, "oportunidad.movida")).GetProperty("datos");

        // Las dos etapas, porque «se ha movido» sin decir de dónde obliga a quien recibe a guardar el
        // estado anterior por su cuenta para saber si avanzó o retrocedió.
        datos.GetProperty("etapaId").GetGuid().Should().Be(columnas[1].GetProperty("etapaId").GetGuid());
        datos.GetProperty("etapaAnteriorId").GetGuid().Should().Be(columnas[0].GetProperty("etapaId").GetGuid());
    }

    [Fact]
    public async Task Una_baja_si_lleva_el_correo_y_es_la_unica_excepcion()
    {
        var cliente = await EnEmpresaAsync();
        var webhook = await AltaAsync(cliente, "contacto.baja");

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Salva Ferrandis",
            email = "salva@correo.es",
            telefono = "961556677",
        }))).GetProperty("id").GetGuid();

        // La baja se da desde la página pública, como la daría la persona: es el único camino que
        // existe, y es el que hay que probar.
        var enlace = (await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative))))
            .GetProperty("enlaceBaja").GetString()!;
        var ruta = enlace[enlace.IndexOf("/b/", StringComparison.Ordinal)..];

        (await api.CreateClient().PostAsync(new Uri(ruta, UriKind.Relative), null))
            .IsSuccessStatusCode.Should().BeTrue();

        var datos = (await CuerpoAsync(webhook, "contacto.baja")).GetProperty("datos");

        // La excepción razonada: el propósito exacto de este evento es que otro sistema deje de escribir
        // a esa dirección, y allí la clave es la dirección, no nuestro identificador. Exigir una llamada
        // a la API para cumplir una obligación legal es peor que mandar el dato que la cumple.
        datos.GetProperty("email").GetString().Should().Be("salva@correo.es");
        datos.GetProperty("contactoId").GetGuid().Should().Be(contacto);

        // Y ni siquiera aquí va el teléfono: para dejar de mandar correos no hace falta.
        datos.GetRawText().Should().NotContain("961556677");
    }
}
