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
public sealed class PruebasFlujoCumplimiento(ApiDePrueba api)
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

    private static async Task<Guid> ContactoAsync(HttpClient cliente, string nombre = "Lucía Ferrer")
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

    // ---- Panel de privacidad y G1 ---------------------------------------------------------

    [Fact]
    public async Task La_ficha_de_privacidad_trae_el_estado_y_el_enlace_de_baja()
    {
        var cliente = await EnEmpresaAsync("Ribera Privacidad");
        var contacto = await ContactoAsync(cliente);

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative)));

        ficha.GetProperty("deBaja").GetBoolean().Should().BeFalse();
        ficha.GetProperty("puedeEnviarComercial").GetBoolean().Should().BeFalse();
        ficha.GetProperty("enlaceBaja").GetString().Should().Contain("/b/");
        ficha.GetProperty("consentimientos").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Con_permiso_comercial_ya_se_puede_enviar()
    {
        var cliente = await EnEmpresaAsync("Ribera Permiso");
        var contacto = await ContactoAsync(cliente);

        var alta = await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 2, // Comercial
            @base = 1,     // Consentimiento
            canal = "alta manual",
            textoAceptado = "Acepto recibir ofertas comerciales.",
        });
        alta.StatusCode.Should().Be(HttpStatusCode.Created);

        var puede = await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}/puede-enviar?finalidad=Comercial", UriKind.Relative)));
        puede.GetProperty("puede").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Sin_base_legal_el_endpoint_dice_no_y_dice_por_que()
    {
        // Responde 200 con «puede: false», no un error: quien pregunta antes de enviar está haciendo
        // lo correcto y su petición no ha fallado.
        var cliente = await EnEmpresaAsync("Ribera Sin Base");
        var contacto = await ContactoAsync(cliente);

        var r = await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}/puede-enviar?finalidad=Comercial", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await LeerAsync(r);
        cuerpo.GetProperty("puede").GetBoolean().Should().BeFalse();
        cuerpo.GetProperty("codigo").GetString().Should().Be("cumplimiento.sin_base_legal");
    }

    [Fact]
    public async Task Un_lead_del_formulario_solo_consiente_que_le_contesten()
    {
        // Fin a fin: lo que se guarda al entrar por el formulario permite responderle y **no**
        // permite venderle. Es la razón de que el consentimiento tenga finalidad y no sea un booleano.
        var cliente = await EnEmpresaAsync("Ribera Lead Consentimiento");
        var formulario = await cliente.PostAsJsonAsync("/formularios", new
        {
            nombre = "Presupuesto",
            textoConsentimiento = "Acepto que me contactéis para responder a mi solicitud.",
            pideTelefono = true,
            pideEmpresa = false,
            pideMensaje = false,
            paginaGracias = (string?)null,
            origen = (string?)null,
        });
        var clave = (await LeerAsync(formulario)).GetProperty("clave").GetString()!;

        var anonimo = api.CreateClient();
        var envio = await anonimo.PostAsJsonAsync($"/f/{clave}", new
        {
            nombre = "Pau Gil",
            email = $"p{Guid.NewGuid():N}@correo.es",
            telefono = "600445566",
            consiente = true,
        });
        envio.StatusCode.Should().Be(HttpStatusCode.OK);

        var lista = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        var contacto = lista.EnumerateArray().Single(c => c.GetProperty("nombre").GetString() == "Pau Gil").GetProperty("id").GetGuid();

        var atender = await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}/puede-enviar?finalidad=AtenderSolicitud", UriKind.Relative)));
        var comercial = await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}/puede-enviar?finalidad=Comercial", UriKind.Relative)));

        atender.GetProperty("puede").GetBoolean().Should().BeTrue();
        comercial.GetProperty("puede").GetBoolean().Should().BeFalse();
    }

    // ---- Baja de un clic ------------------------------------------------------------------

    [Fact]
    public async Task La_pagina_de_baja_pregunta_y_no_da_de_baja()
    {
        // **La prueba que más importa de este módulo.** Los antivirus de correo y las vistas previas
        // abren los enlaces sin que nadie los pulse. Si el GET diera de baja, daría de baja a gente
        // que no lo pidió, y la baja no se puede deshacer desde aquí.
        var cliente = await EnEmpresaAsync("Ribera Baja GET");
        var contacto = await ContactoAsync(cliente);
        var enlace = (await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative))))
            .GetProperty("enlaceBaja").GetString()!;
        var ruta = enlace[enlace.IndexOf("/b/", StringComparison.Ordinal)..];

        var pagina = await api.CreateClient().GetAsync(new Uri(ruta, UriKind.Relative));

        pagina.StatusCode.Should().Be(HttpStatusCode.OK);
        pagina.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await pagina.Content.ReadAsStringAsync()).Should().Contain("darme de baja");

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative)));
        ficha.GetProperty("deBaja").GetBoolean().Should().BeFalse("un GET no puede dar de baja a nadie");
    }

    [Fact]
    public async Task Confirmar_la_baja_funciona_sin_estar_identificado()
    {
        var cliente = await EnEmpresaAsync("Ribera Baja POST");
        var contacto = await ContactoAsync(cliente);
        await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 2,
            @base = 1,
            canal = "alta manual",
            textoAceptado = "Acepto ofertas.",
        });

        var enlace = (await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative))))
            .GetProperty("enlaceBaja").GetString()!;
        var ruta = enlace[enlace.IndexOf("/b/", StringComparison.Ordinal)..];

        // Sin token, desde otro cliente: es alguien en su gestor de correo, no en la aplicación.
        var baja = await api.CreateClient().PostAsync(new Uri(ruta, UriKind.Relative), null);
        baja.StatusCode.Should().Be(HttpStatusCode.OK);

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative)));
        ficha.GetProperty("deBaja").GetBoolean().Should().BeTrue();
        ficha.GetProperty("puedeEnviarComercial").GetBoolean().Should().BeFalse();
        ficha.GetProperty("explicacion").GetString().Should().Contain("Pidió no recibir");
        ficha.GetProperty("consentimientos").EnumerateArray()
            .Should().OnlyContain(c => !c.GetProperty("vigente").GetBoolean());
    }

    [Fact]
    public async Task Pulsar_dos_veces_el_enlace_no_da_error()
    {
        var cliente = await EnEmpresaAsync("Ribera Baja Doble");
        var contacto = await ContactoAsync(cliente);
        var enlace = (await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative))))
            .GetProperty("enlaceBaja").GetString()!;
        var ruta = new Uri(enlace[enlace.IndexOf("/b/", StringComparison.Ordinal)..], UriKind.Relative);

        var anonimo = api.CreateClient();
        (await anonimo.PostAsync(ruta, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var segunda = await anonimo.PostAsync(ruta, null);

        segunda.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LeerAsync(segunda)).GetProperty("yaEstaba").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Un_enlace_manipulado_no_da_de_baja_a_nadie()
    {
        var cliente = await EnEmpresaAsync("Ribera Baja Falsa");
        var contacto = await ContactoAsync(cliente);

        var r = await api.CreateClient().PostAsync(new Uri("/b/AAAAAAAAAAAAAAAAAAAAAA.BBBBBBBBBBBBBBBBBBBBBB", UriKind.Relative), null);

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative))))
            .GetProperty("deBaja").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task La_pagina_de_baja_se_sirve_a_cualquier_origen()
    {
        // Va en un correo, así que el navegador la abre desde cualquier parte. Sin CORS el `fetch` del
        // botón moriría en producción y la baja de un clic sería una baja de ninguno.
        var cliente = await EnEmpresaAsync("Ribera Baja CORS");
        var contacto = await ContactoAsync(cliente);
        var enlace = (await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative))))
            .GetProperty("enlaceBaja").GetString()!;

        var anonimo = api.CreateClient();
        var peticion = new HttpRequestMessage(HttpMethod.Post, enlace[enlace.IndexOf("/b/", StringComparison.Ordinal)..]);
        peticion.Headers.Add("Origin", "https://correo.ejemplo.es");

        var r = await anonimo.SendAsync(peticion);

        r.Headers.Should().ContainKey("Access-Control-Allow-Origin");
    }

    // ---- Derechos de acceso y supresión --------------------------------------------------

    [Fact]
    public async Task La_exportacion_trae_todo_lo_que_hay_de_la_persona()
    {
        var cliente = await EnEmpresaAsync("Ribera Exportar");
        var contacto = await ContactoAsync(cliente, "Nuria Sales");
        await cliente.PostAsJsonAsync($"/contactos/{contacto}/notas", new { cuerpo = "Pidió presupuesto de tarima." });

        var datos = await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}/exportar", UriKind.Relative)));

        datos.GetProperty("contacto").GetProperty("nombre").GetString().Should().Be("Nuria Sales");
        datos.GetProperty("cronologia").GetArrayLength().Should().BeGreaterThan(0);
        datos.TryGetProperty("consentimientos", out _).Should().BeTrue();
        datos.TryGetProperty("puntuacion", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Borrar_un_contacto_lo_borra_de_todas_las_tablas()
    {
        // Supresión de verdad: la fila desaparece. Un `activo = false` no es una supresión, es un
        // archivo con otro nombre.
        var cliente = await EnEmpresaAsync("Ribera Suprimir");
        var contacto = await ContactoAsync(cliente, "Borrable Pérez");
        await cliente.PostAsJsonAsync($"/contactos/{contacto}/notas", new { cuerpo = "Una nota." });
        await cliente.PostAsJsonAsync("/oportunidades", new { contactoId = contacto, titulo = "Tarima", importe = 3200m });

        var r = await cliente.DeleteAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        var recuento = await LeerAsync(r);
        recuento.GetProperty("contactos").GetInt32().Should().Be(1);
        recuento.GetProperty("oportunidades").GetInt32().Should().Be(1);
        recuento.GetProperty("actividades").GetInt32().Should().BeGreaterThan(0);

        (await cliente.GetAsync(new Uri($"/contactos/{contacto}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Busca un identificador **en toda la base de datos**: en cada columna `uuid` y en cada columna de
    /// texto de cada tabla, sin lista escrita a mano.
    ///
    /// Sin lista a mano es todo el punto. La supresión del artículo 17 se quedó incompleta durante varios
    /// módulos porque cada módulo nuevo añadía datos de personas y nadie volvía a la lista. Una prueba
    /// que enumere las tablas desde `information_schema` no se queda desfasada: el día que alguien añada
    /// una tabla con un `contacto_id` y se olvide de la supresión, esto falla solo.
    ///
    /// Se deja fuera `auditoria.registro` a propósito y está documentado: es append-only por diseño, no
    /// guarda datos personales en el detalle y su identificador de entidad es lo único que permite
    /// demostrar después que la supresión se hizo. Borrar la prueba de que se borró sería absurdo.
    /// </summary>
    private async Task<IReadOnlyList<string>> DondeApareceAsync(Guid id)
    {
        using var alcance = api.Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();

        var columnas = await bd.Database
            .SqlQuery<ColumnaDeLaBase>($"""
                SELECT table_schema AS "Esquema", table_name AS "Tabla",
                       column_name AS "Columna", data_type AS "Tipo"
                  FROM information_schema.columns
                 WHERE table_schema NOT IN ('pg_catalog', 'information_schema', 'publico')
                   AND table_schema <> 'auditoria'
                   AND (data_type = 'uuid' OR data_type IN ('text', 'character varying'))
                """)
            .ToListAsync();

        columnas.Should().NotBeEmpty("si no se encuentran columnas, la prueba no comprueba nada");

        var texto = id.ToString();
        var apariciones = new List<string>();

        foreach (var c in columnas)
        {
            // La comparación se hace en SQL y con el tipo de cada columna: un `uuid` se compara con un
            // `uuid`, y un texto se busca dentro por si el identificador viaja en un JSON —los cuerpos de
            // webhook lo hacen— o en una clave compuesta, como las preguntas aparcadas del repaso.
            var condicion = c.Tipo == "uuid"
                ? $@"""{c.Columna}"" = '{texto}'::uuid"
                : $@"""{c.Columna}"" LIKE '%{texto}%'";

            // EF1002 avisa de SQL interpolado, y hace bien. Aquí se silencia con motivo: el nombre de un
            // esquema, de una tabla o de una columna **no se puede parametrizar** en SQL, y estos tres
            // salen de `information_schema` de la propia base, no de nada que escriba un usuario. El
            // único valor que viaja es un `Guid`, que no puede contener una comilla.
#pragma warning disable EF1002
            var cuantas = await bd.Database
                .SqlQueryRaw<long>($@"SELECT count(*) AS ""Value"" FROM ""{c.Esquema}"".""{c.Tabla}"" WHERE {condicion}")
                .SingleAsync();
#pragma warning restore EF1002

            if (cuantas > 0)
            {
                apariciones.Add($"{c.Esquema}.{c.Tabla}.{c.Columna} ({cuantas})");
            }
        }

        return apariciones;
    }

    private sealed record ColumnaDeLaBase(string Esquema, string Tabla, string Columna, string Tipo);

    [Fact]
    public async Task Borrar_un_contacto_no_deja_ni_un_rastro_suyo_en_ninguna_tabla()
    {
        // **La prueba que faltaba, y el fallo que encontró.** La supresión borraba contacto, actividades,
        // oportunidades, tareas, señales, puntuaciones, envíos de formulario y consentimientos… y dejaba
        // en la base los **correos** que se le habían mandado, con su dirección, su asunto y su texto
        // completo. También su fila en cada campaña, las ejecuciones de reglas sobre él, los cuerpos de
        // webhook con su identificador y sus preguntas aparcadas.
        //
        // No fue un descuido de una tabla: fue que cada módulo nuevo añadía datos de personas y nadie
        // volvía a la lista de la supresión. Así que esta prueba no lleva lista: recorre las columnas de
        // la base tal como están hoy.
        var cliente = await EnEmpresaAsync("Ribera Sin Rastro");
        var contacto = await ContactoAsync(cliente, "Borrable Pérez");

        // Se le deja rastro por todos los sitios que lo pueden guardar.
        await cliente.PostAsJsonAsync($"/contactos/{contacto}/notas", new { cuerpo = "Una nota." });
        await cliente.PostAsJsonAsync("/oportunidades", new { contactoId = contacto, titulo = "Tarima", importe = 3200m });
        await cliente.PostAsJsonAsync("/tareas", new { titulo = "Llamar", contactoId = contacto });

        // Un webhook, para que su identificador acabe dentro de un cuerpo JSON.
        await cliente.PostAsJsonAsync("/webhooks", new
        {
            url = "https://ejemplo.invalid/gancho",
            eventos = new[] { "lead.creado", "oportunidad.ganada" },
        });

        // Una regla, para que quede una ejecución con él como sujeto.
        await cliente.PostAsJsonAsync("/reglas", new
        {
            nombre = "Llamar a los nuevos",
            cuando = "lead.creado",
            condiciones = Array.Empty<object>(),
            acciones = new[] { new { tipo = 1, texto = "Llamar al lead nuevo", referencia = (Guid?)null, numero = 0 } },
        });

        // Un correo, que es lo más personal de todo: su dirección y el texto que se le escribió.
        await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 1, @base = 2, canal = "alta manual",
        });
        var plantilla = (await LeerAsync(await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = $"Seguimiento {Guid.NewGuid():N}",
            asunto = "Sobre lo que hablamos",
            cuerpo = "Hola {{nombre}}, te llamo mañana.",
            paraQue = 1,
        }))).GetProperty("id").GetGuid();
        (await cliente.PostAsJsonAsync("/correo/enviar", new { contactoId = contacto, plantillaId = plantilla }))
            .IsSuccessStatusCode.Should().BeTrue();

        // Una pregunta del repaso aparcada, cuya clave lleva su identificador dentro.
        await cliente.PostAsJsonAsync("/repaso/responder", new
        {
            clave = $"silencio-caliente:{contacto}",
            respuesta = 12, // Déjalo estar
        });

        // Antes de borrar, tiene que aparecer en varios sitios: si no, la prueba pasaría por no haber
        // creado nada y no por haber borrado bien.
        var antes = await DondeApareceAsync(contacto);
        antes.Should().HaveCountGreaterThan(4, "hay que dejar rastro antes de comprobar que se limpia");
        antes.Should().Contain(x => x.StartsWith("correo.mensaje", StringComparison.Ordinal),
            "el correo enviado es el rastro que más importa");

        var r = await cliente.DeleteAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative));
        r.StatusCode.Should().Be(HttpStatusCode.OK);

        // Esta es la afirmación que importa, y va primero para que sea la que falle: el recuento puede
        // cuadrar y quedar datos, pero si no queda nada el recuento es un detalle.
        var despues = await DondeApareceAsync(contacto);
        despues.Should().BeEmpty(
            "borrar es borrar: no puede quedar el identificador de esta persona en ninguna columna de "
            + "ninguna tabla. Si esto falla nombrando una tabla nueva, la supresión se ha quedado atrás.");

        // Y se cuenta lo que se borró, porque es lo que se le contesta a quien ejerció el derecho.
        var recuento = await LeerAsync(r);
        recuento.GetProperty("correos").GetInt32().Should().Be(1, "el correo se cuenta, no se borra a escondidas");
    }

    [Fact]
    public async Task La_exportacion_incluye_los_correos_que_se_le_mandaron()
    {
        // También son datos suyos, y probablemente los que más le interese ver a quien ejerce el derecho
        // de acceso: no «se le escribió una vez», sino qué decía ese correo. Faltaban.
        var cliente = await EnEmpresaAsync("Ribera Acceso Correos");
        var contacto = await ContactoAsync(cliente, "Lectora Martí");

        await cliente.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos", new
        {
            finalidad = 1, @base = 2, canal = "alta manual",
        });
        var plantilla = (await LeerAsync(await cliente.PostAsJsonAsync("/plantillas", new
        {
            nombre = $"Seguimiento {Guid.NewGuid():N}",
            asunto = "Tu presupuesto",
            cuerpo = "Hola {{nombre}}, te adjunto el presupuesto.",
            paraQue = 1,
        }))).GetProperty("id").GetGuid();
        await cliente.PostAsJsonAsync("/correo/enviar", new { contactoId = contacto, plantillaId = plantilla });

        var datos = await LeerAsync(await cliente.GetAsync(
            new Uri($"/cumplimiento/contactos/{contacto}/exportar", UriKind.Relative)));

        var correos = datos.GetProperty("correos").EnumerateArray().ToList();
        correos.Should().ContainSingle();
        correos[0].GetProperty("asunto").GetString().Should().Be("Tu presupuesto");
        correos[0].GetProperty("cuerpo").GetString().Should().Contain("presupuesto",
            "el texto exacto que se le mandó es el dato que pide el derecho de acceso");
    }

    [Fact]
    public async Task La_copia_de_la_empresa_incluye_sus_contactos_y_sus_ajustes()
    {
        var cliente = await EnEmpresaAsync("Ribera Portabilidad");
        await ContactoAsync(cliente, "Vicent Pons");

        var datos = await LeerAsync(await cliente.GetAsync(new Uri("/cumplimiento/empresa/exportar", UriKind.Relative)));

        datos.GetProperty("empresa").GetProperty("nombre").GetString().Should().Be("Ribera Portabilidad");
        datos.GetProperty("empresa").GetProperty("mesesRetencionLeads").GetInt32().Should().Be(24);
        datos.GetProperty("contactos").EnumerateArray()
            .Should().Contain(c => c.GetProperty("nombre").GetString() == "Vicent Pons");
    }

    [Fact]
    public async Task Cerrar_la_cuenta_exige_escribir_el_nombre_de_la_empresa()
    {
        var cliente = await EnEmpresaAsync("Ribera Cierre");

        var mal = await cliente.PostAsJsonAsync("/cumplimiento/empresa/borrar", new { confirmacion = "ribera cierre" });

        mal.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(mal)).GetProperty("codigo").GetString().Should().Be("empresa.confirmacion_no_coincide");

        // Y con el nombre exacto, se va: los contactos dejan de existir y el token ya no vale.
        await ContactoAsync(cliente);
        var bien = await cliente.PostAsJsonAsync("/cumplimiento/empresa/borrar", new { confirmacion = "Ribera Cierre" });
        bien.StatusCode.Should().Be(HttpStatusCode.OK);

        var lista = await cliente.GetAsync(new Uri("/contactos", UriKind.Relative));
        (await LeerAsync(lista)).GetArrayLength().Should().Be(0);
    }

    // ---- Retención ------------------------------------------------------------------------

    [Fact]
    public async Task La_retencion_no_toca_lo_que_se_esta_trabajando()
    {
        var cliente = await EnEmpresaAsync("Ribera Retencion");
        var reciente = await ContactoAsync(cliente, "Reciente Martí");

        // Con el plazo mínimo (3 meses) y un contacto de hoy, no debería caer nada.
        (await cliente.PutAsJsonAsync("/empresas/activa/ajustes-retencion", new { mesesRetencionLeads = 3 }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var r = await LeerAsync(await cliente.PostAsync(new Uri("/cumplimiento/retencion", UriKind.Relative), null));

        r.GetProperty("leadsBorrados").GetInt32().Should().Be(0);
        (await cliente.GetAsync(new Uri($"/contactos/{reciente}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task La_retencion_se_lleva_los_leads_viejos_y_deja_a_los_clientes()
    {
        var cliente = await EnEmpresaAsync("Ribera Retencion Vieja");
        var viejo = await ContactoAsync(cliente, "Antiguo Beltrán");
        var clienteViejo = await ContactoAsync(cliente, "Cliente Fiel");
        await cliente.PutAsJsonAsync($"/contactos/{clienteViejo}/estado", new { estado = 2 });

        // Se envejecen a mano las dos filas: el reloj del sistema no se puede mover, y esperar dos
        // años a que la prueba pase no era una opción.
        using (var alcance = api.Services.CreateScope())
        {
            var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();
            await bd.Database.ExecuteSqlRawAsync(
                "UPDATE contactos.contacto SET actualizado_en = now() - interval '30 months' WHERE id IN ({0}, {1})".Replace("{0}", $"'{viejo}'").Replace("{1}", $"'{clienteViejo}'"));
        }

        var r = await LeerAsync(await cliente.PostAsync(new Uri("/cumplimiento/retencion", UriKind.Relative), null));

        r.GetProperty("leadsBorrados").GetInt32().Should().Be(1);
        (await cliente.GetAsync(new Uri($"/contactos/{viejo}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.GetAsync(new Uri($"/contactos/{clienteViejo}", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_plazo_de_retencion_tiene_un_minimo_que_protege_los_leads_vivos()
    {
        var cliente = await EnEmpresaAsync("Ribera Retencion Absurda");

        var r = await cliente.PutAsJsonAsync("/empresas/activa/ajustes-retencion", new { mesesRetencionLeads = 1 });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("empresa.retencion_invalida");
    }
}
