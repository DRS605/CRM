using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoRepaso(ApiDePrueba api)
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

    private static async Task<Guid> ContactoAsync(HttpClient cliente, string nombre)
    {
        var r = await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre,
            email = $"l{Guid.NewGuid():N}@correo.es",
            telefono = "600112233",
        });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PilaAsync(HttpClient cliente) =>
        await LeerAsync(await cliente.GetAsync(new Uri("/repaso", UriKind.Relative)));

    private static Task<HttpResponseMessage> ResponderAsync(HttpClient cliente, string clave, int respuesta, int? motivo = null) =>
        cliente.PostAsJsonAsync("/repaso/responder", new { clave, respuesta, motivo });

    /// <summary>
    /// Envejece filas a mano. El reloj del sistema no se puede mover en una prueba de integración, y
    /// esperar una semana a que pase no era una opción.
    /// </summary>
    private async Task EnvejecerAsync(string sql)
    {
        using var alcance = api.Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();
        await bd.Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task Una_empresa_nueva_esta_al_dia()
    {
        var cliente = await EnEmpresaAsync("Ribera Repaso Vacío");

        var pila = await PilaAsync(cliente);

        pila.GetProperty("alDia").GetBoolean().Should().BeTrue();
        pila.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Un_lead_sin_tocar_genera_una_pregunta_con_su_motivo()
    {
        var cliente = await EnEmpresaAsync("Ribera Repaso Lead");
        await ContactoAsync(cliente, "Manolo García");

        var pila = await PilaAsync(cliente);

        var pregunta = pila.GetProperty("preguntas").EnumerateArray().Single();
        pregunta.GetProperty("tipo").GetInt32().Should().Be(2); // LeadSinTocar
        pregunta.GetProperty("titular").GetString().Should().Be("Manolo García");
        pregunta.GetProperty("detalle").GetString().Should().Contain("no consta");
        pregunta.GetProperty("clave").GetString().Should().StartWith("lead-sin-tocar:");

        // Las opciones vienen del servidor, ya escritas: el cliente no decide qué se puede contestar.
        pregunta.GetProperty("opciones").EnumerateArray()
            .Select(o => o.GetProperty("etiqueta").GetString())
            .Should().Equal("Hablé con él", "No contesta", "No le interesa", "Ahora no");
    }

    [Fact]
    public async Task Contestar_apunta_la_llamada_en_la_ficha_y_quita_la_pregunta()
    {
        var cliente = await EnEmpresaAsync("Ribera Repaso Contestar");
        var contacto = await ContactoAsync(cliente, "Rosa Miralles");
        var clave = (await PilaAsync(cliente)).GetProperty("preguntas").EnumerateArray().Single()
            .GetProperty("clave").GetString()!;

        var r = await ResponderAsync(cliente, clave, 4); // Contactado
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LeerAsync(r)).GetProperty("efecto").GetString().Should().Contain("ficha");

        // El efecto de verdad: la llamada está en su cronología, sin que nadie escribiera nada.
        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{contacto}", UriKind.Relative)));
        ficha.GetProperty("cronologia").EnumerateArray()
            .Should().Contain(a => a.GetProperty("cuerpo").GetString()!.Contains("contactado"));

        (await PilaAsync(cliente)).GetProperty("alDia").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task No_le_interesa_saca_al_contacto_de_la_rueda()
    {
        // Si siguiera siendo lead, volvería a salir en Hoy la semana que viene y el comercial dejaría
        // de creerse lo que contesta.
        var cliente = await EnEmpresaAsync("Ribera Repaso Descartar");
        var contacto = await ContactoAsync(cliente, "Nolo Interesa");
        var clave = (await PilaAsync(cliente)).GetProperty("preguntas").EnumerateArray().Single()
            .GetProperty("clave").GetString()!;

        await ResponderAsync(cliente, clave, 6); // NoLeInteresa

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{contacto}", UriKind.Relative)));
        ficha.GetProperty("contacto").GetProperty("estado").GetInt32().Should().Be(3); // Perdido
    }

    [Fact]
    public async Task Una_tarea_vencida_se_cierra_de_un_toque()
    {
        var cliente = await EnEmpresaAsync("Ribera Repaso Tarea");
        var contacto = await ContactoAsync(cliente, "Ana Soler");
        var tarea = (await LeerAsync(await cliente.PostAsJsonAsync("/tareas", new
        {
            titulo = "Enviar el presupuesto",
            contactoId = contacto,
            venceEl = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
        }))).GetProperty("id").GetGuid();

        await EnvejecerAsync($"UPDATE tareas.tarea SET vence_el = current_date - 4 WHERE id = '{tarea}'");

        var pila = await PilaAsync(cliente);
        var pregunta = pila.GetProperty("preguntas").EnumerateArray()
            .Single(p => p.GetProperty("tipo").GetInt32() == 1);
        pregunta.GetProperty("detalle").GetString().Should().Contain("4 días");

        await ResponderAsync(cliente, pregunta.GetProperty("clave").GetString()!, 1); // Hecha

        var tareas = await LeerAsync(await cliente.GetAsync(new Uri("/tareas", UriKind.Relative)));
        tareas.EnumerateArray().Should().NotContain(t => t.GetProperty("id").GetGuid() == tarea);
    }

    [Fact]
    public async Task Perder_una_oportunidad_desde_el_repaso_exige_el_motivo()
    {
        var cliente = await EnEmpresaAsync("Ribera Repaso Perder");
        var contacto = await ContactoAsync(cliente, "Vicent Pons");
        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Reforma completa",
            importe = 24000m,
            previstaCierre = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
        }))).GetProperty("id").GetGuid();

        await EnvejecerAsync($"UPDATE embudo.oportunidad SET prevista_cierre = current_date - 6 WHERE id = '{oportunidad}'");

        var pregunta = (await PilaAsync(cliente)).GetProperty("preguntas").EnumerateArray()
            .Single(p => p.GetProperty("tipo").GetInt32() == 3); // CierrePasado
        pregunta.GetProperty("titular").GetString().Should().Be("Reforma completa · 24.000 €");

        var clave = pregunta.GetProperty("clave").GetString()!;

        var sinMotivo = await ResponderAsync(cliente, clave, 9); // Perdida
        sinMotivo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(sinMotivo)).GetProperty("codigo").GetString().Should().Be("repaso.falta_motivo");

        (await ResponderAsync(cliente, clave, 9, motivo: 1)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Y el motivo llega al informe, que es para lo que se pide.
        var motivos = await LeerAsync(await cliente.GetAsync(new Uri("/informes/motivos-perdida", UriKind.Relative)));
        motivos.GetProperty("motivos").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Sigue_viva_no_falsea_el_embudo_pero_calla_la_pregunta()
    {
        var cliente = await EnEmpresaAsync("Ribera Repaso Estancada");
        var contacto = await ContactoAsync(cliente, "Pau Gil");
        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Aire acondicionado",
            importe = 3400m,
        }))).GetProperty("id").GetGuid();

        await EnvejecerAsync($"UPDATE embudo.oportunidad SET entro_en_etapa_en = now() - interval '20 days' WHERE id = '{oportunidad}'");

        var pregunta = (await PilaAsync(cliente)).GetProperty("preguntas").EnumerateArray()
            .Single(p => p.GetProperty("tipo").GetInt32() == 4); // OportunidadEstancada

        await ResponderAsync(cliente, pregunta.GetProperty("clave").GetString()!, 7); // SigueViva

        // La oportunidad sigue exactamente donde estaba: no se ha tocado la fecha de entrada en etapa
        // para maquillar el estancamiento.
        var tablero = await LeerAsync(await cliente.GetAsync(new Uri("/embudo/tablero", UriKind.Relative)));
        tablero.GetProperty("columnas").EnumerateArray()
            .SelectMany(c => c.GetProperty("oportunidades").EnumerateArray())
            .Should().Contain(o => o.GetProperty("id").GetGuid() == oportunidad);

        // Pero la pregunta no vuelve hoy.
        (await PilaAsync(cliente)).GetProperty("preguntas").EnumerateArray()
            .Should().NotContain(p => p.GetProperty("tipo").GetInt32() == 4);
    }

    [Fact]
    public async Task Cada_comercial_repasa_lo_suyo()
    {
        // Un repaso que pregunta por los leads de un compañero es un repaso que no se puede vaciar.
        var mio = await EnEmpresaAsync("Ribera Repaso Mío");
        await ContactoAsync(mio, "Mi Lead");

        var otro = await EnEmpresaAsync("Ribera Repaso Otro");
        await ContactoAsync(otro, "Su Lead");

        (await PilaAsync(mio)).GetProperty("preguntas").EnumerateArray()
            .Should().OnlyContain(p => p.GetProperty("titular").GetString() == "Mi Lead");
    }

    [Fact]
    public async Task El_resumen_cuenta_la_semana_de_quien_pregunta()
    {
        var cliente = await EnEmpresaAsync("Ribera Repaso Resumen");
        var contacto = await ContactoAsync(cliente, "Resumida Ros");
        var clave = (await PilaAsync(cliente)).GetProperty("preguntas").EnumerateArray().Single()
            .GetProperty("clave").GetString()!;
        await ResponderAsync(cliente, clave, 4); // Contactado

        var resumen = await LeerAsync(await cliente.GetAsync(new Uri("/repaso/resumen", UriKind.Relative)));

        resumen.GetProperty("llamadas").GetInt32().Should().Be(1);
        resumen.GetProperty("contactosNuevos").GetInt32().Should().Be(1);
        resumen.GetProperty("preguntasResueltas").GetInt32().Should().Be(1);
        resumen.GetProperty("titular").GetString().Should().Contain("1 llamada");
        _ = contacto;
    }

    /// <summary>
    /// **El test que mide la promesa del módulo.**
    ///
    /// Siembra la semana de un comercial real —tareas que se le pasaron, leads que no llamó,
    /// oportunidades con la fecha vencida y otras paradas— y comprueba que la pila se vacía **contando
    /// las interacciones**: una por tarjeta, dos solo cuando se pierde una venta, y **ni un carácter de
    /// texto libre**.
    ///
    /// Si alguien añade mañana un campo obligatorio a cualquier respuesta, este test se pone rojo antes
    /// de que nadie tenga que descubrir en una demo que el repaso ya no dura minutos.
    /// </summary>
    [Fact]
    public async Task Una_semana_entera_se_cierra_en_un_toque_por_tarjeta()
    {
        var cliente = await EnEmpresaAsync("Ribera Semana Completa");

        // Cuatro leads que entraron y nadie llamó.
        for (var i = 1; i <= 4; i++)
        {
            await ContactoAsync(cliente, $"Lead {i}");
        }

        // Tres tareas que se pasaron de fecha.
        var conTarea = await ContactoAsync(cliente, "Con Tareas");
        for (var i = 1; i <= 3; i++)
        {
            await cliente.PostAsJsonAsync("/tareas", new
            {
                titulo = $"Pendiente {i}",
                contactoId = conTarea,
                venceEl = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            });
        }

        // Dos oportunidades con la fecha de cierre pasada y dos paradas en su etapa.
        var deOportunidades = await ContactoAsync(cliente, "Con Oportunidades");
        for (var i = 1; i <= 4; i++)
        {
            await cliente.PostAsJsonAsync("/oportunidades", new
            {
                contactoId = deOportunidades,
                titulo = $"Obra {i}",
                importe = 1000m * i,
                previstaCierre = i <= 2 ? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20) : (DateOnly?)null,
            });
        }

        // Siempre acotado a esta empresa: `ExecuteSqlRaw` corre como superusuario y salta el filtro
        // global de EF, así que un UPDATE sin WHERE aquí tocaría las filas de las otras pruebas que
        // comparten esta base.
        var empresa = (await LeerAsync(await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative))))
            .GetProperty("id").GetGuid();

        await EnvejecerAsync($"UPDATE tareas.tarea SET vence_el = current_date - 3 WHERE empresa_id = '{empresa}'");
        await EnvejecerAsync($"UPDATE embudo.oportunidad SET prevista_cierre = current_date - 5 WHERE empresa_id = '{empresa}' AND prevista_cierre IS NOT NULL");
        await EnvejecerAsync($"UPDATE embudo.oportunidad SET entro_en_etapa_en = now() - interval '25 days' WHERE empresa_id = '{empresa}' AND prevista_cierre IS NULL");

        var pila = await PilaAsync(cliente);
        var total = pila.GetProperty("total").GetInt32();

        // 4 leads + 3 tareas + 2 cierres pasados + 2 estancadas.
        //
        // Y **no** trece: «Con Tareas» y «Con Oportunidades» también son leads sin actividad saliente,
        // pero no se preguntan porque ya hay una tarea o una oportunidad suya en la pila. Escribiendo
        // este test salieron esas dos preguntas de más, que es exactamente la redundancia que hace que
        // un comercial cierre la pestaña.
        total.Should().Be(11);
        pila.GetProperty("segundosEstimados").GetInt32().Should().BeLessThan(60);

        var toques = 0;
        var respuestasPrincipales = new Dictionary<int, int>
        {
            [1] = 1,  // TareaVencida        → Hecha
            [2] = 4,  // LeadSinTocar        → Contactado
            [3] = 10, // CierrePasado        → OtraFecha
            [4] = 7,  // OportunidadEstancada → SigueViva
            [5] = 11, // SilencioCaliente    → LlamarHoy
            [6] = 11, // ClienteSinSiguientePaso → LlamarHoy
        };

        // Se vacía de verdad: se pide la pila, se contesta lo que trae, y se vuelve a pedir. Si algo no
        // se aparcara, este bucle no terminaría y el test moriría por tiempo.
        for (var ronda = 0; ronda < 5; ronda++)
        {
            var actual = await PilaAsync(cliente);
            if (actual.GetProperty("alDia").GetBoolean())
            {
                break;
            }

            foreach (var pregunta in actual.GetProperty("preguntas").EnumerateArray())
            {
                var tipo = pregunta.GetProperty("tipo").GetInt32();
                var respuesta = await ResponderAsync(cliente, pregunta.GetProperty("clave").GetString()!, respuestasPrincipales[tipo]);
                respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
                toques++;
            }
        }

        (await PilaAsync(cliente)).GetProperty("alDia").GetBoolean()
            .Should().BeTrue("la pila tiene que poder vaciarse; si no, nadie la abre dos viernes seguidos");

        // La promesa, medida: un toque por tarjeta y ninguno de más. Contestar «le llamo hoy» a un aviso
        // crea una tarea para hoy, que no está vencida y por tanto no vuelve a preguntar.
        toques.Should().Be(total);
    }
}
