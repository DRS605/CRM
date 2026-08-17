using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoSistema(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task La_sonda_de_salud_comprueba_la_base_de_datos()
    {
        var r = await api.CreateClient().GetAsync(new Uri("/salud", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var cuerpo = await LeerAsync(r);
        cuerpo.GetProperty("estado").GetString().Should().Be("vivo");
        cuerpo.GetProperty("base_datos").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Se_puede_cambiar_la_contrasena_dando_la_actual()
    {
        var cliente = api.CreateClient();
        var correo = $"c{Guid.NewGuid():N}@ribera.es";
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new { email = correo, contrasena = "Levante2026", nombre = "Cambia Clave" });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        var cambio = await cliente.PostAsJsonAsync("/auth/contrasena", new { actual = "Levante2026", nueva = "Albufera2027" });
        cambio.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var anonimo = api.CreateClient();
        (await anonimo.PostAsJsonAsync("/auth/login", new { email = correo, contrasena = "Albufera2027" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonimo.PostAsJsonAsync("/auth/login", new { email = correo, contrasena = "Levante2026" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sin_la_contrasena_actual_no_se_cambia()
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"c{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "No Cambia",
        });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        var r = await cliente.PostAsJsonAsync("/auth/contrasena", new { actual = "MeLoInvento1", nueva = "Albufera2027" });

        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("contrasena.actual_incorrecta");
    }

    [Fact]
    public async Task La_contrasena_nueva_tiene_que_pasar_los_mismos_requisitos()
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"c{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Clave Floja",
        });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        (await LeerAsync(await cliente.PostAsJsonAsync("/auth/contrasena", new { actual = "Levante2026", nueva = "corta" })))
            .GetProperty("codigo").GetString().Should().Be("contrasena.corta");
    }
}

/// <summary>
/// El límite de intentos se prueba **con su propia instancia de la aplicación**, no con la compartida.
/// El contador vive en memoria y se reparte por IP de origen; en el servidor de pruebas todas las
/// peticiones vienen de la misma («sin-ip»), así que agotarlo en la instancia compartida dejaría sin
/// intentos a cualquier otra prueba que iniciara sesión después. Instancia aparte, contador aparte.
///
/// No implementa <c>IAsyncLifetime</c> a propósito: no toca el esquema de la base, solo lee. Y va en
/// la colección «api» aunque no use su instancia compartida, para que xUnit **no** la ejecute en
/// paralelo con ella: la instancia compartida borra y recrea la base al arrancar, y hacerlo mientras
/// esta prueba tiene una conexión abierta la tumba sin que el fallo tenga nada que ver con el límite.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasLimiteDeAcceso
{
    [Fact]
    public async Task Demasiados_intentos_seguidos_se_cortan()
    {
        using var propia = new ApiDePrueba();
        var cliente = propia.CreateClient();
        var intento = new { email = "nadie@ribera.es", contrasena = "NoExiste1" };

        HttpResponseMessage? cortado = null;
        for (var i = 0; i < 25 && cortado is null; i++)
        {
            var r = await cliente.PostAsJsonAsync("/auth/login", intento);
            if (r.StatusCode == HttpStatusCode.TooManyRequests)
            {
                cortado = r;
            }
            else
            {
                // Mientras quedan intentos, la respuesta es la de siempre: credenciales incorrectas.
                r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            }
        }

        cortado.Should().NotBeNull("veinticinco intentos seguidos tienen que agotar el límite de veinte");
        cortado!.Headers.Should().ContainKey("Retry-After");

        var cuerpo = JsonDocument.Parse(await cortado.Content.ReadAsStringAsync()).RootElement;
        cuerpo.GetProperty("codigo").GetString().Should().Be("acceso.demasiados_intentos");
    }
}
