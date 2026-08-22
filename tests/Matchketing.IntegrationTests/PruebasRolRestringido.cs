using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// La API **con un rol de base de datos normal**, o sea con las políticas por fila de PostgreSQL
/// aplicándose de verdad.
///
/// Todas las demás pruebas se conectan como superusuario, porque crean y borran la base en cada
/// arranque. Y a un superusuario **no se le aplica la seguridad por fila**, así que durante diecisiete
/// módulos la segunda barrera del aislamiento no se ejerció ni una vez desde el código: se comprobaba
/// con un guion de bash suelto, a mano, y solo que no dejara ver lo que no toca.
///
/// Lo que eso escondía se vio en el primer arranque de verdad: **no se podía crear una empresa**. Quien
/// se registra no pertenece a ninguna todavía, así que `app.empresa_actual` estaba vacío y PostgreSQL
/// rechazaba la empresa, su embudo y sus etapas con «new row violates row-level security policy». La
/// primera pantalla después de registrarse, y ninguna prueba podía verlo.
///
/// Esta clase cierra ese hueco: crea el rol restringido con **el mismo guion de permisos que usa el
/// despliegue** —así se prueba también el guion— y levanta la API con él.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasRolRestringido : IAsyncLifetime
{
    /// <summary>
    /// **No se llama `matchketing_app`.** Los roles son del servidor entero, no de una base, así que
    /// usar el nombre de producción le cambiaría la contraseña al despliegue de quien tenga los dos en
    /// la misma máquina. El guion de permisos acepta el nombre por `SET mk.rol`, y así se ejerce
    /// también ese camino.
    /// </summary>
    private const string Rol = "matchketing_pruebas";

    private const string Clave = "clave-del-rol-de-pruebas";

    private ApiRestringida? api;

    private sealed class ApiRestringida(string conexion) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder constructor)
        {
            constructor.UseEnvironment(Environments.Production);
            constructor.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Matchketing"] = conexion,
                    ["Jwt:Clave"] = "clave-de-pruebas-de-integracion-suficientemente-larga-0123456789",
                    ["Baja:Secreto"] = "secreto-de-pruebas-de-integracion-suficientemente-largo-0123456789",
                    ["Baja:UrlBase"] = "https://pruebas.matchketing.es",

                    // Aquí **no** se permite el superusuario: es justo lo que se está probando.
                }));
        }
    }

    public async Task InitializeAsync()
    {
        var deAdministrador = new NpgsqlConnectionStringBuilder(ApiDePrueba.Conexion);

        await using var bd = new NpgsqlConnection(deAdministrador.ConnectionString);
        await bd.OpenAsync();

        // El rol de las pruebas. Los permisos se los da **el guion del despliegue**, que es lo que se
        // quiere probar de paso.
        await Ejecutar(bd, $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{Rol}') THEN
                    CREATE ROLE {Rol} LOGIN;
                END IF;
            END
            $$;
            ALTER ROLE {Rol} NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOINHERIT;
            ALTER ROLE {Rol} PASSWORD '{Clave}';
            GRANT CONNECT ON DATABASE "{deAdministrador.Database}" TO {Rol};
            GRANT USAGE ON SCHEMA public TO {Rol};
            """);

        // Y los permisos, con el guion de verdad. `SET LOCAL` no sirve: el guion abre sus propios
        // bloques y hace falta que el ajuste siga puesto al leerlo dentro.
        await Ejecutar(bd, $"SET mk.rol = '{Rol}';\n" + LeerDelRepositorio("scripts/bd/permisos.sql"));

        api = new ApiRestringida(new NpgsqlConnectionStringBuilder(ApiDePrueba.Conexion)
        {
            Username = Rol,
            Password = Clave,
        }.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (api is not null)
        {
            await api.DisposeAsync();
        }
    }

    private static async Task Ejecutar(NpgsqlConnection bd, string sql)
    {
        await using var orden = bd.CreateCommand();
        orden.CommandText = sql;
        await orden.ExecuteNonQueryAsync();
    }

    private static string LeerDelRepositorio(string relativo)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Matchketing.sln")))
        {
            carpeta = carpeta.Parent;
        }

        carpeta.Should().NotBeNull("las pruebas tienen que poder encontrar la raíz del repositorio");
        return File.ReadAllText(Path.Combine(carpeta!.FullName, relativo));
    }

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private HttpClient Cliente => api!.CreateClient();

    private async Task<(HttpClient Cliente, JsonElement Sesion)> EnEmpresaAsync(string nombre)
    {
        var cliente = Cliente;
        var alta = await LeerAsync(await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"rol{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", alta.GetProperty("token").GetString());

        var respuesta = await cliente.PostAsJsonAsync("/empresas", new { nombre, provincia = "Valencia" });
        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await respuesta.Content.ReadAsStringAsync());

        var sesion = await LeerAsync(respuesta);
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sesion.GetProperty("token").GetString());

        return (cliente, sesion);
    }

    [Fact]
    public async Task Se_puede_crear_una_empresa_con_las_politicas_por_fila_puestas()
    {
        // **La prueba del fallo que solo aparecía en producción.** Antes del arreglo, esto devolvía 500
        // con «new row violates row-level security policy for table "embudo"»: la empresa activa de la
        // petición todavía no existía cuando se guardaban la empresa, su embudo y sus cinco etapas.
        var (cliente, _) = await EnEmpresaAsync("Instalaciones Ribera");

        var tablero = await LeerAsync(await cliente.GetAsync(new Uri("/embudo/tablero", UriKind.Relative)));

        tablero.GetProperty("columnas").EnumerateArray().Should().HaveCount(5,
            "la empresa nace con su embudo de cinco etapas, y esas filas también pasan por la política");
    }

    [Fact]
    public async Task El_trabajo_normal_de_un_dia_funciona_con_el_rol_restringido()
    {
        // Un contacto, una oportunidad, una nota y una tarea: cuatro tablas con política de inserción.
        // Si a una le faltara el permiso o la empresa activa, aquí se ve.
        var (cliente, _) = await EnEmpresaAsync("Ribera Trabajo");

        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García",
            email = $"m{Guid.NewGuid():N}@casamanolo.es",
        }))).GetProperty("id").GetGuid();

        var oportunidad = await cliente.PostAsJsonAsync(
            "/oportunidades", new { contactoId = contacto, titulo = "Caldera", importe = 4200m });
        oportunidad.IsSuccessStatusCode.Should().BeTrue(await oportunidad.Content.ReadAsStringAsync());

        (await cliente.PostAsJsonAsync($"/contactos/{contacto}/notas", new { cuerpo = "Quiere presupuesto." }))
            .IsSuccessStatusCode.Should().BeTrue();
        (await cliente.PostAsJsonAsync("/tareas", new { titulo = "Mandar presupuesto", contactoId = contacto }))
            .IsSuccessStatusCode.Should().BeTrue();

        (await cliente.GetAsync(new Uri("/hoy", UriKind.Relative))).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Con_la_segunda_barrera_puesta_una_empresa_sigue_sin_ver_a_la_otra()
    {
        // El aislamiento ya está probado en muchas pruebas, pero **todas con el filtro de EF Core como
        // única barrera de verdad**. Ésta es la única que lo comprueba con las dos puestas.
        var (ribera, _) = await EnEmpresaAsync("Ribera Aislada");
        var (vecina, _) = await EnEmpresaAsync("Clima Vecina");

        var suyo = (await LeerAsync(await ribera.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García",
            email = $"m{Guid.NewGuid():N}@casamanolo.es",
        }))).GetProperty("id").GetGuid();

        (await LeerAsync(await vecina.GetAsync(new Uri("/contactos", UriKind.Relative))))
            .EnumerateArray().Should().BeEmpty("la vecina no tiene contactos propios y no puede ver los de al lado");

        (await vecina.GetAsync(new Uri($"/contactos/{suyo}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task La_sonda_confirma_que_hay_dos_barreras()
    {
        var cuerpo = await LeerAsync(await Cliente.GetAsync(new Uri("/salud", UriKind.Relative)));

        cuerpo.GetProperty("aislamiento").GetString().Should().Be("dos barreras",
            "el rol de estas pruebas no es superusuario, así que las políticas por fila se le aplican");
    }
}
