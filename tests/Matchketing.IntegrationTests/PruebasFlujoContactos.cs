using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoContactos(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>Un cliente autenticado, con empresa propia creada y activa en el token.</summary>
    private async Task<HttpClient> EnEmpresaAsync(string nombreEmpresa)
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"u{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        });
        var token = (await LeerAsync(alta)).GetProperty("token").GetString();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var empresa = await cliente.PostAsJsonAsync("/empresas", new { nombre = nombreEmpresa, provincia = "Valencia" });
        var conEmpresa = (await LeerAsync(empresa)).GetProperty("token").GetString();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", conEmpresa);
        return cliente;
    }

    private static async Task<Guid> CrearContactoAsync(HttpClient cliente, string nombre, string? email = null, string? telefono = null)
    {
        var r = await cliente.PostAsJsonAsync("/contactos", new { nombre, email, telefono });
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Un_contacto_creado_aparece_en_el_listado()
    {
        var cliente = await EnEmpresaAsync("Ribera Listado");
        await CrearContactoAsync(cliente, "Manolo García", "manolo@casamanolo.es");

        var lista = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));

        lista.GetArrayLength().Should().Be(1);
        lista[0].GetProperty("nombre").GetString().Should().Be("Manolo García");
        lista[0].GetProperty("email").GetString().Should().Be("manolo@casamanolo.es");
    }

    [Fact]
    public async Task Un_contacto_sin_correo_ni_telefono_se_rechaza()
    {
        var cliente = await EnEmpresaAsync("Ribera Sin Medio");

        var r = await cliente.PostAsJsonAsync("/contactos", new { nombre = "Fantasma" });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("contacto.sin_medio");
    }

    [Fact]
    public async Task Una_empresa_no_ve_los_contactos_de_otra()
    {
        var unaEmpresa = await EnEmpresaAsync("Ribera Uno");
        var otraEmpresa = await EnEmpresaAsync("Ribera Dos");

        await CrearContactoAsync(unaEmpresa, "Secreto de la primera", "secreto@uno.es");

        var vistosPorLaOtra = await LeerAsync(await otraEmpresa.GetAsync(new Uri("/contactos", UriKind.Relative)));

        vistosPorLaOtra.GetArrayLength().Should().Be(0, "el filtro global por empresa no debe dejar pasar nada");
    }

    [Fact]
    public async Task La_ficha_de_un_contacto_de_otra_empresa_no_existe_para_mi()
    {
        var unaEmpresa = await EnEmpresaAsync("Ribera Ficha A");
        var otraEmpresa = await EnEmpresaAsync("Ribera Ficha B");
        var id = await CrearContactoAsync(unaEmpresa, "Secreto", "secreto2@uno.es");

        var r = await otraEmpresa.GetAsync(new Uri($"/contactos/{id}", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task El_telefono_se_normaliza_asi_que_se_busca_escrito_de_cualquier_forma()
    {
        var cliente = await EnEmpresaAsync("Ribera Teléfono");
        await CrearContactoAsync(cliente, "Manolo", null, "96 123 45 67");

        var lista = await LeerAsync(await cliente.GetAsync(new Uri("/contactos?busqueda=%2B34961234567", UriKind.Relative)));

        lista.GetArrayLength().Should().Be(1);
        lista[0].GetProperty("telefono").GetString().Should().Be("+34961234567");
    }

    [Fact]
    public async Task Registrar_una_llamada_deja_rastro_en_la_cronologia()
    {
        var cliente = await EnEmpresaAsync("Ribera Llamada");
        var id = await CrearContactoAsync(cliente, "Manolo", "llamada@uno.es");

        var r = await cliente.PostAsJsonAsync($"/contactos/{id}/llamada", new { resultado = 2, nota = "Probar por la tarde." });
        r.StatusCode.Should().Be(HttpStatusCode.Created);

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{id}", UriKind.Relative)));
        var crono = ficha.GetProperty("cronologia");

        crono.GetArrayLength().Should().Be(1);
        crono[0].GetProperty("cuerpo").GetString().Should().Contain("no contesta");
        crono[0].GetProperty("resultado").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Una_nota_vacia_no_se_guarda()
    {
        var cliente = await EnEmpresaAsync("Ribera Nota");
        var id = await CrearContactoAsync(cliente, "Manolo", "nota@uno.es");

        var r = await cliente.PostAsJsonAsync($"/contactos/{id}/notas", new { cuerpo = "   " });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("actividad.cuerpo_vacio");
    }

    [Fact]
    public async Task La_previsualizacion_de_una_importacion_no_guarda_nada()
    {
        var cliente = await EnEmpresaAsync("Ribera Previsualiza");
        var csv = "Nombre;Correo;Teléfono\nManolo García;manolo@prev.es;961234567\nAna Soler;ana@prev.es;961234568";

        var r = await LeerAsync(await cliente.PostAsJsonAsync("/contactos/importar", new { contenido = csv, previsualizar = true }));

        r.GetProperty("validas").GetInt32().Should().Be(2);
        r.GetProperty("creados").GetInt32().Should().Be(0);

        var lista = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        lista.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Al_confirmar_la_importacion_se_crean_las_filas_validas_y_se_avisa_de_las_malas()
    {
        var cliente = await EnEmpresaAsync("Ribera Importa");
        var csv = string.Join("\n",
            "Nombre;Correo;Teléfono",
            "Manolo García;manolo@imp.es;961234567",
            ";sin-nombre@imp.es;961234500",
            "Sin medio;;",
            "Ana Soler;ana@imp.es;");

        var r = await LeerAsync(await cliente.PostAsJsonAsync("/contactos/importar", new { contenido = csv, previsualizar = false }));

        r.GetProperty("creados").GetInt32().Should().Be(2);
        var errores = r.GetProperty("errores");
        errores.GetArrayLength().Should().Be(2);
        errores[0].GetProperty("linea").GetInt32().Should().Be(3, "la cabecera es la línea 1 y la gente cuenta desde 1");

        var lista = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        lista.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task La_importacion_omite_lo_que_ya_existe()
    {
        var cliente = await EnEmpresaAsync("Ribera Repetidos");
        await CrearContactoAsync(cliente, "Manolo García", "manolo@rep.es");

        var csv = "Nombre;Correo\nManolo García;manolo@rep.es\nAna Soler;ana@rep.es";
        var r = await LeerAsync(await cliente.PostAsJsonAsync("/contactos/importar", new { contenido = csv, previsualizar = false }));

        r.GetProperty("duplicadas").GetInt32().Should().Be(1);
        r.GetProperty("creados").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Dos_contactos_con_el_mismo_telefono_escrito_distinto_se_detectan_como_duplicados()
    {
        var cliente = await EnEmpresaAsync("Ribera Duplicados");
        await CrearContactoAsync(cliente, "Manolo García", "manolo@dup.es", "961234567");
        await CrearContactoAsync(cliente, "M. García", "mgarcia@dup.es", "+34 961 23 45 67");

        var pares = await LeerAsync(await cliente.GetAsync(new Uri("/contactos/duplicados", UriKind.Relative)));

        pares.GetArrayLength().Should().Be(1);
        pares[0].GetProperty("motivo").GetString().Should().Be("Mismo teléfono");
    }

    [Fact]
    public async Task Fusionar_se_trae_todas_las_actividades_y_no_pierde_ninguna()
    {
        var cliente = await EnEmpresaAsync("Ribera Fusión");
        var superviviente = await CrearContactoAsync(cliente, "Manolo García", "manolo@fus.es");
        var absorbido = await CrearContactoAsync(cliente, "M. García", "mgarcia@fus.es", "961234567");

        await cliente.PostAsJsonAsync($"/contactos/{superviviente}/notas", new { cuerpo = "Nota del que se queda" });
        await cliente.PostAsJsonAsync($"/contactos/{absorbido}/notas", new { cuerpo = "Nota del absorbido" });
        await cliente.PostAsJsonAsync($"/contactos/{absorbido}/llamada", new { resultado = 1, nota = (string?)null });

        var fusion = await cliente.PostAsJsonAsync($"/contactos/{superviviente}/fusionar", new { absorbidoId = absorbido });
        fusion.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LeerAsync(fusion)).GetProperty("actividadesMovidas").GetInt32().Should().Be(2);

        var ficha = await LeerAsync(await cliente.GetAsync(new Uri($"/contactos/{superviviente}", UriKind.Relative)));

        // 1 propia + 2 traídas + 1 apunte del sistema que deja constancia de la fusión.
        ficha.GetProperty("cronologia").GetArrayLength().Should().Be(4);
        ficha.GetProperty("contacto").GetProperty("telefono").GetString().Should().Be("+34961234567", "el hueco se rellena con el dato del absorbido");

        var lista = await LeerAsync(await cliente.GetAsync(new Uri("/contactos", UriKind.Relative)));
        lista.GetArrayLength().Should().Be(1, "el absorbido deja de aparecer, pero no se borra");
    }

    [Fact]
    public async Task Las_politicas_de_RLS_estan_puestas_en_las_tablas_de_negocio()
    {
        using var alcance = api.Services.CreateAsyncScope();
        var bd = alcance.ServiceProvider.GetRequiredService<Persistencia.ContextoMatchketing>();

        var conexion = bd.Database.GetDbConnection();
        await conexion.OpenAsync();
        await using var orden = conexion.CreateCommand();
        orden.CommandText = """
            SELECT count(*) FROM pg_policies
            WHERE schemaname = 'contactos' AND policyname = 'aislamiento_empresa'
            """;
        var politicas = Convert.ToInt32(await orden.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);

        politicas.Should().Be(3, "cuenta, contacto y actividad deben tener su política de aislamiento");
    }
}
