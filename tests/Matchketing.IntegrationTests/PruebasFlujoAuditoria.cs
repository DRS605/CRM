using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Auditoria.Dominio;
using Matchketing.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Matchketing.IntegrationTests;

[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoAuditoria(ApiDePrueba api)
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
        });
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> RegistroAsync(HttpClient cliente) =>
        await LeerAsync(await cliente.GetAsync(new Uri("/auditoria", UriKind.Relative)));

    [Fact]
    public async Task Una_empresa_recien_creada_no_tiene_nada_que_auditar()
    {
        var cliente = await EnEmpresaAsync("Ribera Auditoría Vacía");

        (await RegistroAsync(cliente)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Ganar_una_oportunidad_queda_apuntado_con_quien_la_gano()
    {
        var cliente = await EnEmpresaAsync("Ribera Auditoría Ganada");
        var contacto = await ContactoAsync(cliente, "Ganadora Ros");
        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Cocina completa",
            importe = 18400m,
        }))).GetProperty("id").GetGuid();

        await cliente.PostAsync(new Uri($"/oportunidades/{oportunidad}/ganar", UriKind.Relative), null);

        var linea = (await RegistroAsync(cliente)).EnumerateArray()
            .Single(l => l.GetProperty("accion").GetString() == Acciones.OportunidadGanada);

        linea.GetProperty("entidadId").GetGuid().Should().Be(oportunidad);
        linea.GetProperty("actor").GetString().Should().Be("Marta Ruiz");
        linea.GetProperty("detalle").GetString().Should().Contain("18400");
    }

    [Fact]
    public async Task Perder_una_oportunidad_apunta_el_motivo_pero_no_el_texto_libre()
    {
        // El detalle escrito a mano no entra: es donde la gente cuenta cosas de personas, y esto no
        // se puede borrar.
        var cliente = await EnEmpresaAsync("Ribera Auditoría Perdida");
        var contacto = await ContactoAsync(cliente, "Perdedora Blay");
        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Baño",
            importe = 4100m,
        }))).GetProperty("id").GetGuid();

        await cliente.PostAsJsonAsync($"/oportunidades/{oportunidad}/perder", new
        {
            motivo = 1,
            detalle = "Me lo dijo su marido por teléfono, que no les cuadraba.",
        });

        var detalle = (await RegistroAsync(cliente)).EnumerateArray()
            .Single(l => l.GetProperty("accion").GetString() == Acciones.OportunidadPerdida)
            .GetProperty("detalle").GetString()!;

        detalle.Should().Contain("motivo").And.NotContain("marido");
    }

    [Fact]
    public async Task Fusionar_dos_contactos_deja_rastro()
    {
        var cliente = await EnEmpresaAsync("Ribera Auditoría Fusión");
        var superviviente = await ContactoAsync(cliente, "Sara Grau");
        var absorbido = await ContactoAsync(cliente, "S. Grau");

        await cliente.PostAsJsonAsync($"/contactos/{superviviente}/fusionar", new { absorbidoId = absorbido });

        (await RegistroAsync(cliente)).EnumerateArray()
            .Should().Contain(l => l.GetProperty("accion").GetString() == Acciones.ContactoFusionado);
    }

    [Fact]
    public async Task Cambiar_los_ajustes_de_retencion_se_audita()
    {
        var cliente = await EnEmpresaAsync("Ribera Auditoría Ajustes");

        await cliente.PutAsJsonAsync("/empresas/activa/ajustes-retencion", new { mesesRetencionLeads = 36 });

        var linea = (await RegistroAsync(cliente)).EnumerateArray()
            .Single(l => l.GetProperty("accion").GetString() == Acciones.AjustesCambiados);

        linea.GetProperty("detalle").GetString().Should().Contain("36");
    }

    [Fact]
    public async Task La_baja_publica_se_apunta_como_del_sistema()
    {
        var cliente = await EnEmpresaAsync("Ribera Auditoría Baja");
        var contacto = await ContactoAsync(cliente, "Bajada Server");
        var enlace = (await LeerAsync(await cliente.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative))))
            .GetProperty("enlaceBaja").GetString()!;

        await api.CreateClient().PostAsync(new Uri(enlace[enlace.IndexOf("/b/", StringComparison.Ordinal)..], UriKind.Relative), null);

        var linea = (await RegistroAsync(cliente)).EnumerateArray()
            .Single(l => l.GetProperty("accion").GetString() == Acciones.ContactoBaja);

        linea.GetProperty("actorId").ValueKind.Should().Be(JsonValueKind.Null);
        linea.GetProperty("actor").GetString().Should().Be("el sistema");
    }

    [Fact]
    public async Task Cada_empresa_solo_ve_su_registro()
    {
        // El apunte se escribe desde la entrada pública de un formulario, sin token: si el aislamiento
        // se apoyara solo en el JWT, este es el caso que se escaparía.
        var una = await EnEmpresaAsync("Ribera Auditoría Uno");
        var otra = await EnEmpresaAsync("Ribera Auditoría Dos");

        var contacto = await ContactoAsync(una, "Suya Nomía");
        var oportunidad = (await LeerAsync(await una.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Suyo",
            importe = 100m,
        }))).GetProperty("id").GetGuid();
        await una.PostAsync(new Uri($"/oportunidades/{oportunidad}/ganar", UriKind.Relative), null);

        (await RegistroAsync(una)).GetArrayLength().Should().BeGreaterThan(0);
        (await RegistroAsync(otra)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Solo_quien_toca_los_ajustes_puede_leer_el_registro()
    {
        // Sin empresa activa no hay permisos en el token, así que el registro no se puede ni pedir.
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"c{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Sin Empresa",
        });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        (await cliente.GetAsync(new Uri("/auditoria", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task El_registro_no_se_puede_modificar_ni_borrar()
    {
        // La prueba del disparador `auditoria.solo_anadir`. Se hace con `SET ROLE` a un rol que no es
        // el propietario de la tabla, que es como se conecta la aplicación en producción: la regla
        // está para el día en que alguien intente arreglar un apunte «que estaba mal».
        var cliente = await EnEmpresaAsync("Ribera Auditoría Inmutable");
        var contacto = await ContactoAsync(cliente, "Inmutable Vidal");
        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Intocable",
            importe = 500m,
        }))).GetProperty("id").GetGuid();
        await cliente.PostAsync(new Uri($"/oportunidades/{oportunidad}/ganar", UriKind.Relative), null);

        var empresaId = (await LeerAsync(await cliente.GetAsync(new Uri("/empresas/activa", UriKind.Relative))))
            .GetProperty("id").GetGuid();

        using var alcance = api.Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();

        await bd.Database.ExecuteSqlRawAsync("""
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'mk_prueba_app') THEN
                    CREATE ROLE mk_prueba_app;
                END IF;
            END $$;
            GRANT USAGE ON SCHEMA auditoria TO mk_prueba_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON auditoria.registro TO mk_prueba_app;
            """);

        // La conexión se abre a mano: `SET ROLE` y `set_config` son estado de sesión, y si cada orden
        // cogiera una conexión distinta del pool se perderían entre una y otra.
        await bd.Database.OpenConnectionAsync();
        try
        {
            // Sin fijar la empresa, la RLS no dejaría ver ni una fila y el UPDATE afectaría a cero:
            // el disparador es BEFORE ... FOR EACH ROW y no llegaría a saltar. Parecería que la regla
            // no existe cuando lo que pasa es que la otra barrera actuó primero.
            await bd.Database.ExecuteSqlAsync($"SELECT set_config('app.empresa_actual', {empresaId.ToString()}, false)");
            await bd.Database.ExecuteSqlRawAsync("SET ROLE mk_prueba_app");

            (await bd.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM auditoria.registro").ToListAsync())
                .Single().Should().BeGreaterThan(0, "el rol de aplicación tiene que ver el registro de su empresa");

            Func<Task> ComoLaAplicacion(string orden) => () => bd.Database.ExecuteSqlRawAsync(orden);

            await ComoLaAplicacion("UPDATE auditoria.registro SET detalle = 'retocado'").Should()
                .ThrowAsync<PostgresException>().WithMessage("*solo admite INSERT*");

            await ComoLaAplicacion("DELETE FROM auditoria.registro").Should()
                .ThrowAsync<PostgresException>().WithMessage("*solo admite INSERT*");
        }
        finally
        {
            await bd.Database.ExecuteSqlRawAsync("RESET ROLE");
            await bd.Database.CloseConnectionAsync();
        }
    }
}
