using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoAutomatizacion(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<HttpClient> EnEmpresaAsync(string nombre = "Ribera Reglas")
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"au{Guid.NewGuid():N}@ribera.es",
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

    private static async Task<Guid> ReglaAsync(
        HttpClient cliente, string disparador, object[] condiciones, object[] acciones, bool encender = true)
    {
        var r = await cliente.PostAsJsonAsync("/reglas", new
        {
            nombre = $"Regla {Guid.NewGuid():N}",
            disparador,
            condiciones,
            acciones,
        });

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await LeerAsync(r)).GetProperty("id").GetGuid();

        if (encender)
        {
            (await cliente.PostAsync(new Uri($"/reglas/{id}/encender?encender=true", UriKind.Relative), null))
                .IsSuccessStatusCode.Should().BeTrue();
        }

        return id;
    }

    private static async Task<Guid> ContactoAsync(HttpClient cliente, Guid? cuentaId = null) =>
        (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García",
            email = $"m{Guid.NewGuid():N}@casamanolo.es",
            telefono = "961234567",
            origen = "feria",
            cuentaId,
        }))).GetProperty("id").GetGuid();

    private static async Task<IReadOnlyList<JsonElement>> EjecucionesAsync(HttpClient cliente, Guid regla) =>
        (await LeerAsync(await cliente.GetAsync(new Uri($"/reglas/{regla}/ejecuciones", UriKind.Relative))))
            .EnumerateArray().ToList();

    // ---------- Gestión ----------

    [Fact]
    public async Task Una_regla_nace_apagada_y_lo_dice()
    {
        var cliente = await EnEmpresaAsync();

        var r = await cliente.PostAsJsonAsync("/reglas", new
        {
            nombre = "Leads de feria",
            disparador = "lead.creado",
            condiciones = Array.Empty<object>(),
            acciones = new[] { new { tipo = 1, texto = "Llamar", referencia = (Guid?)null, numero = 0 } },
        });

        var cuerpo = await LeerAsync(r);
        cuerpo.GetProperty("aviso").GetString().Should().Contain("apagada");
        cuerpo.GetProperty("leida").GetString().Should().StartWith("Si pasa «lead.creado»");

        var lista = (await LeerAsync(await cliente.GetAsync(new Uri("/reglas", UriKind.Relative)))).EnumerateArray().ToList();
        lista.Single().GetProperty("activa").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Una_regla_apagada_no_dispara()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "lead.creado", [],
            [new { tipo = 1, texto = "Llamar", referencia = (Guid?)null, numero = 0 }], encender: false);

        await ContactoAsync(cliente);

        (await EjecucionesAsync(cliente, regla)).Should().BeEmpty();
    }

    [Fact]
    public async Task Un_disparador_que_no_existe_se_rechaza()
    {
        var cliente = await EnEmpresaAsync();

        var r = await cliente.PostAsJsonAsync("/reglas", new
        {
            nombre = "Mala",
            disparador = "cuando-me-apetezca",
            condiciones = Array.Empty<object>(),
            acciones = new[] { new { tipo = 4, texto = "Hola", referencia = (Guid?)null, numero = (int?)null } },
        });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("regla.disparador_desconocido");
    }

    [Fact]
    public async Task Cambiar_una_regla_la_apaga()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "lead.creado", [],
            [new { tipo = 4, texto = "Nota", referencia = (Guid?)null, numero = (int?)null }]);

        (await cliente.PutAsJsonAsync($"/reglas/{regla}", new
        {
            nombre = "Cambiada",
            disparador = "lead.creado",
            condiciones = Array.Empty<object>(),
            acciones = new[] { new { tipo = 4, texto = "Otra nota", referencia = (Guid?)null, numero = (int?)null } },
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var lista = (await LeerAsync(await cliente.GetAsync(new Uri("/reglas", UriKind.Relative)))).EnumerateArray().ToList();
        lista.Single().GetProperty("activa").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Cada_empresa_ve_solo_sus_reglas()
    {
        var una = await EnEmpresaAsync("Ribera Uno");
        var otra = await EnEmpresaAsync("Ribera Dos");
        await ReglaAsync(una, "lead.creado", [],
            [new { tipo = 4, texto = "Nota", referencia = (Guid?)null, numero = (int?)null }]);

        (await LeerAsync(await otra.GetAsync(new Uri("/reglas", UriKind.Relative)))).EnumerateArray().Should().BeEmpty();
    }

    // ---------- Disparo de verdad ----------

    [Fact]
    public async Task Un_lead_nuevo_dispara_la_regla_y_crea_la_tarea()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "lead.creado", [],
            [new { tipo = 1, texto = "Llamar al lead nuevo", referencia = (Guid?)null, numero = 0 }]);

        var contacto = await ContactoAsync(cliente);

        var ejecuciones = await EjecucionesAsync(cliente, regla);
        ejecuciones.Should().ContainSingle();
        ejecuciones[0].GetProperty("queHizo").GetString().Should().Contain("Llamar al lead nuevo");
        ejecuciones[0].GetProperty("contactoId").GetGuid().Should().Be(contacto);

        // Y la tarea existe de verdad, en Hoy.
        var hoy = await LeerAsync(await cliente.GetAsync(new Uri("/hoy", UriKind.Relative)));
        JsonSerializer.Serialize(hoy).Should().Contain("Llamar al lead nuevo");
    }

    [Fact]
    public async Task Solo_dispara_si_se_cumple_la_condicion()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "lead.creado",
            [new { campo = 2, operador = 1, valor = "web" }],   // origen es «web»
            [new { tipo = 4, texto = "Vino de la web", referencia = (Guid?)null, numero = (int?)null }]);

        await ContactoAsync(cliente);   // origen «feria»

        (await EjecucionesAsync(cliente, regla)).Should().BeEmpty();
    }

    [Fact]
    public async Task Actua_una_sola_vez_por_contacto_aunque_se_reprocese()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "lead.creado", [],
            [new { tipo = 4, texto = "Nota", referencia = (Guid?)null, numero = (int?)null }]);

        var contacto = await ContactoAsync(cliente);

        // Otra operación de negocio sobre el mismo contacto: la ejecución no se repite. La garantía es un
        // índice único en la base, no un `if`.
        await cliente.PostAsJsonAsync($"/contactos/{contacto}/notas", new { cuerpo = "Una nota a mano" });

        (await EjecucionesAsync(cliente, regla)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Ganar_una_oportunidad_desde_el_repaso_tambien_dispara()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "oportunidad.ganada", [],
            [new { tipo = 1, texto = "Pedir referencia", referencia = (Guid?)null, numero = 30 }]);

        var contacto = await ContactoAsync(cliente);
        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Cocina completa",
            importe = 18400m,
            previstaCierre = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10)),
        }))).GetProperty("id").GetGuid();

        // Por el camino del repaso, no por el del tablero. **Esta es la prueba que justifica colgar las
        // reglas de los eventos de dominio**: el repaso no sabe que existen las automatizaciones.
        (await cliente.PostAsJsonAsync("/repaso/responder", new
        {
            clave = $"cierre-pasado:{oportunidad}",
            respuesta = 8, // Ganada
        })).IsSuccessStatusCode.Should().BeTrue();

        var ejecuciones = await EjecucionesAsync(cliente, regla);
        ejecuciones.Should().ContainSingle();
        ejecuciones[0].GetProperty("queHizo").GetString().Should().Contain("Pedir referencia");
    }

    [Fact]
    public async Task Una_condicion_sobre_el_importe_filtra_de_verdad()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "oportunidad.ganada",
            [new { campo = 4, operador = 4, valor = "20000" }],   // importe mayor que 20000
            [new { tipo = 4, texto = "Venta grande", referencia = (Guid?)null, numero = (int?)null }]);

        var contacto = await ContactoAsync(cliente);
        var pequena = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto, titulo = "Pequeña", importe = 5000m,
        }))).GetProperty("id").GetGuid();

        await cliente.PostAsync(new Uri($"/oportunidades/{pequena}/ganar", UriKind.Relative), null);
        (await EjecucionesAsync(cliente, regla)).Should().BeEmpty();

        var grande = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto, titulo = "Grande", importe = 42000m,
        }))).GetProperty("id").GetGuid();

        await cliente.PostAsync(new Uri($"/oportunidades/{grande}/ganar", UriKind.Relative), null);
        (await EjecucionesAsync(cliente, regla)).Should().ContainSingle();
    }

    // ---------- Lo que no puede pasar ----------

    [Fact]
    public async Task Una_regla_no_dispara_a_otra()
    {
        var cliente = await EnEmpresaAsync();

        // La primera crea una tarea al entrar un lead. La segunda escucha «lead.creado» también: si los
        // eventos de las acciones se procesaran, dos reglas podrían peloteárselos para siempre.
        var primera = await ReglaAsync(cliente, "lead.creado", [],
            [new { tipo = 1, texto = "Llamar", referencia = (Guid?)null, numero = 0 }]);
        var segunda = await ReglaAsync(cliente, "lead.creado", [],
            [new { tipo = 4, texto = "Nota de la segunda", referencia = (Guid?)null, numero = (int?)null }]);

        await ContactoAsync(cliente);

        // Las dos actúan una vez, por el evento original. Ninguna se dispara por lo que hizo la otra.
        (await EjecucionesAsync(cliente, primera)).Should().HaveCount(1);
        (await EjecucionesAsync(cliente, segunda)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Una_regla_no_puede_mandar_un_correo_sin_permiso()
    {
        var cliente = await EnEmpresaAsync();

        var plantilla = (await LeerAsync(await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = "Acuse de recibo",
            asunto = "Hemos recibido tu consulta",
            cuerpo = "Hola {{nombre}}, te llamamos hoy.",
            paraQue = 1,
        }))).GetProperty("id").GetGuid();

        var regla = await ReglaAsync(cliente, "lead.creado", [],
        [
            new { tipo = 3, texto = (string?)null, referencia = (Guid?)plantilla, numero = (int?)null },
            new { tipo = 1, texto = "Llamar", referencia = (Guid?)null, numero = 0 },
        ]);

        var contacto = await ContactoAsync(cliente);

        // Un contacto creado a mano no trae base legal. **Una automatización no es una excusa para
        // saltarse el RGPD**: el correo no sale, y queda escrito que no salió.
        //
        // Se comprueba que lo que falla es **el correo y solo el correo**. Antes esta prueba solo miraba
        // que apareciera «no se pudo», y pasaba por el motivo equivocado: fallaban también las otras
        // acciones porque el contacto todavía no estaba guardado cuando la regla actuaba. Una aserción
        // que se cumple por casualidad es peor que no tenerla.
        var ejecucion = (await EjecucionesAsync(cliente, regla)).Single();
        var queHizo = ejecucion.GetProperty("queHizo").GetString()!;
        queHizo.Should().Contain("no se pudo mandarle un correo");

        var correos = (await LeerAsync(await cliente.GetAsync(new Uri($"/correo/contacto/{contacto}", UriKind.Relative))))
            .EnumerateArray().ToList();
        correos.Should().BeEmpty();

        // Pero la tarea sí se crea: es justo cuando más hay que llamar.
        ejecucion.GetProperty("queHizo").GetString().Should().Contain("Llamar");
    }

    [Fact]
    public async Task Con_permiso_la_regla_si_manda_el_correo()
    {
        var cliente = await EnEmpresaAsync();

        var plantilla = (await LeerAsync(await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = "Acuse de recibo",
            asunto = "Hemos recibido tu consulta",
            cuerpo = "Hola {{nombre}}, te llamamos hoy.",
            paraQue = 1,
        }))).GetProperty("id").GetGuid();

        // La regla escucha «oportunidad.ganada» para poder dar el permiso antes de que dispare.
        var regla = await ReglaAsync(cliente, "oportunidad.ganada", [],
            [new { tipo = 3, texto = (string?)null, referencia = (Guid?)plantilla, numero = (int?)null }]);

        var contacto = await ContactoAsync(cliente);
        (await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 1,
            @base = 2,
            canal = "alta manual",
        })).IsSuccessStatusCode.Should().BeTrue();

        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto, titulo = "Cocina", importe = 18400m,
        }))).GetProperty("id").GetGuid();

        await cliente.PostAsync(new Uri($"/oportunidades/{oportunidad}/ganar", UriKind.Relative), null);

        (await EjecucionesAsync(cliente, regla)).Single()
            .GetProperty("queHizo").GetString().Should().Contain("correo encolado");

        (await LeerAsync(await cliente.GetAsync(new Uri($"/correo/contacto/{contacto}", UriKind.Relative))))
            .EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Las_cuatro_acciones_funcionan_sobre_un_contacto_recien_creado()
    {
        var cliente = await EnEmpresaAsync();

        var plantilla = (await LeerAsync(await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = "Acuse", asunto = "Recibido", cuerpo = "Hola {{nombre}}.", paraQue = 1,
        }))).GetProperty("id").GetGuid();

        var comerciales = (await LeerAsync(await cliente.GetAsync(new Uri("/match/comerciales", UriKind.Relative))))
            .EnumerateArray().ToList();
        comerciales.Should().NotBeEmpty();
        var comercial = comerciales[0].GetProperty("id").GetGuid();

        var regla = await ReglaAsync(cliente, "lead.creado", [],
        [
            new { tipo = 1, texto = "Llamar", referencia = (Guid?)null, numero = 0 },
            new { tipo = 2, texto = (string?)null, referencia = (Guid?)comercial, numero = (int?)null },
            new { tipo = 4, texto = "Vino de la feria", referencia = (Guid?)null, numero = (int?)null },
            new { tipo = 3, texto = (string?)null, referencia = (Guid?)plantilla, numero = (int?)null },
        ]);

        await ContactoAsync(cliente);

        // **Esta es la prueba que descubrió el fallo del momento.** Las reglas se ejecutaban antes de
        // guardar, así que tres de las cuatro acciones cargaban de la base un contacto que todavía no
        // existía y fallaban en silencio: solo funcionaba crear la tarea, que es la única que no consulta
        // nada. Y solo con los disparadores de contacto.
        var queHizo = (await EjecucionesAsync(cliente, regla)).Single().GetProperty("queHizo").GetString()!;

        queHizo.Should().Contain("tarea «Llamar»");
        queHizo.Should().Contain("asignado a un comercial");
        queHizo.Should().Contain("nota apuntada");

        // El correo es el único que no sale, y por el motivo correcto: no hay base legal. Lo demás sí.
        queHizo.Should().Contain("no se pudo mandarle un correo");
    }

    // ---------- Ensayo ----------

    [Fact]
    public async Task El_ensayo_dice_que_haria_y_no_hace_nada()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "lead.creado",
            [new { campo = 2, operador = 1, valor = "feria" }],
            [new { tipo = 1, texto = "Llamar", referencia = (Guid?)null, numero = 0 }], encender: false);

        var contacto = await ContactoAsync(cliente);

        var r = await LeerAsync(await cliente.GetAsync(
            new Uri($"/reglas/{regla}/ensayo?contactoId={contacto}", UriKind.Relative)));

        r.GetProperty("aplicaria").GetBoolean().Should().BeTrue();
        r.GetProperty("haria").EnumerateArray().First().GetString().Should().Contain("Llamar");

        // Y no ha hecho nada: es la única forma de probar una regla sin encenderla.
        (await EjecucionesAsync(cliente, regla)).Should().BeEmpty();
    }

    [Fact]
    public async Task El_ensayo_dice_que_condicion_falla()
    {
        var cliente = await EnEmpresaAsync();
        var regla = await ReglaAsync(cliente, "lead.creado",
            [new { campo = 2, operador = 1, valor = "web" }],
            [new { tipo = 4, texto = "Nota", referencia = (Guid?)null, numero = (int?)null }], encender: false);

        var contacto = await ContactoAsync(cliente);

        var r = await LeerAsync(await cliente.GetAsync(
            new Uri($"/reglas/{regla}/ensayo?contactoId={contacto}", UriKind.Relative)));

        r.GetProperty("aplicaria").GetBoolean().Should().BeFalse();
        r.GetProperty("porQueNo").GetString().Should().Contain("origen es «web»");
    }

    [Fact]
    public async Task Sin_sesion_no_se_ve_nada()
    {
        (await api.CreateClient().GetAsync(new Uri("/reglas", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
