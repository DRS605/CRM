using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Identidad.Dominio;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoIdentidad(ApiDePrueba api)
{
    private static int contador;

    private static string CorreoNuevo() => $"marta{Interlocked.Increment(ref contador)}-{Guid.NewGuid():N}@ribera.es";

    private HttpClient Cliente() => api.CreateClient();

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<(HttpClient Cliente, string Token, string Email)> RegistradoAsync()
    {
        var cliente = Cliente();
        var email = CorreoNuevo();
        var r = await cliente.PostAsJsonAsync("/auth/registro", new { email, contrasena = "Levante2026", nombre = "Marta Ruiz" });
        r.StatusCode.Should().Be(HttpStatusCode.Created);

        var token = (await LeerAsync(r)).GetProperty("token").GetString()!;
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (cliente, token, email);
    }

    [Fact]
    public async Task Registrarse_devuelve_la_sesion_ya_iniciada()
    {
        var (_, token, _) = await RegistradoAsync();

        token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task No_se_puede_registrar_dos_veces_el_mismo_correo()
    {
        var (_, _, email) = await RegistradoAsync();

        var r = await Cliente().PostAsJsonAsync("/auth/registro", new { email, contrasena = "Levante2026", nombre = "Otra" });

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("usuario.email_repetido");
    }

    [Fact]
    public async Task El_correo_se_normaliza_asi_que_se_puede_entrar_en_mayusculas()
    {
        var (_, _, email) = await RegistradoAsync();

        var r = await Cliente().PostAsJsonAsync("/auth/login", new { email = email.ToUpperInvariant(), contrasena = "Levante2026" });

        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Contrasena_mala_y_correo_inexistente_dan_exactamente_el_mismo_error()
    {
        var (_, _, email) = await RegistradoAsync();

        var mala = await Cliente().PostAsJsonAsync("/auth/login", new { email, contrasena = "Equivocada9" });
        var inexistente = await Cliente().PostAsJsonAsync("/auth/login", new { email = CorreoNuevo(), contrasena = "Equivocada9" });

        mala.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        inexistente.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var m = await mala.Content.ReadAsStringAsync();
        var i = await inexistente.Content.ReadAsStringAsync();
        m.Should().Be(i, "decir cuál de los dos falla regala información a quien prueba correos");
    }

    [Fact]
    public async Task Quien_crea_la_empresa_es_su_propietario_y_recibe_todos_los_permisos()
    {
        var (cliente, _, _) = await RegistradoAsync();

        var r = await cliente.PostAsJsonAsync("/empresas", new { nombre = "Instalaciones Ribera, S.L.", provincia = "Valencia" });

        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var cuerpo = await LeerAsync(r);
        cuerpo.GetProperty("nombreEmpresa").GetString().Should().Be("Instalaciones Ribera, S.L.");
        // Todos los que hay, sean los que sean hoy: el propietario es por definición quien los tiene
        // todos, y esa es la afirmación. Un número escrito a mano habría dicho lo mismo hasta el día que
        // se añade un permiso nuevo, y entonces habría fallado sin que el propietario hubiera cambiado.
        cuerpo.GetProperty("permisos").GetArrayLength().Should().Be(Permisos.Todos.Count);
    }

    [Fact]
    public async Task Un_usuario_no_puede_entrar_en_la_empresa_de_otro()
    {
        var (duena, _, _) = await RegistradoAsync();
        var creada = await duena.PostAsJsonAsync("/empresas", new { nombre = "Ribera" });
        var empresaId = (await LeerAsync(creada)).GetProperty("empresaId").GetString();

        var (intrusa, _, _) = await RegistradoAsync();
        var r = await intrusa.PostAsync($"/empresas/{empresaId}/seleccionar", null);

        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("empresa.sin_acceso");
    }

    [Fact]
    public async Task Sin_token_no_se_ve_nada()
    {
        var r = await Cliente().GetAsync(new Uri("/empresas/activa", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task El_token_sin_empresa_no_da_acceso_a_datos_de_empresa()
    {
        var (cliente, _, _) = await RegistradoAsync();

        var r = await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Los_ajustes_del_match_se_guardan_y_se_releen()
    {
        var (cliente, _, _) = await RegistradoAsync();
        var creada = await cliente.PostAsJsonAsync("/empresas", new { nombre = "Ribera" });
        var token = (await LeerAsync(creada)).GetProperty("token").GetString();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var guardar = await cliente.PutAsJsonAsync("/empresas/activa/ajustes-match", new { pesoEncaje = 0.65m, horasRebote = 6 });
        guardar.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var leido = await LeerAsync(await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative)));
        leido.GetProperty("pesoEncaje").GetDecimal().Should().Be(0.65m);
        leido.GetProperty("horasRebote").GetInt32().Should().Be(6);
    }

    [Fact]
    public async Task Un_peso_fuera_de_rango_se_rechaza()
    {
        var (cliente, _, _) = await RegistradoAsync();
        var creada = await cliente.PostAsJsonAsync("/empresas", new { nombre = "Ribera" });
        var token = (await LeerAsync(creada)).GetProperty("token").GetString();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var r = await cliente.PutAsJsonAsync("/empresas/activa/ajustes-match", new { pesoEncaje = 1.4m, horasRebote = 6 });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("empresa.peso_invalido");
    }

    [Fact]
    public async Task Los_datos_de_la_empresa_se_corrigen_y_se_releen()
    {
        // No se podía. `Empresa.Actualizar` estaba en el dominio desde el módulo 1 sin un solo
        // llamante: el NIF se **enseñaba** en Ajustes y no había manera de rellenarlo, y una errata en
        // el nombre —el que sale en los correos y en la copia de los datos— era para siempre.
        var (cliente, _, _) = await ConEmpresaAsync("Bar Nou, S.L.");

        var guardar = await cliente.PutAsJsonAsync(
            "/empresas/activa", new { nombre = "Bar Nou de Vinaròs, S.L.", nif = "B98765432", provincia = "Castellón" });
        guardar.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var leido = await LeerAsync(await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative)));
        leido.GetProperty("nombre").GetString().Should().Be("Bar Nou de Vinaròs, S.L.");
        leido.GetProperty("nif").GetString().Should().Be("B98765432");
        leido.GetProperty("provincia").GetString().Should().Be("Castellón");
    }

    [Fact]
    public async Task Corregir_los_datos_no_puede_dejar_la_empresa_sin_nombre()
    {
        var (cliente, _, _) = await ConEmpresaAsync("Bar Nou, S.L.");

        var r = await cliente.PutAsJsonAsync("/empresas/activa", new { nombre = "   ", nif = "B98765432" });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("empresa.nombre_vacio");

        var leido = await LeerAsync(await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative)));
        leido.GetProperty("nombre").GetString().Should().Be("Bar Nou, S.L.", "un cambio inválido no toca nada");
    }

    [Fact]
    public async Task El_registro_de_auditoria_no_guarda_el_NIF()
    {
        // El NIF de un autónomo es su DNI. El registro de auditoría apunta **qué** se cambió, nunca el
        // valor: es la regla de `docs/modulos/auditoria.md` y aquí es fácil de romper sin darse cuenta.
        var (cliente, _, _) = await ConEmpresaAsync("Bar Nou, S.L.");

        await cliente.PutAsJsonAsync("/empresas/activa", new { nombre = "Bar Nou, S.L.", nif = "B98765432" });

        var registro = await (await cliente.GetAsync(new Uri("/auditoria", UriKind.Relative))).Content.ReadAsStringAsync();
        registro.Should().Contain("ajustes.cambiados");
        registro.Should().NotContain("B98765432", "el valor no entra en el registro, solo qué campos se tocaron");
    }

    [Fact]
    public async Task La_medicion_de_aperturas_se_puede_encender_y_apagar()
    {
        // La documentación decía «que sea una decisión explícita de la empresa». No lo era: el valor
        // nacía en `false` y **no había endpoint ni pantalla para cambiarlo**, así que el píxel, la
        // séptima pregunta del repaso y todo el seguimiento de aperturas eran código inalcanzable.
        var (cliente, _, _) = await ConEmpresaAsync("Ribera");

        var recien = await LeerAsync(await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative)));
        recien.GetProperty("sigueAperturas").GetBoolean().Should().BeFalse("nace apagado, y eso es la decisión por defecto");

        (await cliente.PutAsJsonAsync("/empresas/activa/ajustes-correo", new { sigueAperturas = true }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LeerAsync(await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative))))
            .GetProperty("sigueAperturas").GetBoolean().Should().BeTrue();

        (await cliente.PutAsJsonAsync("/empresas/activa/ajustes-correo", new { sigueAperturas = false }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LeerAsync(await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative))))
            .GetProperty("sigueAperturas").GetBoolean().Should().BeFalse("apagarlo tiene que ser igual de fácil que encenderlo");
    }

    [Fact]
    public async Task Encender_y_apagar_la_medicion_de_aperturas_queda_auditado()
    {
        // Es la prueba de cuándo se decidió medir el comportamiento de la gente y cuándo se dejó de
        // medir. Sin ese rastro no se puede contestar a un cliente que lo pregunte.
        var (cliente, _, _) = await ConEmpresaAsync("Ribera");

        await cliente.PutAsJsonAsync("/empresas/activa/ajustes-correo", new { sigueAperturas = true });

        var registro = await (await cliente.GetAsync(new Uri("/auditoria", UriKind.Relative))).Content.ReadAsStringAsync();
        registro.Should().Contain("ajustes.cambiados");
        registro.Should().Contain("SigueAperturas", "hace falta saber si se encendió o se apagó");
    }

    [Fact]
    public async Task Los_datos_de_una_empresa_no_se_tocan_desde_otra()
    {
        // El aislamiento, en el endpoint nuevo: la empresa que se corrige es la del token, y no hay
        // manera de nombrar otra en la petición.
        var (unoCliente, _, _) = await ConEmpresaAsync("Ribera Uno");
        var (dosCliente, _, _) = await ConEmpresaAsync("Ribera Dos");

        await unoCliente.PutAsJsonAsync("/empresas/activa", new { nombre = "Ribera Uno Corregida", nif = "B11111111" });

        var dos = await LeerAsync(await dosCliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative)));
        dos.GetProperty("nombre").GetString().Should().Be("Ribera Dos");
        dos.TryGetProperty("nif", out var nif).Should().BeTrue();
        (nif.ValueKind == JsonValueKind.Null).Should().BeTrue("el NIF de la otra empresa sigue vacío");
    }

    /// <summary>Un cliente registrado y con empresa activa, que es lo que piden casi todas las pruebas.</summary>
    private async Task<(HttpClient Cliente, string Token, string Email)> ConEmpresaAsync(string nombre)
    {
        var (cliente, _, email) = await RegistradoAsync();
        var creada = await cliente.PostAsJsonAsync("/empresas", new { nombre });
        creada.StatusCode.Should().Be(HttpStatusCode.Created);
        var token = (await LeerAsync(creada)).GetProperty("token").GetString()!;
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (cliente, token, email);
    }

    [Fact]
    public async Task El_perfil_lista_las_empresas_del_usuario()
    {
        var (cliente, _, _) = await RegistradoAsync();
        await cliente.PostAsJsonAsync("/empresas", new { nombre = "Ribera Uno" });
        await cliente.PostAsJsonAsync("/empresas", new { nombre = "Ribera Dos" });

        var yo = await LeerAsync(await cliente.GetAsync(new Uri("/auth/yo", UriKind.Relative)));

        yo.GetProperty("empresas").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task El_perfil_devuelve_el_correo_y_el_nombre_de_quien_pregunta()
    {
        var (cliente, _, email) = await RegistradoAsync();

        var yo = await LeerAsync(await cliente.GetAsync(new Uri("/auth/yo", UriKind.Relative)));

        yo.GetProperty("nombre").GetString().Should().Be("Marta Ruiz");

        // Esto devolvía `null` desde el primer módulo. El token se firma con la reclamación corta
        // `email`, pero `JwtBearer` trae `MapInboundClaims` activado y la reescribe al URI largo de
        // WS-Federation, así que buscarla por `"email"` no la encontraba nunca. No se veía en pantalla
        // porque la interfaz no lo usa, que es justo por lo que había durado tanto.
        yo.GetProperty("email").GetString().Should().Be(email);
    }
}
