using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
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
        cuerpo.GetProperty("permisos").GetArrayLength().Should().Be(11);
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
    public async Task El_perfil_lista_las_empresas_del_usuario()
    {
        var (cliente, _, _) = await RegistradoAsync();
        await cliente.PostAsJsonAsync("/empresas", new { nombre = "Ribera Uno" });
        await cliente.PostAsJsonAsync("/empresas", new { nombre = "Ribera Dos" });

        var yo = await LeerAsync(await cliente.GetAsync(new Uri("/auth/yo", UriKind.Relative)));

        yo.GetProperty("empresas").GetArrayLength().Should().Be(2);
    }
}
