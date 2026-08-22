using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// Lo que solo se rompe al desplegar.
///
/// Todas las pruebas de este proyecto corren en un sitio donde el despliegue no existe: sin proxio
/// delante, con un superusuario de base de datos y con los secretos de desarrollo puestos. Eso hace que
/// tres cosas correctas se vuelvan falsas en producción **sin que nada falle**, y esas tres cosas son
/// las de aquí.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasDespliegue(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>Una API igual que la de siempre pero con la configuración que se le indique cambiada.</summary>
    private sealed class ApiCon(IReadOnlyDictionary<string, string?> ajustes) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder constructor)
        {
            constructor.UseEnvironment(Environments.Production);
            constructor.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Matchketing"] = ApiDePrueba.Conexion,
                    ["Jwt:Clave"] = "clave-de-pruebas-de-integracion-suficientemente-larga-0123456789",
                    ["Baja:Secreto"] = "secreto-de-pruebas-de-integracion-suficientemente-largo-0123456789",
                    ["Baja:UrlBase"] = "https://pruebas.matchketing.es",
                    ["Aislamiento:PermitirSuperusuario"] = "true",
                }).AddInMemoryCollection(ajustes));
        }
    }

    // ---------- Los secretos ----------

    [Fact]
    public void No_arranca_en_produccion_con_la_clave_de_firma_de_desarrollo()
    {
        // Estaba escrita en el repositorio con un valor por defecto, así que un despliegue sin
        // configurarla funcionaba perfectamente y **cualquiera podía firmarse un token de sesión de
        // cualquier empresa**. No es un fallo que se note desde fuera: es el aislamiento entero.
        using var roto = new ApiCon(new Dictionary<string, string?>
        {
            ["Jwt:Clave"] = Matchketing.Api.Comun.Secretos.ClaveJwtDeDesarrollo,
        });

        var accion = () => roto.CreateClient();

        accion.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:Clave*")
            .WithMessage("*publicado en el repositorio*");
    }

    [Fact]
    public void No_arranca_en_produccion_con_el_secreto_de_las_bajas_de_desarrollo()
    {
        // Con el de desarrollo, cualquiera fabrica el enlace de baja de cualquier contacto.
        using var roto = new ApiCon(new Dictionary<string, string?>
        {
            ["Baja:Secreto"] = Matchketing.Api.Comun.Secretos.SecretoBajaDeDesarrollo,
        });

        ((Action)(() => roto.CreateClient())).Should().Throw<InvalidOperationException>()
            .WithMessage("*Baja:Secreto*");
    }

    [Fact]
    public void No_arranca_con_una_clave_de_firma_corta()
    {
        using var roto = new ApiCon(new Dictionary<string, string?> { ["Jwt:Clave"] = "corta" });

        ((Action)(() => roto.CreateClient())).Should().Throw<InvalidOperationException>()
            .WithMessage("*caracteres*");
    }

    [Fact]
    public void Sin_la_url_de_las_bajas_no_arranca()
    {
        // Su valor por defecto apunta a otro dominio: los enlaces de baja de los correos llevarían a un
        // sitio que no es este y la baja no llegaría nunca.
        using var roto = new ApiCon(new Dictionary<string, string?> { ["Baja:UrlBase"] = "" });

        ((Action)(() => roto.CreateClient())).Should().Throw<InvalidOperationException>()
            .WithMessage("*Baja:UrlBase*");
    }

    // ---------- Las dos barreras del aislamiento ----------

    [Fact]
    public async Task La_sonda_dice_que_falta_una_barrera_si_el_rol_es_superusuario()
    {
        // **La comprobación que ningún test podía hacer hasta ahora.** Las políticas por fila de
        // PostgreSQL no se aplican a un superusuario, así que un despliegue con `postgres` se queda con
        // una sola barrera —el filtro de EF Core— y nada falla: las pruebas pasan y la aplicación
        // funciona. Aquí falla, y falla donde lo ve un equilibrador de carga.
        //
        // Estas pruebas se conectan como superusuario a propósito —crean y borran la base—, así que la
        // instancia normal lleva el permiso puesto. Ésta no lo lleva, que es el caso de producción.
        using var comoDios = new ApiCon(new Dictionary<string, string?>
        {
            ["Aislamiento:PermitirSuperusuario"] = "false",
        });

        var r = await comoDios.CreateClient().GetAsync(new Uri("/salud", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var cuerpo = await LeerAsync(r);
        cuerpo.GetProperty("estado").GetString().Should().Be("enfermo");
        cuerpo.GetProperty("aislamiento").GetString().Should()
            .Contain("una sola barrera").And.Contain("superusuario");
    }

    [Fact]
    public async Task La_sonda_dice_las_dos_barreras_cuando_todo_esta_en_su_sitio()
    {
        var cuerpo = await LeerAsync(await api.CreateClient().GetAsync(new Uri("/salud", UriKind.Relative)));

        cuerpo.GetProperty("estado").GetString().Should().Be("vivo");
        cuerpo.GetProperty("aislamiento").GetString().Should().Be("dos barreras");
    }

    // ---------- La IP del cliente ----------

    /// <summary>Apunta un consentimiento y devuelve la IP que ha quedado guardada.</summary>
    private static async Task<string?> IpDelConsentimientoAsync(HttpClient cliente, string? inventada)
    {
        var alta = await LeerAsync(await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"ip{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", alta.GetProperty("token").GetString());

        var empresa = await LeerAsync(await cliente.PostAsJsonAsync(
            "/empresas", new { nombre = "Instalaciones Ribera", provincia = "Valencia" }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", empresa.GetProperty("token").GetString());

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync(
            "/contactos", new { nombre = "Manolo García", email = $"m{Guid.NewGuid():N}@casamanolo.es" })))
            .GetProperty("id").GetGuid();

        var peticion = new HttpRequestMessage(
            HttpMethod.Post, new Uri($"/cumplimiento/contactos/{contacto}/consentimientos", UriKind.Relative))
        {
            Content = JsonContent.Create(new { finalidad = 1, @base = 2, canal = "alta manual" }),
        };
        if (inventada is not null)
        {
            peticion.Headers.Add("X-Forwarded-For", inventada);
        }

        (await cliente.SendAsync(peticion)).IsSuccessStatusCode.Should().BeTrue();

        var copia = await LeerAsync(await cliente.GetAsync(
            new Uri($"/cumplimiento/contactos/{contacto}/exportar", UriKind.Relative)));

        return copia.GetProperty("consentimientos")[0].GetProperty("ip").GetString();
    }

    [Fact]
    public async Task Sin_proxio_declarado_una_cabecera_inventada_no_cambia_la_ip_del_consentimiento()
    {
        // **El arreglo obvio del problema del proxio es peor que el problema.** Si se confiara siempre
        // en `X-Forwarded-For`, cualquiera podría elegir la IP que queda escrita como prueba de que
        // aceptó recibir comunicaciones —y saltarse el techo de intentos de acceso cambiándola en cada
        // petición—. Así que sin declarar el proxio, la cabecera no se mira.
        var ip = await IpDelConsentimientoAsync(api.CreateClient(), "203.0.113.7");

        ip.Should().NotBe("203.0.113.7", "una cabecera que escribe el cliente no puede decidir la prueba");
    }

    [Fact]
    public async Task Con_el_proxio_declarado_la_ip_del_cliente_es_la_de_la_cabecera()
    {
        // Y con proxio declarado hay que leerla, o la prueba del consentimiento guarda la IP del proxio
        // —la misma para todo el mundo—, que es una prueba que no prueba nada.
        using var detrasDeProxio = new ApiCon(new Dictionary<string, string?>
        {
            ["Proxy:Confiar"] = "true",
        });

        var ip = await IpDelConsentimientoAsync(detrasDeProxio.CreateClient(), "203.0.113.7");

        ip.Should().Be("203.0.113.7");
    }

    // ---------- Cabeceras de seguridad ----------

    [Fact]
    public async Task La_pagina_sale_con_las_cabeceras_de_seguridad_puestas()
    {
        // Van en la aplicación y no en la configuración del proxio: una protección que vive en otro
        // programa se pierde en la primera mudanza.
        var r = await api.CreateClient().GetAsync(new Uri("/", UriKind.Relative));

        r.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        r.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        r.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");

        var csp = string.Join(" ", r.Headers.GetValues("Content-Security-Policy"));
        csp.Should().Contain("default-src 'self'").And.Contain("frame-ancestors 'none'");
    }
}
