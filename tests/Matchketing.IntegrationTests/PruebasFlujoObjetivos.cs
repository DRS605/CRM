using System.Globalization;
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
public sealed class PruebasFlujoObjetivos(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string MesEnCurso =>
        new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<(HttpClient Cliente, Guid UsuarioId)> EnEmpresaAsync(string nombre = "Ribera Objetivos")
    {
        var cliente = api.CreateClient();
        var alta = await LeerAsync(await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = $"ob{Guid.NewGuid():N}@ribera.es",
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", alta.GetProperty("token").GetString());

        var empresa = await LeerAsync(await cliente.PostAsJsonAsync("/empresas", new { nombre, provincia = "Valencia" }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", empresa.GetProperty("token").GetString());

        return (cliente, alta.GetProperty("usuario").GetProperty("id").GetGuid());
    }

    /// <summary>
    /// Gana una oportunidad por el importe dado y la deja cerrada **este mes**.
    ///
    /// El camino real es crear contacto, oportunidad y pulsar «ganar», y así se hace: lo que se prueba es
    /// que el objetivo lee eso y no otra cosa. Solo la fecha se retoca cuando hace falta que el cierre
    /// caiga en otro mes.
    /// </summary>
    private static async Task<Guid> GanarAsync(HttpClient cliente, decimal importe)
    {
        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Manolo García",
            email = $"m{Guid.NewGuid():N}@casamanolo.es",
        }))).GetProperty("id").GetGuid();

        var oportunidad = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto,
            titulo = "Instalación",
            importe,
        }))).GetProperty("id").GetGuid();

        (await cliente.PostAsync(new Uri($"/oportunidades/{oportunidad}/ganar", UriKind.Relative), null))
            .IsSuccessStatusCode.Should().BeTrue();

        return oportunidad;
    }

    private async Task MoverCierreAsync(Guid oportunidadId, DateTimeOffset cuando)
    {
        using var alcance = api.Services.CreateScope();
        var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();

        var afectadas = await bd.Database.ExecuteSqlRawAsync(
            "UPDATE embudo.oportunidad SET cerrada_en = {0} WHERE id = {1}", cuando, oportunidadId);

        afectadas.Should().Be(1);
    }

    // ---------- Fijar ----------

    [Fact]
    public async Task Un_objetivo_de_cero_se_rechaza_con_su_motivo()
    {
        var (cliente, yo) = await EnEmpresaAsync();

        var r = await cliente.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 0 });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("objetivo.importe_invalido");
    }

    [Fact]
    public async Task El_objetivo_de_un_mes_que_ya_paso_no_se_puede_poner()
    {
        // Poner hoy el objetivo del mes pasado es escribir la historia después de conocerla.
        var (cliente, yo) = await EnEmpresaAsync();
        var pasado = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .AddMonths(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var r = await cliente.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = pasado, importe = 30000 });

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("objetivo.mes_pasado");
    }

    [Fact]
    public async Task Fijarlo_dos_veces_lo_cambia_y_no_crea_otro()
    {
        var (cliente, yo) = await EnEmpresaAsync();

        await cliente.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 30000 });
        var segundo = await LeerAsync(await cliente.PutAsJsonAsync(
            "/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 45000 }));

        segundo.GetProperty("importe").GetDecimal().Should().Be(45000m);

        var equipo = await LeerAsync(await cliente.GetAsync(new Uri("/objetivos/equipo", UriKind.Relative)));
        equipo.GetProperty("personas").EnumerateArray().Should().ContainSingle();
        equipo.GetProperty("objetivo").GetDecimal().Should().Be(45000m, "hay un objetivo, no dos");
    }

    [Fact]
    public async Task No_se_le_pone_objetivo_a_alguien_de_otra_empresa()
    {
        var (una, _) = await EnEmpresaAsync("Ribera Obj Uno");
        var (_, deOtra) = await EnEmpresaAsync("Ribera Obj Dos");

        var r = await una.PutAsJsonAsync("/objetivos", new { usuarioId = deOtra, mes = MesEnCurso, importe = 30000 });

        r.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be("objetivo.persona_no_esta");
    }

    // ---------- Cómo va ----------

    [Fact]
    public async Task Sin_objetivo_puesto_la_pantalla_no_ensena_nada()
    {
        // 204 y no 404: no tener objetivo es un estado normal, no una ruta que falte, y la pantalla no
        // tiene que distinguir «no hay» de «se ha roto algo». Y ni siquiera se enseña lo ganado: el
        // número solo dice algo al lado del compromiso.
        var (cliente, _) = await EnEmpresaAsync();
        await GanarAsync(cliente, 12_400m);

        var r = await cliente.GetAsync(new Uri("/objetivos/mio", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await r.Content.ReadAsStringAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Solo_cuenta_lo_ganado_y_no_lo_perdido_ni_lo_abierto()
    {
        var (cliente, yo) = await EnEmpresaAsync();
        await cliente.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 30000 });

        await GanarAsync(cliente, 12_000m);

        // Una perdida y una abierta, del mismo tamaño, que no deben sumar.
        var contacto = (await LeerAsync(await cliente.PostAsJsonAsync("/contactos", new
        {
            nombre = "Consuelo Beltrán",
            email = $"c{Guid.NewGuid():N}@beltran.es",
        }))).GetProperty("id").GetGuid();

        var perdida = (await LeerAsync(await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto, titulo = "La que no fue", importe = 50_000m,
        }))).GetProperty("id").GetGuid();
        (await cliente.PostAsJsonAsync($"/oportunidades/{perdida}/perder", new { motivo = 1, detalle = (string?)null }))
            .IsSuccessStatusCode.Should().BeTrue();

        await cliente.PostAsJsonAsync("/oportunidades", new
        {
            contactoId = contacto, titulo = "La que sigue viva", importe = 80_000m,
        });

        var mio = await LeerAsync(await cliente.GetAsync(new Uri("/objetivos/mio", UriKind.Relative)));

        mio.GetProperty("logrado").GetDecimal().Should().Be(12_000m,
            "un objetivo de venta se cumple cuando se firma, no cuando se apunta ni cuando se pierde");
    }

    [Fact]
    public async Task Lo_cerrado_en_otro_mes_no_cuenta_en_este()
    {
        var (cliente, yo) = await EnEmpresaAsync();
        await cliente.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 30000 });

        var deEsteMes = await GanarAsync(cliente, 5_000m);
        var deOtroMes = await GanarAsync(cliente, 25_000m);
        await MoverCierreAsync(deOtroMes, DateTimeOffset.UtcNow.AddMonths(-2));

        var mio = await LeerAsync(await cliente.GetAsync(new Uri("/objetivos/mio", UriKind.Relative)));

        mio.GetProperty("logrado").GetDecimal().Should().Be(5_000m);
        deEsteMes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task La_pantalla_dice_cuanto_falta_al_dia_que_es_el_numero_que_sirve()
    {
        // «Te faltan 18.400 € y quedan 7 días laborables» es lo que hace que alguien llame esta tarde. Un
        // 38 % no le dice a nadie si tiene que darse prisa.
        var (cliente, yo) = await EnEmpresaAsync();
        await cliente.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 30000 });
        await GanarAsync(cliente, 11_600m);

        var avance = (await LeerAsync(await cliente.GetAsync(new Uri("/objetivos/mio", UriKind.Relative))))
            .GetProperty("avance");

        avance.GetProperty("falta").GetDecimal().Should().Be(18_400m);
        avance.GetProperty("porcentaje").GetInt32().Should().Be(39);

        var dias = avance.GetProperty("diasLaborablesRestantes").GetInt32();
        dias.Should().BeGreaterThan(0, "el mes en curso siempre tiene algún día por delante");

        // El reparto cuadra con los días que dice tener: se comprueba la relación, no un número fijo,
        // porque la prueba corre cualquier día del mes.
        avance.GetProperty("porDiaQueQueda").GetDecimal()
            .Should().Be(Math.Round(18_400m / dias, 0));
    }

    [Fact]
    public async Task Pasarse_del_objetivo_se_ensena_tal_cual()
    {
        var (cliente, yo) = await EnEmpresaAsync();
        await cliente.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 10000 });
        await GanarAsync(cliente, 14_000m);

        var avance = (await LeerAsync(await cliente.GetAsync(new Uri("/objetivos/mio", UriKind.Relative))))
            .GetProperty("avance");

        avance.GetProperty("porcentaje").GetInt32().Should().Be(140, "esconder el mejor mes del año no ayuda a nadie");
        avance.GetProperty("cumplido").GetBoolean().Should().BeTrue();
        avance.GetProperty("falta").GetDecimal().Should().Be(0m);
        avance.GetProperty("porDiaQueQueda").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ---------- Equipo y permisos ----------

    [Fact]
    public async Task Un_comercial_ve_lo_suyo_y_no_los_objetivos_del_equipo()
    {
        var (propietario, _) = await EnEmpresaAsync("Ribera Obj Equipo");

        var invitacion = await LeerAsync(await propietario.PostAsJsonAsync("/equipo/invitaciones", new
        {
            email = $"vicent{Guid.NewGuid():N}@ribera.es",
            rol = 2, // comercial
        }));

        var comercial = api.CreateClient();
        var sesion = await LeerAsync(await comercial.PostAsJsonAsync(
            $"/invitaciones/{invitacion.GetProperty("token").GetString()}",
            new { nombre = "Vicent Ferrer", contrasena = "Levante2026" }));
        comercial.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sesion.GetProperty("token").GetString());
        var comercialId = sesion.GetProperty("usuario").GetProperty("id").GetGuid();

        await propietario.PutAsJsonAsync("/objetivos", new
        {
            usuarioId = comercialId, mes = MesEnCurso, importe = 20000,
        });

        // El suyo sí: si no pudiera verlo, el objetivo no serviría de nada.
        var mio = await LeerAsync(await comercial.GetAsync(new Uri("/objetivos/mio", UriKind.Relative)));
        mio.GetProperty("avance").GetProperty("objetivo").GetDecimal().Should().Be(20000m);

        // La tabla del equipo, no: es mirar el trabajo de los demás.
        (await comercial.GetAsync(new Uri("/objetivos/equipo", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Ni ponerse objetivo a sí mismo.
        (await comercial.PutAsJsonAsync("/objetivos", new
        {
            usuarioId = comercialId, mes = MesEnCurso, importe = 999,
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Su propio histórico sí lo puede ver.
        (await comercial.GetAsync(new Uri($"/objetivos/personas/{comercialId}/historico", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task La_tabla_del_equipo_no_saca_a_quien_no_puede_vender()
    {
        // Un objetivo de venta para quien no puede tocar una oportunidad es un objetivo que no puede
        // cumplir, así que ni aparece en la tabla donde se ponen.
        var (propietario, yo) = await EnEmpresaAsync("Ribera Obj Lectura");

        var invitacion = await LeerAsync(await propietario.PostAsJsonAsync("/equipo/invitaciones", new
        {
            email = $"rocio{Guid.NewGuid():N}@ribera.es",
            rol = 3, // solo lectura
        }));

        var lector = api.CreateClient();
        var sesion = await LeerAsync(await lector.PostAsJsonAsync(
            $"/invitaciones/{invitacion.GetProperty("token").GetString()}",
            new { nombre = "Rocío Ferrán", contrasena = "Levante2026" }));
        var lectorId = sesion.GetProperty("usuario").GetProperty("id").GetGuid();

        var equipo = await LeerAsync(await propietario.GetAsync(new Uri("/objetivos/equipo", UriKind.Relative)));
        var ids = equipo.GetProperty("personas").EnumerateArray()
            .Select(p => p.GetProperty("usuarioId").GetGuid()).ToList();

        ids.Should().Contain(yo);
        ids.Should().NotContain(lectorId);
    }

    [Fact]
    public async Task Los_objetivos_de_una_empresa_no_se_ven_desde_otra()
    {
        var (una, yo) = await EnEmpresaAsync("Ribera Obj Aislada Uno");
        var (otra, _) = await EnEmpresaAsync("Ribera Obj Aislada Dos");

        await una.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 30000 });

        var suyo = await LeerAsync(await otra.GetAsync(new Uri("/objetivos/equipo", UriKind.Relative)));

        suyo.GetProperty("objetivo").GetDecimal().Should().Be(0m);
        suyo.GetProperty("personas").EnumerateArray()
            .Should().NotContain(p => p.GetProperty("usuarioId").GetGuid() == yo);
    }

    [Fact]
    public async Task Quitar_el_objetivo_deja_de_ensenar_la_linea()
    {
        var (cliente, yo) = await EnEmpresaAsync();
        await cliente.PutAsJsonAsync("/objetivos", new { usuarioId = yo, mes = MesEnCurso, importe = 30000 });

        (await cliente.DeleteAsync(new Uri($"/objetivos/personas/{yo}?mes={MesEnCurso}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.GetAsync(new Uri("/objetivos/mio", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent,
                "quitarlo no es ponerlo a cero: la línea desaparece");
    }
}
