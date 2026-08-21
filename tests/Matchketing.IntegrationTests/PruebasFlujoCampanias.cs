using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Campanias.Aplicacion;
using Matchketing.Nucleo.Comun;
using Matchketing.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// El módulo entero, contra una base de datos real.
///
/// Las pruebas que de verdad importan aquí son las de consentimiento. Un módulo de campañas que se
/// equivoque en eso no manda «un correo de más»: manda una infracción, cuatrocientas veces, firmada con
/// el nombre del cliente. Así que están escritas al revés de lo habitual: no comprueban que el correo
/// llega, comprueban que **no** llega cuando no debe, y que queda escrito por qué.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoCampanias(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync(string nombre = "Ribera Campañas")
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"ca{Guid.NewGuid():N}@ribera.es",
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

    /// <summary>Le da permiso para comunicaciones comerciales, que es el que exige una campaña.</summary>
    private static async Task PermitirComercialAsync(HttpClient cliente, Guid contacto) =>
        (await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 2, // comercial
            @base = 1,     // consentimiento
            canal = "formulario web",
            textoAceptado = "Acepto recibir ofertas de Instalaciones Ribera.",
        })).IsSuccessStatusCode.Should().BeTrue();

    private static async Task<Guid> PlantillaAsync(HttpClient cliente, int paraQue = 2) =>
        (await LeerAsync(await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = $"Oferta {Guid.NewGuid():N}",
            asunto = "Una oferta para ti",
            cuerpo = "Hola {{nombre}}, te cuento la oferta de este mes. {{comercial}}",
            paraQue,
        }))).GetProperty("id").GetGuid();

    private static async Task<Guid> SegmentoAsync(HttpClient cliente, object criterios) =>
        (await LeerAsync(await cliente.PostAsJsonAsync("/segmentos", criterios))).GetProperty("id").GetGuid();

    private static async Task<Guid> CampaniaAsync(HttpClient cliente, Guid segmento, Guid plantilla) =>
        (await LeerAsync(await cliente.PostAsJsonAsync("/campanias", new
        {
            nombre = "Oferta de primavera",
            segmentoId = segmento,
            plantillaId = plantilla,
        }))).GetProperty("id").GetGuid();

    /// <summary>
    /// Hace lo que hace el trabajo de fondo: encola el siguiente lote de las campañas en marcha.
    ///
    /// Fija la empresa igual que <c>TrabajoPeriodico</c> —sacándola de la propia campaña— porque sin
    /// empresa activa el filtro global falla cerrado y el servicio no vería ni una fila; se quedaría tan
    /// callado como si no hubiera nada que hacer, y la prueba pasaría sin haber comprobado nada.
    /// </summary>
    private async Task<PasadaCampanias> PasadaAsync(Guid campaniaId)
    {
        using var alcance = api.Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();

        var empresaId = await bd.Database
            .SqlQuery<Guid>($"SELECT empresa_id AS \"Value\" FROM campania.campania WHERE id = {campaniaId}")
            .SingleAsync();

        alcance.ServiceProvider.GetRequiredService<IContextoEmpresaPublico>().FijarEmpresa(empresaId);
        await bd.ReaplicarEmpresaAsync();

        var servicio = alcance.ServiceProvider.GetRequiredService<ServicioCampanias>();
        var r = await servicio.EncolarLoteAsync();
        await alcance.ServiceProvider.GetRequiredService<Identidad.Aplicacion.IUnidadDeTrabajo>()
            .GuardarCambiosAsync();

        return r;
    }

    // ---------- Segmentos ----------

    [Fact]
    public async Task Un_segmento_sin_criterios_se_rechaza_con_su_motivo()
    {
        var cliente = await EnEmpresaAsync();

        var r = await cliente.PostAsJsonAsync("/segmentos", new { nombre = "Todos" });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("segmento.sin_criterios");
    }

    [Fact]
    public async Task El_segmento_cuenta_a_los_de_hoy_y_deja_fuera_a_los_de_baja_y_a_los_sin_correo()
    {
        var cliente = await EnEmpresaAsync();

        var conCorreo = await ContactoAsync(cliente, "Amparo Sanchis");
        var sinCorreo = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Sin correo",
            telefono = "961111111",
        }))).GetProperty("id").GetGuid();

        var deBaja = await ContactoAsync(cliente, "Se dio de baja");
        (await cliente.PutAsJsonAsync($"/contactos/{deBaja}/estado", new { estado = 4 }))
            .IsSuccessStatusCode.Should().BeTrue();

        var segmento = await SegmentoAsync(cliente, new { nombre = "Todos los leads", estado = 1 });

        var previa = await LeerAsync(await cliente.GetAsync(new Uri($"/segmentos/{segmento}/previa", UriKind.Relative)));

        // Solo el que tiene correo y no está de baja. Las otras dos exclusiones no son criterios que se
        // puedan quitar: quien está de baja es un muro, y quien no tiene correo no es un destinatario.
        previa.GetProperty("cuantos").GetInt32().Should().Be(1);
        previa.GetProperty("muestra").EnumerateArray()
            .Select(m => m.GetProperty("contactoId").GetGuid())
            .Should().BeEquivalentTo([conCorreo]);

        sinCorreo.Should().NotBeEmpty();
    }

    [Fact]
    public async Task La_provincia_sale_de_la_cuenta_asi_que_un_particular_no_entra()
    {
        var cliente = await EnEmpresaAsync();

        var cuenta = (await LeerAsync(await cliente.PostAsJsonAsync("/cuentas", new
        {
            nombre = "Climatizaciones Sanchis",
            provincia = "Valencia",
        }))).GetProperty("id").GetGuid();

        var deEmpresa = await ContactoAsync(cliente, "Amparo Sanchis");
        (await cliente.PutAsJsonAsync($"/contactos/{deEmpresa}", new
        {
            nombre = "Amparo Sanchis",
            email = $"a{Guid.NewGuid():N}@sanchis.es",
            cuentaId = cuenta,
        })).IsSuccessStatusCode.Should().BeTrue();

        await ContactoAsync(cliente, "Particular sin cuenta");

        var segmento = await SegmentoAsync(cliente, new { nombre = "De Valencia", provincia = "valencia" });
        var previa = await LeerAsync(await cliente.GetAsync(new Uri($"/segmentos/{segmento}/previa", UriKind.Relative)));

        // Uno, y en minúsculas: la comparación la hace PostgreSQL con `ILIKE`, porque con
        // `InvariantGlobalization` activo comparar en minúsculas en el servidor no es de fiar.
        previa.GetProperty("cuantos").GetInt32().Should().Be(1);
        previa.GetProperty("frase").GetString().Should().Be("contactos, de valencia");
    }

    [Fact]
    public async Task Un_segmento_que_ya_lanzo_una_campania_no_se_borra()
    {
        var cliente = await EnEmpresaAsync();
        var segmento = await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 });
        await CampaniaAsync(cliente, segmento, await PlantillaAsync(cliente));

        var r = await cliente.DeleteAsync(new Uri($"/segmentos/{segmento}", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("segmento.en_uso");
    }

    // ---------- Campañas ----------

    [Fact]
    public async Task Una_campania_no_se_puede_hacer_con_una_plantilla_de_atender_solicitudes()
    {
        var cliente = await EnEmpresaAsync();
        var segmento = await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 });
        var deAtender = await PlantillaAsync(cliente, paraQue: 1);

        var r = await cliente.PostAsJsonAsync("/campanias", new
        {
            nombre = "Oferta",
            segmentoId = segmento,
            plantillaId = deAtender,
        });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("campania.plantilla_no_comercial");
    }

    [Fact]
    public async Task Lanzar_congela_la_audiencia_y_contesta_202_porque_todavia_no_ha_salido_nada()
    {
        var cliente = await EnEmpresaAsync();
        var uno = await ContactoAsync(cliente, "Amparo Sanchis");
        var dos = await ContactoAsync(cliente, "Consuelo Beltrán");
        await PermitirComercialAsync(cliente, uno);
        await PermitirComercialAsync(cliente, dos);

        var campania = await CampaniaAsync(
            cliente,
            await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 }),
            await PlantillaAsync(cliente));

        var r = await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);

        r.StatusCode.Should().Be(HttpStatusCode.Accepted, "lanzar no manda nada: congela a quién se le va a mandar");
        var cuerpo = await LeerAsync(r);
        cuerpo.GetProperty("destinatarios").GetInt32().Should().Be(2);
        cuerpo.GetProperty("estado").GetString().Should().Be("enviando");

        // Y no hay ni un correo en el buzón de salida todavía.
        foreach (var contacto in new[] { uno, dos })
        {
            (await LeerAsync(await cliente.GetAsync(new Uri($"/correo/contacto/{contacto}", UriKind.Relative))))
                .EnumerateArray().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Al_que_no_ha_dado_su_consentimiento_comercial_no_se_le_manda_y_queda_escrito_por_que()
    {
        // La prueba central del módulo. Los dos contactos están en el segmento; uno dio permiso y el otro
        // no. Lo que se comprueba no es que al primero le llegue, es que al segundo **no** le llegue y que
        // la ficha lo diga con palabras, porque esa frase es la respuesta que hay que dar cuando alguien
        // pregunta —el cliente o la Agencia—.
        var cliente = await EnEmpresaAsync();
        var conPermiso = await ContactoAsync(cliente, "Amparo Sanchis");
        var sinPermiso = await ContactoAsync(cliente, "Consuelo Beltrán");
        await PermitirComercialAsync(cliente, conPermiso);

        var campania = await CampaniaAsync(
            cliente,
            await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 }),
            await PlantillaAsync(cliente));

        await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);
        var pasada = await PasadaAsync(campania);

        pasada.Encolados.Should().Be(1);
        pasada.Excluidos.Should().Be(1);

        var detalle = await LeerAsync(await cliente.GetAsync(new Uri($"/campanias/{campania}", UriKind.Relative)));
        detalle.GetProperty("campania").GetProperty("encolados").GetInt32().Should().Be(1);
        detalle.GetProperty("campania").GetProperty("excluidos").GetInt32().Should().Be(1);
        detalle.GetProperty("campania").GetProperty("estado").GetString().Should().Be("enviada");

        var motivos = detalle.GetProperty("porQueNoLlego").EnumerateArray().ToList();
        motivos.Should().ContainSingle();
        motivos[0].GetProperty("cuantos").GetInt32().Should().Be(1);
        motivos[0].GetProperty("motivo").GetString().Should().NotBeNullOrWhiteSpace();

        // Y en el buzón de salida hay exactamente un correo: el del que dio permiso.
        (await LeerAsync(await cliente.GetAsync(new Uri($"/correo/contacto/{conPermiso}", UriKind.Relative))))
            .EnumerateArray().Should().ContainSingle();
        (await LeerAsync(await cliente.GetAsync(new Uri($"/correo/contacto/{sinPermiso}", UriKind.Relative))))
            .EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Quien_retira_el_permiso_entre_lanzar_y_encolar_no_recibe_el_correo()
    {
        // Entre congelar la audiencia y encolar el correo pasan minutos, y en esos minutos alguien puede
        // retirar su consentimiento. La comprobación que cuenta es la del momento de encolar, no la del
        // momento de lanzar: si fuese la del lanzamiento, esta persona recibiría publicidad después de
        // haber dicho que no la quiere.
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente, "Amparo Sanchis");
        await PermitirComercialAsync(cliente, contacto);

        var campania = await CampaniaAsync(
            cliente,
            await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 }),
            await PlantillaAsync(cliente));

        await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);

        // Se lo piensa mejor, después de que la campaña ya lo tenga en su audiencia.
        (await cliente.DeleteAsync(new Uri(
            $"/cumplimiento/contactos/{contacto}/consentimientos?finalidad=2", UriKind.Relative)))
            .IsSuccessStatusCode.Should().BeTrue();

        var pasada = await PasadaAsync(campania);

        pasada.Encolados.Should().Be(0);
        pasada.Excluidos.Should().Be(1);

        (await LeerAsync(await cliente.GetAsync(new Uri($"/correo/contacto/{contacto}", UriKind.Relative))))
            .EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task La_ficha_sigue_diciendo_a_quien_apunto_aunque_el_segmento_cambie_despues()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente, "Amparo Sanchis");
        await PermitirComercialAsync(cliente, contacto);

        var segmento = await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 });
        var campania = await CampaniaAsync(cliente, segmento, await PlantillaAsync(cliente));
        await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);

        // Se edita el segmento después de lanzar.
        (await cliente.PutAsJsonAsync($"/segmentos/{segmento}", new
        {
            nombre = "Perdidos de Teruel",
            estado = 3,
            provincia = "Teruel",
        })).IsSuccessStatusCode.Should().BeTrue();

        var detalle = await LeerAsync(await cliente.GetAsync(new Uri($"/campanias/{campania}", UriKind.Relative)));

        detalle.GetProperty("segmentoAlLanzar").GetString().Should().Be("Leads: leads");
    }

    [Fact]
    public async Task Una_campania_lanzada_no_se_edita_ni_se_borra()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        await PermitirComercialAsync(cliente, contacto);

        var segmento = await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 });
        var plantilla = await PlantillaAsync(cliente);
        var campania = await CampaniaAsync(cliente, segmento, plantilla);
        await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);

        var editada = await cliente.PutAsJsonAsync($"/campanias/{campania}", new
        {
            nombre = "Otro nombre",
            segmentoId = segmento,
            plantillaId = plantilla,
        });
        editada.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var borrada = await cliente.DeleteAsync(new Uri($"/campanias/{campania}", UriKind.Relative));
        borrada.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LeerAsync(borrada)).GetProperty("codigo").GetString().Should().Be("campania.ya_lanzada");
    }

    [Fact]
    public async Task Un_borrador_si_se_borra()
    {
        var cliente = await EnEmpresaAsync();
        var campania = await CampaniaAsync(
            cliente,
            await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 }),
            await PlantillaAsync(cliente));

        (await cliente.DeleteAsync(new Uri($"/campanias/{campania}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.GetAsync(new Uri($"/campanias/{campania}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task No_se_lanza_una_campania_a_un_segmento_que_hoy_no_tiene_a_nadie()
    {
        var cliente = await EnEmpresaAsync();
        var campania = await CampaniaAsync(
            cliente,
            await SegmentoAsync(cliente, new { nombre = "Clientes", estado = 2 }),
            await PlantillaAsync(cliente));

        var r = await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("campania.segmento_vacio");
    }

    [Fact]
    public async Task Detener_una_campania_deja_de_encolar_y_la_suma_sigue_cuadrando()
    {
        var cliente = await EnEmpresaAsync();
        for (var i = 0; i < 3; i++)
        {
            await PermitirComercialAsync(cliente, await ContactoAsync(cliente, "Contacto " + i));
        }

        var campania = await CampaniaAsync(
            cliente,
            await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 }),
            await PlantillaAsync(cliente));

        await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);

        (await cliente.PostAsync(new Uri($"/campanias/{campania}/detener", UriKind.Relative), null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var pasada = await PasadaAsync(campania);
        pasada.Encolados.Should().Be(0, "una campaña detenida no encola nada más");

        var detalle = await LeerAsync(await cliente.GetAsync(new Uri($"/campanias/{campania}", UriKind.Relative)));
        var c = detalle.GetProperty("campania");
        c.GetProperty("estado").GetString().Should().Be("detenida");
        c.GetProperty("excluidos").GetInt32().Should().Be(3);
        c.GetProperty("pendientes").GetInt32().Should().Be(0, "en la ficha no puede quedar nadie sin explicación");
    }

    [Fact]
    public async Task El_correo_de_una_campania_lo_firma_quien_la_lanzo_y_se_apunta_en_la_cronologia()
    {
        // Dos cosas en una: el hueco `{{comercial}}` sale con el nombre de quien lanzó la campaña —no con
        // «el sistema»— y el correo aparece en la cronología del contacto. Lo segundo importa porque el
        // comercial que llame mañana tiene que ver que a esa persona ya le escribimos.
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente, "Amparo Sanchis");
        await PermitirComercialAsync(cliente, contacto);

        var campania = await CampaniaAsync(
            cliente,
            await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 }),
            await PlantillaAsync(cliente));

        await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);
        await PasadaAsync(campania);

        var correos = (await LeerAsync(await cliente.GetAsync(
            new Uri($"/correo/contacto/{contacto}", UriKind.Relative)))).EnumerateArray().ToList();

        correos.Should().ContainSingle();
        correos[0].GetProperty("cuerpo").GetString().Should().Contain("Marta Ruiz");
        correos[0].GetProperty("estado").GetString().Should().Be("en cola");

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{contacto}", UriKind.Relative)));
        ficha.GetProperty("cronologia").EnumerateArray()
            .Should().Contain(a => a.GetProperty("tipo").GetInt32() == 3, "un correo de campaña es un correo");
    }

    [Fact]
    public async Task Una_pasada_no_encola_dos_veces_a_la_misma_persona()
    {
        var cliente = await EnEmpresaAsync();
        var contacto = await ContactoAsync(cliente);
        await PermitirComercialAsync(cliente, contacto);

        var campania = await CampaniaAsync(
            cliente,
            await SegmentoAsync(cliente, new { nombre = "Leads", estado = 1 }),
            await PlantillaAsync(cliente));

        await cliente.PostAsync(new Uri($"/campanias/{campania}/lanzar", UriKind.Relative), null);

        await PasadaAsync(campania);
        var segunda = await PasadaAsync(campania);

        segunda.Encolados.Should().Be(0, "la campaña ya está cerrada y no queda nadie pendiente");
        (await LeerAsync(await cliente.GetAsync(new Uri($"/correo/contacto/{contacto}", UriKind.Relative))))
            .EnumerateArray().Should().ContainSingle();
    }

    // ---------- Permisos ----------

    [Fact]
    public async Task Un_comercial_ve_las_campanias_y_no_las_lanza()
    {
        // Escribirle a un cliente y escribirle a cuatrocientos en nombre de la empresa no son lo mismo, y
        // por eso `campania.gestionar` es un permiso propio y no cae dentro de `contacto.gestionar`. Ver
        // una sí le hace falta: si a su cliente le llegó un correo de campaña, tiene que saberlo antes de
        // llamarle.
        var propietario = await EnEmpresaAsync("Ribera Permisos");
        var segmento = await SegmentoAsync(propietario, new { nombre = "Leads", estado = 1 });
        var plantilla = await PlantillaAsync(propietario);

        var invitacion = await LeerAsync(await propietario.PostAsJsonAsync("/equipo/invitaciones", new
        {
            email = $"comercial{Guid.NewGuid():N}@ribera.es",
            rol = 2, // comercial
        }));

        var comercial = api.CreateClient();
        var aceptada = await LeerAsync(await comercial.PostAsJsonAsync(
            $"/invitaciones/{invitacion.GetProperty("token").GetString()}",
            new { nombre = "Rocío Ferrán", contrasena = "Levante2026" }));
        comercial.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", aceptada.GetProperty("token").GetString());

        (await comercial.GetAsync(new Uri("/campanias", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await comercial.GetAsync(new Uri("/segmentos", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await comercial.PostAsJsonAsync("/campanias", new
        {
            nombre = "La mía",
            segmentoId = segmento,
            plantillaId = plantilla,
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await comercial.PostAsJsonAsync("/segmentos", new { nombre = "El mío", estado = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Las_campanias_de_una_empresa_no_se_ven_desde_otra()
    {
        var una = await EnEmpresaAsync("Ribera Uno");
        var otra = await EnEmpresaAsync("Ribera Dos");

        var campania = await CampaniaAsync(
            una,
            await SegmentoAsync(una, new { nombre = "Leads", estado = 1 }),
            await PlantillaAsync(una));

        (await LeerAsync(await otra.GetAsync(new Uri("/campanias", UriKind.Relative))))
            .EnumerateArray().Should().BeEmpty();
        (await LeerAsync(await otra.GetAsync(new Uri("/segmentos", UriKind.Relative))))
            .EnumerateArray().Should().BeEmpty();

        (await otra.GetAsync(new Uri($"/campanias/{campania}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
