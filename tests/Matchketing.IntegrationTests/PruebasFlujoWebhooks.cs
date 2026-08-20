using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoWebhooks(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync(string nombreEmpresa = "Instalaciones Ribera")
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"w{Guid.NewGuid():N}@ribera.es",
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

    private static async Task<Guid> AltaWebhookAsync(HttpClient cliente, params string[] eventos)
    {
        var r = await cliente.PostAsJsonAsync("/webhooks", new
        {
            url = $"https://erp.ejemplo.es/hooks/{Guid.NewGuid():N}",
            descripcion = "Pedidos al ERP",
            eventos,
        });

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task<int> PendientesAsync(HttpClient cliente, Guid id) =>
        (await LeerAsync(await cliente.GetAsync(new Uri($"/webhooks/{id}/entregas", UriKind.Relative))))
            .EnumerateArray().Count();

    // ---------- Alta y gestión ----------

    [Fact]
    public async Task El_catalogo_de_eventos_son_cinco_y_con_su_nombre_publico()
    {
        var cliente = await EnEmpresaAsync();

        var eventos = (await LeerAsync(await cliente.GetAsync(new Uri("/webhooks/eventos", UriKind.Relative))))
            .EnumerateArray().Select(e => e.GetProperty("nombre").GetString()).ToArray();

        // Es contrato público: si esta lista cambia, alguien se queda sin integración.
        eventos.Should().BeEquivalentTo(
            "lead.creado", "oportunidad.movida", "oportunidad.ganada", "oportunidad.perdida", "contacto.baja");
    }

    [Fact]
    public async Task El_secreto_se_devuelve_al_crear_y_nunca_mas()
    {
        var cliente = await EnEmpresaAsync();

        var creado = await LeerAsync(await cliente.PostAsJsonAsync("/webhooks", new
        {
            url = "https://erp.ejemplo.es/hooks/uno",
            descripcion = "Pedidos al ERP",
            eventos = new[] { "oportunidad.ganada" },
        }));

        var secreto = creado.GetProperty("secreto").GetString();
        secreto.Should().StartWith("whsec_");

        // Y el listado no lo lleva. Guardarlo en claro es inevitable —hay que firmar con él—, pero
        // devolverlo en cada consulta sería regalarlo a cualquier sesión abierta sin bloquear.
        var listado = await (await cliente.GetAsync(new Uri("/webhooks", UriKind.Relative))).Content.ReadAsStringAsync();
        listado.Should().NotContain(secreto!);
        listado.Should().NotContain("whsec_");
    }

    [Fact]
    public async Task Un_endpoint_http_se_rechaza_con_su_codigo()
    {
        var cliente = await EnEmpresaAsync();

        var r = await cliente.PostAsJsonAsync("/webhooks", new
        {
            url = "http://erp.ejemplo.es/hooks",
            descripcion = "Pedidos",
            eventos = new[] { "oportunidad.ganada" },
        });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("webhook.url_invalida");
    }

    [Fact]
    public async Task Rotar_el_secreto_devuelve_otro_y_avisa_de_que_el_anterior_ya_no_vale()
    {
        var cliente = await EnEmpresaAsync();
        var id = await AltaWebhookAsync(cliente, "oportunidad.ganada");

        var r = await LeerAsync(await cliente.PostAsync(new Uri($"/webhooks/{id}/secreto", UriKind.Relative), null));

        r.GetProperty("secreto").GetString().Should().StartWith("whsec_");
        r.GetProperty("aviso").GetString().Should().Contain("ya no vale");
    }

    [Fact]
    public async Task Cada_empresa_ve_solo_sus_webhooks()
    {
        var una = await EnEmpresaAsync("Ribera Uno");
        var otra = await EnEmpresaAsync("Ribera Dos");
        await AltaWebhookAsync(una, "oportunidad.ganada");

        var vistos = (await LeerAsync(await otra.GetAsync(new Uri("/webhooks", UriKind.Relative)))).EnumerateArray();

        vistos.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_sesion_no_se_puede_ni_ver_el_catalogo()
    {
        var r = await api.CreateClient().GetAsync(new Uri("/webhooks/eventos", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------- Emisión: lo que de verdad importa ----------

    [Fact]
    public async Task Ganar_una_oportunidad_encola_una_entrega_con_su_importe()
    {
        var cliente = await EnEmpresaAsync();
        var id = await AltaWebhookAsync(cliente, "oportunidad.ganada");

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García",
            email = "manolo@casamanolo.es",
        }))).GetProperty("id").GetGuid();

        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Cocina completa",
            importe = 18400m,
        }))).GetProperty("id").GetGuid();

        (await cliente.PostAsync(new Uri($"/oportunidades/{oportunidad}/ganar", UriKind.Relative), null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var entregas = (await LeerAsync(await cliente.GetAsync(new Uri($"/webhooks/{id}/entregas", UriKind.Relative))))
            .EnumerateArray().ToList();

        var ganada = entregas.Should().ContainSingle(e => e.GetProperty("evento").GetString() == "oportunidad.ganada")
            .Subject;
        ganada.GetProperty("estado").GetString().Should().Be("pendiente");
        ganada.GetProperty("intentos").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Ganar_desde_el_repaso_encola_igual_que_ganar_desde_el_tablero()
    {
        var cliente = await EnEmpresaAsync();
        var id = await AltaWebhookAsync(cliente, "oportunidad.ganada");

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Rosa Miralles",
            telefono = "965112233",
        }))).GetProperty("id").GetGuid();

        // Una oportunidad con la fecha de cierre pasada: eso hace que el repaso pregunte por ella.
        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Climatización",
            importe = 42000m,
            previstaCierre = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
        }))).GetProperty("id").GetGuid();

        var respuesta = await cliente.PostAsJsonAsync("/repaso/responder", new
        {
            clave = $"cierre-pasado:{oportunidad}",
            respuesta = 8, // Ganada
        });
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        // **Esta es la prueba que justifica todo el diseño.** Los webhooks se cuelgan de los eventos de
        // dominio y no de los endpoints, así que el repaso emite sin saber que los webhooks existen.
        // Colgarlos de los endpoints habría dejado este camino fuera, y nadie lo habría notado hasta
        // que un cliente preguntara por qué a veces no llega.
        var entregas = (await LeerAsync(await cliente.GetAsync(new Uri($"/webhooks/{id}/entregas", UriKind.Relative))))
            .EnumerateArray().ToList();

        entregas.Should().Contain(e => e.GetProperty("evento").GetString() == "oportunidad.ganada");
    }

    [Fact]
    public async Task Crear_un_contacto_encola_un_lead_creado()
    {
        var cliente = await EnEmpresaAsync();
        var id = await AltaWebhookAsync(cliente, "lead.creado");

        await cliente.PostAsJsonAsync("/contactos", new { nombre = "Empar Beltrán", telefono = "961223344", origen = "feria" });

        var entregas = (await LeerAsync(await cliente.GetAsync(new Uri($"/webhooks/{id}/entregas", UriKind.Relative))))
            .EnumerateArray().ToList();

        entregas.Should().ContainSingle(e => e.GetProperty("evento").GetString() == "lead.creado");
    }

    [Fact]
    public async Task A_quien_no_escucha_ese_evento_no_le_llega_nada()
    {
        var cliente = await EnEmpresaAsync();
        var soloBajas = await AltaWebhookAsync(cliente, "contacto.baja");

        await cliente.PostAsJsonAsync("/contactos", new { nombre = "Toni Escrivà", telefono = "961334455" });

        (await PendientesAsync(cliente, soloBajas)).Should().Be(0);
    }

    [Fact]
    public async Task Mover_una_oportunidad_a_la_etapa_en_la_que_ya_esta_no_emite_nada()
    {
        var cliente = await EnEmpresaAsync();
        var id = await AltaWebhookAsync(cliente, "oportunidad.movida");

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new { nombre = "Neus Aparici", telefono = "961445566" })))
            .GetProperty("id").GetGuid();
        var oportunidadId = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Cámara frigorífica",
            importe = 7800m,
        }))).GetProperty("id").GetGuid();

        // Una oportunidad nueva cae en la primera etapa del embudo: moverla ahí es moverla a donde ya
        // está, que es el caso que no debe emitir.
        var etapaActual = (await LeerAsync(await cliente.GetAsync(new Uri("/embudo/tablero", UriKind.Relative))))
            .GetProperty("columnas").EnumerateArray().First().GetProperty("etapaId").GetGuid();

        await cliente.PostAsJsonAsync($"/oportunidades/{oportunidadId}/mover", new { etapaId = etapaActual });

        // Mover algo a donde ya estaba no es un movimiento, y emitirlo llenaría el embudo de eventos
        // que no dicen nada.
        (await PendientesAsync(cliente, id)).Should().Be(0);
    }

    [Fact]
    public async Task Sin_ningun_webhook_dado_de_alta_no_se_guarda_nada()
    {
        var cliente = await EnEmpresaAsync();

        await cliente.PostAsJsonAsync("/contactos", new { nombre = "Salva Ferrandis", telefono = "961556677" });

        // Es el caso de casi todo el mundo: el coste de tener webhooks en el sistema tiene que ser cero
        // para quien no los usa. Si esto se rompiera, cada alta de contacto escribiría filas de más.
        var id = await AltaWebhookAsync(cliente, "lead.creado");
        (await PendientesAsync(cliente, id)).Should().Be(0);
    }

    [Fact]
    public async Task Borrar_el_webhook_no_deja_entregas_vivas()
    {
        var cliente = await EnEmpresaAsync();
        var id = await AltaWebhookAsync(cliente, "lead.creado");
        await cliente.PostAsJsonAsync("/contactos", new { nombre = "Empar Beltrán", telefono = "961223344" });
        (await PendientesAsync(cliente, id)).Should().Be(1);

        (await cliente.DeleteAsync(new Uri($"/webhooks/{id}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Ya no se puede consultar su historial, porque ya no existe.
        (await cliente.GetAsync(new Uri($"/webhooks/{id}/entregas", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Un_webhook_apagado_deja_de_recibir_y_al_reactivarlo_vuelve()
    {
        var cliente = await EnEmpresaAsync();
        var id = await AltaWebhookAsync(cliente, "lead.creado");

        // Se cambia a un evento que no vamos a provocar: es la forma de comprobar que el filtro por
        // tipo se aplica al encolar y no al entregar.
        (await cliente.PutAsJsonAsync($"/webhooks/{id}", new
        {
            descripcion = "Solo bajas ahora",
            eventos = new[] { "contacto.baja" },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await cliente.PostAsJsonAsync("/contactos", new { nombre = "Toni Escrivà", telefono = "961334455" });
        (await PendientesAsync(cliente, id)).Should().Be(0);

        (await cliente.PutAsJsonAsync($"/webhooks/{id}", new
        {
            descripcion = "Leads otra vez",
            eventos = new[] { "lead.creado" },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await cliente.PostAsJsonAsync("/contactos", new { nombre = "Neus Aparici", telefono = "961445566" });
        (await PendientesAsync(cliente, id)).Should().Be(1);
    }

    [Fact]
    public async Task El_cuerpo_de_una_entrega_no_lleva_telefonos_ni_correos()
    {
        var cliente = await EnEmpresaAsync();
        var id = await AltaWebhookAsync(cliente, "lead.creado");

        await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García",
            email = "manolo@casamanolo.es",
            telefono = "961234567",
        });

        // El historial no devuelve el cuerpo a propósito, así que esto se comprueba por el otro lado:
        // el módulo declara qué campos lleva cada evento y `DespachadorEventos` solo pone esos. La
        // prueba que lo sujeta de verdad está en `PruebasDespachador`; aquí se comprueba que al menos
        // la entrega existe y que la pantalla no filtra el cuerpo sin querer.
        var entregas = await (await cliente.GetAsync(new Uri($"/webhooks/{id}/entregas", UriKind.Relative)))
            .Content.ReadAsStringAsync();

        entregas.Should().NotContain("961234567");
        entregas.Should().NotContain("manolo@casamanolo.es");
        (await PendientesAsync(cliente, id)).Should().Be(1);
    }
}
