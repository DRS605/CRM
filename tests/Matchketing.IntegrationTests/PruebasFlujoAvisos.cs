using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoAvisos(ApiDePrueba api)
{
    private const string Publica = "BM6oFunqnW-q5Rz-laNO3Mao2nF9eQ7cLPaW6ltwuhLqSdgz0awOs05RnQPmw-Koucpiqg71PjrZVmLkxjujuuU";
    private const string Secreto = "v96B8cq6_hyHop4iU0iZKg";

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

    private static string Endpoint() => $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";

    [Fact]
    public async Task La_clave_publica_vapid_tiene_el_formato_que_espera_el_navegador()
    {
        var cliente = await EnEmpresaAsync("Ribera Avisos Clave");

        var clave = (await LeerAsync(await cliente.GetAsync(new Uri("/avisos/clave", UriKind.Relative))))
            .GetProperty("clave").GetString()!;

        // 65 bytes en base64url son 87 caracteres. Es lo que `applicationServerKey` acepta.
        clave.Should().HaveLength(87).And.MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public async Task Suscribirse_y_desuscribirse_de_extremo_a_extremo()
    {
        var cliente = await EnEmpresaAsync("Ribera Avisos Alta");
        var endpoint = Endpoint();

        var alta = await cliente.PostAsJsonAsync("/avisos/suscripcion", new
        {
            endpoint,
            clavePublica = Publica,
            secreto = Secreto,
        });
        alta.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var aparatos = await LeerAsync(await cliente.GetAsync(new Uri("/avisos/aparatos", UriKind.Relative)));
        aparatos.GetArrayLength().Should().Be(1);

        // El endpoint completo **no** se devuelve: es la credencial con la que se le manda un aviso a
        // ese aparato, y en la pantalla solo hace falta saber de qué servicio es.
        var aparato = aparatos.EnumerateArray().Single();
        aparato.GetProperty("servicio").GetString().Should().Be("fcm.googleapis.com");
        aparato.TryGetProperty("endpoint", out _).Should().BeFalse();

        var baja = await cliente.DeleteAsync(new Uri($"/avisos/suscripcion?endpoint={Uri.EscapeDataString(endpoint)}", UriKind.Relative));
        baja.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LeerAsync(await cliente.GetAsync(new Uri("/avisos/aparatos", UriKind.Relative))))
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Reenviar_la_misma_suscripcion_no_duplica_el_aparato()
    {
        var cliente = await EnEmpresaAsync("Ribera Avisos Repetida");
        var endpoint = Endpoint();
        var cuerpo = new { endpoint, clavePublica = Publica, secreto = Secreto };

        await cliente.PostAsJsonAsync("/avisos/suscripcion", cuerpo);
        await cliente.PostAsJsonAsync("/avisos/suscripcion", cuerpo);

        (await LeerAsync(await cliente.GetAsync(new Uri("/avisos/aparatos", UriKind.Relative))))
            .GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Un_endpoint_que_no_es_https_se_rechaza_con_su_codigo()
    {
        var cliente = await EnEmpresaAsync("Ribera Avisos Malo");

        var r = await cliente.PostAsJsonAsync("/avisos/suscripcion", new
        {
            endpoint = "http://fcm.googleapis.com/fcm/send/x",
            clavePublica = Publica,
            secreto = Secreto,
        });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("suscripcion.endpoint_invalido");
    }

    [Fact]
    public async Task Apagar_un_aparato_que_no_estaba_no_es_un_error()
    {
        // Quien dice «no quiero avisos» no puede recibir un error por respuesta.
        var cliente = await EnEmpresaAsync("Ribera Avisos Fantasma");

        (await cliente.DeleteAsync(new Uri($"/avisos/suscripcion?endpoint={Uri.EscapeDataString(Endpoint())}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Cada_empresa_ve_solo_sus_aparatos()
    {
        var una = await EnEmpresaAsync("Ribera Avisos Uno");
        var otra = await EnEmpresaAsync("Ribera Avisos Dos");

        await una.PostAsJsonAsync("/avisos/suscripcion", new { endpoint = Endpoint(), clavePublica = Publica, secreto = Secreto });

        (await LeerAsync(await otra.GetAsync(new Uri("/avisos/aparatos", UriKind.Relative))))
            .GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Sin_sesion_no_se_puede_ni_pedir_la_clave()
    {
        // La clave pública lo es de verdad, pero solo la necesita quien va a suscribirse.
        (await api.CreateClient().GetAsync(new Uri("/avisos/clave", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
