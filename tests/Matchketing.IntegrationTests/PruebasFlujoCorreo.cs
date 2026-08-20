using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoCorreo(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync(string nombre = "Ribera Correo")
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"co{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        var empresa = await cliente.PostAsJsonAsync("/empresas", new { nombre, provincia = "Valencia" });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(empresa)).GetProperty("token").GetString());
        return cliente;
    }

    private static async Task<Guid> ContactoAsync(HttpClient cliente, string nombre = "Manolo García") =>
        (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre,
            email = $"m{Guid.NewGuid():N}@casamanolo.es",
            telefono = "961234567",
        }))).GetProperty("id").GetGuid();

    /// <summary>
    /// Le da base legal para poder contestarle.
    ///
    /// Hace falta hasta para un correo de «te confirmo la visita», y eso **no es un descuido del módulo
    /// de correo**: es la invariante G1 del de cumplimiento. Un contacto que alguien ha metido a mano no
    /// trae ninguna base legal, y escribirle sin haber apuntado por qué se puede es justo lo que ese
    /// módulo existe para impedir. En la aplicación se hace desde el panel de privacidad de la ficha, y
    /// el borrador dice exactamente esto cuando falta.
    /// </summary>
    private static async Task PermitirAtenderAsync(HttpClient cliente, Guid contacto) =>
        (await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 1, // atender la solicitud
            @base = 2,     // interés legítimo
            canal = "alta manual",
        })).IsSuccessStatusCode.Should().BeTrue();

    private static async Task<Guid> PlantillaAsync(HttpClient cliente, int paraQue = 1) =>
        (await LeerAsync(await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = $"Seguimiento {Guid.NewGuid():N}",
            asunto = "Sobre lo que hablamos",
            cuerpo = "Hola {{nombre}}, te llamo mañana. {{comercial}}",
            paraQue,
        }))).GetProperty("id").GetGuid();

    // ---------- Plantillas ----------

    [Fact]
    public async Task Los_campos_que_se_pueden_usar_son_cuatro()
    {
        var cliente = await EnEmpresaAsync();

        var campos = (await LeerAsync(await cliente.GetAsync(new Uri("/plantillas/campos", UriKind.Relative))))
            .EnumerateArray().Select(c => c.GetProperty("campo").GetString()).ToArray();

        campos.Should().BeEquivalentTo("nombre", "cuenta", "comercial", "empresa");
    }

    [Fact]
    public async Task Un_hueco_inventado_se_rechaza_al_guardar_la_plantilla()
    {
        var cliente = await EnEmpresaAsync();

        var r = await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = "Mala",
            asunto = "Hola",
            cuerpo = "Hola {{apodo}}",
            paraQue = 1,
        });

        // Aquí y no al enviar: si se colara, el correo saldría con las llaves puestas y eso no se
        // descubre hasta que lo lee el cliente.
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("plantilla.campo_desconocido");
    }

    [Fact]
    public async Task Cada_empresa_ve_solo_sus_plantillas()
    {
        var una = await EnEmpresaAsync("Ribera Uno");
        var otra = await EnEmpresaAsync("Ribera Dos");
        await PlantillaAsync(una);

        (await LeerAsync(await otra.GetAsync(new Uri("/plantillas", UriKind.Relative))))
            .EnumerateArray().Should().BeEmpty();
    }

    // ---------- Borrador ----------

    [Fact]
    public async Task El_borrador_rellena_los_huecos_con_los_datos_de_verdad()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        await PermitirAtenderAsync(cliente, contacto);
        var plantilla = await PlantillaAsync(cliente);

        var borrador = await LeerAsync(await cliente.GetAsync(
            new Uri($"/correo/borrador?contactoId={contacto}&plantillaId={plantilla}", UriKind.Relative)));

        // El nombre de pila y no el completo: «Hola Manolo García,» no lo escribiría nadie.
        borrador.GetProperty("cuerpo").GetString().Should().Be("Hola Manolo, te llamo mañana. Marta Ruiz");
        borrador.GetProperty("sePuede").GetBoolean().Should().BeTrue();
    }

    // ---------- Permiso, que es el módulo entero ----------

    [Fact]
    public async Task Un_correo_comercial_sin_consentimiento_no_sale()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        var plantilla = await PlantillaAsync(cliente, paraQue: 2);

        var r = await cliente.PostAsJsonAsync("/correo/enviar", new { contactoId = contacto, plantillaId = plantilla });

        // Un contacto creado a mano no trae consentimiento comercial. Esta es la comprobación que
        // justifica que exista el módulo de cumplimiento.
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("cumplimiento.sin_base_legal");
    }

    [Fact]
    public async Task Con_consentimiento_comercial_si_sale()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        var plantilla = await PlantillaAsync(cliente, paraQue: 2);

        (await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 2, // comercial
            @base = 1,     // consentimiento
            canal = "alta manual",
            textoAceptado = "Acepto recibir información comercial.",
        })).IsSuccessStatusCode.Should().BeTrue();

        var r = await cliente.PostAsJsonAsync("/correo/enviar", new { contactoId = contacto, plantillaId = plantilla });

        // 202 y no 200: está en el buzón de salida, no en la bandeja de nadie.
        r.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task A_quien_se_dio_de_baja_no_se_le_escribe_ni_para_atenderle()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        var plantilla = await PlantillaAsync(cliente);

        var enlace = (await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative))))
            .GetProperty("enlaceBaja").GetString()!;
        var ruta = enlace[enlace.IndexOf("/b/", StringComparison.Ordinal)..];
        (await api.CreateClient().PostAsync(new Uri(ruta, UriKind.Relative), null)).IsSuccessStatusCode.Should().BeTrue();

        var r = await cliente.PostAsJsonAsync("/correo/enviar", new { contactoId = contacto, plantillaId = plantilla });

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("cumplimiento.de_baja");
    }

    // ---------- Encolar y cronología ----------

    [Fact]
    public async Task Sin_ninguna_base_legal_no_se_escribe_ni_para_contestar()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        var plantilla = await PlantillaAsync(cliente);

        var r = await cliente.PostAsJsonAsync("/correo/enviar", new { contactoId = contacto, plantillaId = plantilla });

        // Un contacto metido a mano no trae base legal, y esto es a propósito: si añadiste el correo de
        // alguien a un CRM, tienes que poder decir por qué puedes escribirle. Interés legítimo vale, pero
        // hay que apuntarlo.
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("cumplimiento.sin_base_legal");
    }

    [Fact]
    public async Task El_borrador_explica_que_falta_la_base_legal()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        var plantilla = await PlantillaAsync(cliente);

        var borrador = await LeerAsync(await cliente.GetAsync(
            new Uri($"/correo/borrador?contactoId={contacto}&plantillaId={plantilla}", UriKind.Relative)));

        // El texto se enseña igual; lo que no se puede es enviar. Quien lo lea tiene que entender qué le
        // falta, no encontrarse un botón gris sin explicación.
        borrador.GetProperty("sePuede").GetBoolean().Should().BeFalse();
        borrador.GetProperty("porQueNo").GetString().Should().Contain("base legal");
        borrador.GetProperty("cuerpo").GetString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Enviar_deja_el_correo_en_cola_y_en_la_cronologia()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        await PermitirAtenderAsync(cliente, contacto);
        var plantilla = await PlantillaAsync(cliente);

        await cliente.PostAsJsonAsync("/correo/enviar", new { contactoId = contacto, plantillaId = plantilla });

        var historial = (await LeerAsync(await cliente.GetAsync(new Uri($"/correo/contacto/{contacto}", UriKind.Relative))))
            .EnumerateArray().ToList();

        historial.Should().ContainSingle();
        historial[0].GetProperty("estado").GetString().Should().Be("en cola");
        historial[0].GetProperty("cuerpo").GetString().Should().Contain("Hola Manolo");

        // Y en la ficha, para que el comercial no vuelva a mandarlo pensando que no salió.
        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{contacto}", UriKind.Relative)));
        ficha.GetProperty("cronologia").EnumerateArray()
            .Should().Contain(a => a.GetProperty("tipo").GetInt32() == 3);
    }

    // ---------- El píxel ----------

    [Fact]
    public async Task El_pixel_devuelve_un_gif_aunque_el_token_sea_inventado()
    {
        var r = await api.CreateClient().GetAsync(new Uri("/e/estonoexiste.gif", UriKind.Relative));

        // Contestar 404 a un token inventado confirmaría, por eliminación, cuáles sí existen. Y a quien
        // abre el correo le da igual: solo quiere una imagen.
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("image/gif");
        (await r.Content.ReadAsByteArrayAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task El_pixel_no_se_cachea()
    {
        var r = await api.CreateClient().GetAsync(new Uri("/e/loquesea.gif", UriKind.Relative));

        // Si se cacheara, la segunda apertura no llegaría nunca: ni al servidor ni al recuento.
        r.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task El_pixel_no_pide_sesion()
    {
        // La petición la hace el cliente de correo de la persona, que no tiene sesión ni la va a tener.
        (await api.CreateClient().GetAsync(new Uri("/e/x.gif", UriKind.Relative)))
            .StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    // ---------- Sin sesión ----------

    [Fact]
    public async Task Sin_sesion_no_se_ven_ni_las_plantillas_ni_los_correos()
    {
        var cliente = api.CreateClient();

        (await cliente.GetAsync(new Uri("/plantillas", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await cliente.GetAsync(new Uri($"/correo/contacto/{Guid.NewGuid()}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
