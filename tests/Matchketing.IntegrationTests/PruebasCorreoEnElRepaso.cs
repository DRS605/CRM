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
/// La razón de haber hecho el correo antes que cualquier otra cosa: **el repaso puede preguntar algo
/// que antes no podía.**
///
/// «Le escribiste hace seis días y no ha contestado» es la situación comercial más común que existe y
/// la que más se queda sin resolver, porque no genera ninguna tarea ni ninguna alerta. Nadie apunta
/// «volver a llamar a quien no me contestó». Con el correo dentro, el repaso lo saca solo.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasCorreoEnElRepaso(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync()
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"cr{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        var empresa = await cliente.PostAsJsonAsync("/empresas", new { nombre = "Ribera Repaso Correo", provincia = "Valencia" });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(empresa)).GetProperty("token").GetString());
        return cliente;
    }

    /// <summary>
    /// Un contacto con un correo ya enviado hace <paramref name="hace"/> días.
    ///
    /// El envío se empuja al pasado tocando la fila: la otra opción sería inyectar un reloj falso en la
    /// API entera, y eso convertiría una prueba de una pregunta en una prueba de la infraestructura de
    /// pruebas. Lo que se está probando es la consulta, y la consulta solo mira `enviado_en`.
    /// </summary>
    private async Task<(Guid Contacto, Guid Correo)> ConCorreoEnviadoAsync(
        HttpClient cliente, int hace, int aperturas = 0)
    {
        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García",
            email = $"m{Guid.NewGuid():N}@casamanolo.es",
            telefono = "961234567",
        }))).GetProperty("id").GetGuid();

        (await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 1,
            @base = 2,
            canal = "alta manual",
        })).IsSuccessStatusCode.Should().BeTrue();

        var plantilla = (await LeerAsync(await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = $"Seguimiento {Guid.NewGuid():N}",
            asunto = "Sobre lo que hablamos",
            cuerpo = "Hola {{nombre}}, te llamo mañana.",
            paraQue = 1,
        }))).GetProperty("id").GetGuid();

        var enviado = await LeerAsync(await cliente.PostAsJsonAsync(
            "/correo/enviar", new { contactoId = contacto, plantillaId = plantilla }));
        var correoId = enviado.GetProperty("id").GetGuid();

        using var alcance = api.Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();
        var cuando = DateTimeOffset.UtcNow.AddDays(-hace);

        await bd.Database.ExecuteSqlRawAsync(
            "UPDATE correo.mensaje SET estado = 2, enviado_en = {0}, aperturas = {1} WHERE id = {2}",
            cuando, aperturas, correoId);

        return (contacto, correoId);
    }

    private static async Task<JsonElement?> PreguntaAsync(HttpClient cliente, Guid contacto)
    {
        var pila = await LeerAsync(await cliente.GetAsync(new Uri("/repaso", UriKind.Relative)));

        foreach (var p in pila.GetProperty("preguntas").EnumerateArray())
        {
            if (p.GetProperty("clave").GetString() == $"correo-sin-respuesta:{contacto}")
            {
                return p.Clone();
            }
        }

        return null;
    }

    [Fact]
    public async Task Un_correo_sin_contestar_sale_en_el_repaso()
    {
        var cliente = await EnEmpresaAsync();
        var (contacto, _) = await ConCorreoEnviadoAsync(cliente, hace: 6);

        var pregunta = await PreguntaAsync(cliente, contacto);

        pregunta.Should().NotBeNull();
        pregunta!.Value.GetProperty("detalle").GetString().Should().Contain("no ha contestado");

        // Y con la respuesta que de verdad funciona por delante: si el primer correo no ha servido, el
        // segundo casi nunca sirve. Lo que cambia el resultado es el teléfono.
        pregunta.Value.GetProperty("opciones").EnumerateArray().First()
            .GetProperty("etiqueta").GetString().Should().Contain("llamo");
    }

    [Fact]
    public async Task Un_correo_de_ayer_todavia_no_se_pregunta()
    {
        var cliente = await EnEmpresaAsync();
        var (contacto, _) = await ConCorreoEnviadoAsync(cliente, hace: 1);

        // Hay gente que contesta el correo del viernes el lunes por la tarde. Preguntar al día siguiente
        // es agobiar, y a quien agobia se le deja de leer.
        (await PreguntaAsync(cliente, contacto)).Should().BeNull();
    }

    [Fact]
    public async Task Ya_me_contesto_apunta_la_respuesta_y_calla_la_pregunta()
    {
        var cliente = await EnEmpresaAsync();
        var (contacto, _) = await ConCorreoEnviadoAsync(cliente, hace: 6);
        (await PreguntaAsync(cliente, contacto)).Should().NotBeNull();

        // «Ya me contestó», de un toque, desde el propio repaso.
        //
        // Esta opción existe porque al escribir la prueba se vio que sin ella la pregunta no se podía
        // cerrar diciendo la verdad: el comercial recibe la respuesta en **su buzón**, no aquí, y solo
        // podría contestar «déjalo estar». El sistema seguiría creyendo que nadie contestó, y eso
        // contamina el Match y el informe de la semana.
        var r = await cliente.PostAsJsonAsync("/repaso/responder", new
        {
            clave = $"correo-sin-respuesta:{contacto}",
            respuesta = 13, // Ya me contestó
        });

        r.IsSuccessStatusCode.Should().BeTrue();
        (await LeerAsync(r)).GetProperty("efecto").GetString().Should().Contain("su ficha");

        // Y no vuelve, porque lo que la calla no es un aplazamiento: es la actividad entrante que se
        // acaba de apuntar en su cronología.
        (await PreguntaAsync(cliente, contacto)).Should().BeNull();

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{contacto}", UriKind.Relative)));
        ficha.GetProperty("cronologia").EnumerateArray()
            .Should().Contain(a => a.GetProperty("cuerpo").GetString()!.Contains("contestado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Abrir_el_correo_NO_cuenta_como_contestar()
    {
        var cliente = await EnEmpresaAsync();
        var (contacto, correoId) = await ConCorreoEnviadoAsync(cliente, hace: 6);

        // Se pide el píxel, como lo pediría su cliente de correo.
        using var alcance = api.Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();
        var token = await bd.Mensajes.IgnoreQueryFilters()
            .Where(c => c.Id == correoId).Select(c => c.TokenApertura).FirstAsync();

        (await api.CreateClient().GetAsync(new Uri($"/e/{token}.gif", UriKind.Relative)))
            .IsSuccessStatusCode.Should().BeTrue();

        // **Esta es la prueba que justifica que una apertura tenga tipo de actividad propio.** Si
        // contara como entrante normal, abrir el correo silenciaría la pregunta justo cuando más hay
        // que llamar: alguien que abre tu correo y no contesta es el mejor candidato del día.
        var pregunta = await PreguntaAsync(cliente, contacto);
        pregunta.Should().NotBeNull();

        var detalle = pregunta!.Value.GetProperty("detalle").GetString()!;
        detalle.Should().Contain("abierto");
        detalle.Should().Contain("no ha contestado");
    }

    [Fact]
    public async Task Con_una_tarea_pendiente_no_se_vuelve_a_preguntar()
    {
        var cliente = await EnEmpresaAsync();
        var (contacto, _) = await ConCorreoEnviadoAsync(cliente, hace: 6);

        await cliente.PostAsJsonAsync("/tareas", new
        {
            contactoId = contacto,
            titulo = "Llamar a Manolo",
            venceEl = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        // La decisión ya está tomada. Volver a preguntar es el «al ratón y al gato» que se arregló en el
        // propio módulo del repaso.
        (await PreguntaAsync(cliente, contacto)).Should().BeNull();
    }

    [Fact]
    public async Task Contestar_crea_la_tarea_de_llamar_hoy()
    {
        var cliente = await EnEmpresaAsync();
        var (contacto, _) = await ConCorreoEnviadoAsync(cliente, hace: 6);

        var r = await cliente.PostAsJsonAsync("/repaso/responder", new
        {
            clave = $"correo-sin-respuesta:{contacto}",
            respuesta = 11, // Le llamo hoy
        });

        r.IsSuccessStatusCode.Should().BeTrue();
        (await LeerAsync(r)).GetProperty("efecto").GetString().Should().Contain("lista de hoy");

        // Y la pregunta desaparece, porque ya hay una tarea.
        (await PreguntaAsync(cliente, contacto)).Should().BeNull();
    }

    [Fact]
    public async Task Solo_se_pregunta_por_el_ultimo_correo_y_una_vez()
    {
        var cliente = await EnEmpresaAsync();
        var (contacto, _) = await ConCorreoEnviadoAsync(cliente, hace: 20);
        await ConCorreoEnviadoAsync(cliente, hace: 6);

        var pila = await LeerAsync(await cliente.GetAsync(new Uri("/repaso", UriKind.Relative)));
        var deCorreo = pila.GetProperty("preguntas").EnumerateArray()
            .Count(p => p.GetProperty("clave").GetString()!.StartsWith("correo-sin-respuesta:", StringComparison.Ordinal));

        // Dos contactos, dos preguntas; nunca dos por el mismo contacto aunque se le haya escrito tres
        // veces. Si no, escribir tres correos llenaría el repaso de la misma persona.
        deCorreo.Should().Be(2);
    }
}
