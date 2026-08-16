using System.Diagnostics;
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
public sealed class PruebasFlujoCaptacion(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync(string nombreEmpresa)
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"c{Guid.NewGuid():N}@ribera.es",
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

    private static async Task<string> FormularioAsync(HttpClient cliente, string? gracias = null)
    {
        var r = await cliente.PostAsJsonAsync("/formularios", new
        {
            nombre = "Presupuesto web",
            textoConsentimiento = "Acepto que me contactéis para responder a mi solicitud.",
            pideTelefono = true,
            pideEmpresa = false,
            pideMensaje = true,
            paginaGracias = gracias,
            origen = (string?)null,
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await LeerAsync(r)).GetProperty("clave").GetString()!;
    }

    [Fact]
    public async Task La_definicion_del_formulario_es_publica_para_poder_pintarlo()
    {
        var cliente = await EnEmpresaAsync("Ribera Formulario");
        var clave = await FormularioAsync(cliente);

        var anonimo = api.CreateClient();
        var def = await LeerAsync(await anonimo.GetAsync(new Uri($"/f/{clave}", UriKind.Relative)));

        def.GetProperty("nombre").GetString().Should().Be("Presupuesto web");
        def.GetProperty("pideTelefono").GetBoolean().Should().BeTrue();
        def.GetProperty("textoConsentimiento").GetString().Should().Contain("Acepto");
    }

    [Fact]
    public async Task Una_clave_que_no_existe_no_dice_nada()
    {
        var r = await api.CreateClient().GetAsync(new Uri("/f/noexisteestaclave123", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sin_marcar_la_casilla_no_entra_ni_el_contacto()
    {
        var cliente = await EnEmpresaAsync("Ribera Sin Consentir");
        var clave = await FormularioAsync(cliente);

        var r = await api.CreateClient().PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo García", email = "manolo@casamanolo.es", consiente = false,
        });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("lead.sin_consentimiento");

        var contactos = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        contactos.GetArrayLength().Should().Be(0, "guardar a alguien que no ha consentido es justo lo que no se puede hacer");
    }

    [Fact]
    public async Task Un_lead_entra_asignado_puntuado_y_con_su_primera_llamada()
    {
        var cliente = await EnEmpresaAsync("Ribera Lead");
        var clave = await FormularioAsync(cliente, "https://ribera.es/gracias");

        var crono = Stopwatch.StartNew();
        var r = await api.CreateClient().PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo García",
            email = "manolo@casamanolo.es",
            telefono = "961234567",
            mensaje = "Quiero presupuesto para una cámara frigorífica.",
            consiente = true,
        });
        crono.Stop();

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await LeerAsync(r);
        cuerpo.GetProperty("asignadoA").GetString().Should().Be("Marta Ruiz");
        cuerpo.GetProperty("paginaGracias").GetString().Should().Be("https://ribera.es/gracias");

        // El criterio de aceptación del producto: de la web a una tarjeta en Hoy, sin tocar nada.
        crono.ElapsedMilliseconds.Should().BeLessThan(2000, "el objetivo es un lead listo en menos de un minuto, con margen de sobra");

        var contactos = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        contactos.GetArrayLength().Should().Be(1);
        contactos[0].GetProperty("origen").GetString().Should().Be("formulario web");

        var id = contactos[0].GetProperty("id").GetGuid();

        var match = await LeerAsync(await cliente.GetAsync(new Uri($"/match/contactos/{id}", UriKind.Relative)));
        match.GetProperty("momento").GetInt32().Should().Be(35, "un formulario enviado pesa 35");

        var tareas = await LeerAsync(await cliente.GetAsync(new Uri("/tareas", UriKind.Relative)));
        tareas.GetArrayLength().Should().Be(1);
        tareas[0].GetProperty("titulo").GetString().Should().Be("Primera llamada a Manolo García");

        var hoy = await LeerAsync(await cliente.GetAsync(new Uri("/hoy", UriKind.Relative)));
        hoy.GetProperty("tarjetas")[0].GetProperty("nombreContacto").GetString().Should().Be("Manolo García");
    }

    [Fact]
    public async Task El_mensaje_y_la_asignacion_quedan_escritos_en_la_cronologia()
    {
        var cliente = await EnEmpresaAsync("Ribera Cronología");
        var clave = await FormularioAsync(cliente);

        await api.CreateClient().PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo García", email = "manolo@crono.es", mensaje = "Necesito una cámara.", consiente = true,
        });

        var contactos = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        var id = contactos[0].GetProperty("id").GetGuid();
        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{id}", UriKind.Relative)));

        var textos = ficha.GetProperty("cronologia").EnumerateArray()
            .Select(a => a.GetProperty("cuerpo").GetString()!).ToList();

        textos.Should().Contain(t => t.Contains("Necesito una cámara", StringComparison.Ordinal));
        textos.Should().Contain(t => t.StartsWith("Asignado a Marta Ruiz", StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_consentimiento_se_guarda_con_su_prueba_no_como_un_si_a_secas()
    {
        var cliente = await EnEmpresaAsync("Ribera Prueba");
        var clave = await FormularioAsync(cliente);

        // El navegador del visitante manda su User-Agent y nosotros lo guardamos como prueba.
        var visitante = api.CreateClient();
        visitante.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0) Firefox/141.0");

        await visitante.PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo García", email = "manolo@prueba.es", consiente = true,
        });

        var contactos = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        var id = contactos[0].GetProperty("id").GetGuid();

        // Acotado a este contacto: todas las pruebas comparten base, y un First() suelto cogería
        // el consentimiento de cualquier otra.
        using var alcance = api.Services.CreateAsyncScope();
        var bd = alcance.ServiceProvider.GetRequiredService<Persistencia.ContextoMatchketing>();
        var c = await bd.Consentimientos.IgnoreQueryFilters().SingleAsync(x => x.ContactoId == id);

        c.Canal.Should().Be("formulario web");
        c.TextoAceptado.Should().Contain("Acepto que me contactéis");
        c.Vigente.Should().BeTrue();
        c.Agente.Should().Contain("Firefox/141.0");
    }

    [Fact]
    public async Task El_envio_se_guarda_entero_aunque_el_contacto_cambie_despues()
    {
        var cliente = await EnEmpresaAsync("Ribera Envío");
        var clave = await FormularioAsync(cliente);

        await api.CreateClient().PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo García", email = "manolo@envio.es", mensaje = "Texto original", consiente = true,
        });

        var formularios = await LeerAsync(await cliente.GetAsync(new Uri("/formularios", UriKind.Relative)));
        formularios[0].GetProperty("envios").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Un_formulario_desactivado_deja_de_aceptar_envios()
    {
        var cliente = await EnEmpresaAsync("Ribera Desactivado");
        var clave = await FormularioAsync(cliente);
        var formularios = await LeerAsync(await cliente.GetAsync(new Uri("/formularios", UriKind.Relative)));
        var id = formularios[0].GetProperty("id").GetGuid();

        await cliente.DeleteAsync(new Uri($"/formularios/{id}", UriKind.Relative));

        var r = await api.CreateClient().PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo", email = "m@x.es", consiente = true,
        });

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task El_script_de_una_linea_se_sirve_y_apunta_al_endpoint_correcto()
    {
        var cliente = await EnEmpresaAsync("Ribera Script");
        var clave = await FormularioAsync(cliente);

        var r = await api.CreateClient().GetAsync(new Uri($"/f/{clave}/script.js", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/javascript");

        var js = await r.Content.ReadAsStringAsync();
        js.Should().Contain(clave);
        js.Should().Contain("Acepto que me contact", "el texto de consentimiento va dentro del script");
        js.Should().Contain("telefono", "este formulario pide teléfono");
        js.Should().NotContain("empresa\\\"", "este no pide empresa");
    }

    [Fact]
    public async Task La_entrada_publica_permite_peticiones_desde_la_web_del_cliente()
    {
        // El script vive en otro origen. Sin CORS el navegador bloquearía el envío y la captación
        // no funcionaría fuera de nuestro propio dominio.
        var cliente = await EnEmpresaAsync("Ribera CORS");
        var clave = await FormularioAsync(cliente);

        var visitante = api.CreateClient();
        visitante.DefaultRequestHeaders.Add("Origin", "https://www.instalacionesribera.es");

        var r = await visitante.PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo García", email = "manolo@cors.es", consiente = true,
        });

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Headers.Should().ContainKey("Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task El_resto_de_la_API_no_abre_CORS_a_cualquiera()
    {
        var cliente = await EnEmpresaAsync("Ribera CORS Cerrado");
        cliente.DefaultRequestHeaders.Add("Origin", "https://sitio-ajeno.example");

        var r = await cliente.GetAsync(new Uri("/contactos", UriKind.Relative));

        r.Headers.Should().NotContainKey("Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task La_visita_web_de_un_contacto_conocido_suma_señal()
    {
        var cliente = await EnEmpresaAsync("Ribera Visita");
        var clave = await FormularioAsync(cliente);
        await api.CreateClient().PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo García", email = "manolo@visita.es", consiente = true,
        });

        var contactos = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        var id = contactos[0].GetProperty("id").GetGuid();
        var antes = (await LeerAsync(await cliente.GetAsync(new Uri($"/match/contactos/{id}", UriKind.Relative)))).GetProperty("momento").GetInt32();

        await api.CreateClient().PostAsJsonAsync($"/f/{clave}/visita", new { contactoId = id });

        var despues = (await LeerAsync(await cliente.GetAsync(new Uri($"/match/contactos/{id}", UriKind.Relative)))).GetProperty("momento").GetInt32();
        despues.Should().BeGreaterThan(antes);
    }

    [Fact]
    public async Task El_lead_de_un_formulario_no_se_cuela_en_otra_empresa()
    {
        var unaEmpresa = await EnEmpresaAsync("Ribera Captación A");
        var otraEmpresa = await EnEmpresaAsync("Ribera Captación B");
        var clave = await FormularioAsync(unaEmpresa);

        await api.CreateClient().PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Manolo García", email = "manolo@aislado.es", consiente = true,
        });

        (await LeerAsync(await unaEmpresa.GetAsync(new Uri("/contactos", UriKind.Relative)))).GetArrayLength().Should().Be(1);
        (await LeerAsync(await otraEmpresa.GetAsync(new Uri("/contactos", UriKind.Relative)))).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Un_lead_sin_correo_ni_telefono_se_rechaza_igual_que_en_el_alta_manual()
    {
        var cliente = await EnEmpresaAsync("Ribera Lead Vacío");
        var clave = await FormularioAsync(cliente);

        var r = await api.CreateClient().PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Fantasma", consiente = true,
        });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("contacto.sin_medio");
    }
}
