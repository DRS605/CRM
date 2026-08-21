using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Matchketing.Identidad.Dominio;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// El equipo, contra PostgreSQL de verdad. Lo que solo se puede comprobar aquí: que la invitación
/// atraviesa la RLS con la empresa que lleva dentro del token, que dos empresas no se ven el equipo, y
/// que quien acepta sale con la sesión puesta y con los permisos de su rol y no de otro.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoEquipo(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string CorreoNuevo() => $"e{Guid.NewGuid():N}@ribera.es";

    private async Task<HttpClient> EnEmpresaAsync(string nombreEmpresa = "Instalaciones Ribera")
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = CorreoNuevo(),
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

    private static async Task<string> InvitarAsync(HttpClient cliente, string email, int rol = 2)
    {
        var r = await cliente.PostAsJsonAsync("/equipo/invitaciones", new { email, rol });
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await LeerAsync(r)).GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Una_empresa_puede_tener_dos_personas()
    {
        // Hasta este módulo no podía: la única membresía que se creaba nunca era la del propietario al
        // crear la empresa, así que los roles Comercial y Solo lectura eran inalcanzables.
        var duena = await EnEmpresaAsync();
        var email = CorreoNuevo();

        var token = await InvitarAsync(duena, email);
        var aceptada = await api.CreateClient().PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative), new { nombre = "Vicent Llopis", contrasena = "Vinaros2026" });

        aceptada.StatusCode.Should().Be(HttpStatusCode.OK);
        var sesion = await LeerAsync(aceptada);
        sesion.GetProperty("nombreEmpresa").GetString().Should().Be("Instalaciones Ribera");
        sesion.GetProperty("usuario").GetProperty("email").GetString().Should().Be(email);

        // Un comercial: exactamente los permisos de su rol, y ninguno de los de administrar. Se compara
        // contra `PermisosDeRol` y no contra un número escrito a mano: un número se queda desfasado al
        // añadir un permiso, y entonces la prueba falla por algo que no tiene nada que ver con el equipo.
        var permisos = sesion.GetProperty("permisos").EnumerateArray().Select(p => p.GetString()).ToList();
        permisos.Should().BeEquivalentTo(PermisosDeRol.De(Rol.Comercial));
        permisos.Should().NotContain("usuario.gestionar");
        permisos.Should().NotContain("empresa.ajustes");
        permisos.Should().Contain("contacto.gestionar");

        var equipo = await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative)));
        equipo.GetProperty("miembros").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task El_enlace_lleva_la_empresa_dentro_y_por_eso_se_puede_abrir_sin_sesion()
    {
        // Sin sesión no hay inquilino, y sin inquilino la RLS de PostgreSQL no devuelve ni una fila. La
        // empresa sale del propio token y se fija antes de consultar; es el mismo truco que el enlace
        // de baja y el píxel de apertura, y sin él esto devolvería siempre «no vale».
        var duena = await EnEmpresaAsync("Bar Nou");
        var token = await InvitarAsync(duena, CorreoNuevo(), rol: 3);

        var abierta = await LeerAsync(await api.CreateClient().GetAsync(new Uri($"/invitaciones/{token}", UriKind.Relative)));

        abierta.GetProperty("empresa").GetString().Should().Be("Bar Nou");
        abierta.GetProperty("rolTexto").GetString().Should().Be("solo lectura");
        abierta.GetProperty("yaTieneCuenta").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Un_enlace_de_otra_empresa_no_sirve_para_entrar_en_la_tuya()
    {
        var una = await EnEmpresaAsync("Ribera Uno");
        var dos = await EnEmpresaAsync("Ribera Dos");

        var tokenDeUna = await InvitarAsync(una, CorreoNuevo());

        // Quien acepta entra en la empresa que dice **el enlace**, no en la que tenga abierta quien
        // pulsa. Con la precedencia al revés —token de sesión por encima de la empresa fijada— esto
        // habría metido a la persona en Ribera Dos.
        var aceptada = await dos.PostAsJsonAsync(
            new Uri($"/invitaciones/{tokenDeUna}", UriKind.Relative), new { nombre = "Vicent", contrasena = "Vinaros2026" });

        aceptada.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LeerAsync(aceptada)).GetProperty("nombreEmpresa").GetString().Should().Be("Ribera Uno");

        (await LeerAsync(await dos.GetAsync(new Uri("/equipo", UriKind.Relative))))
            .GetProperty("miembros").GetArrayLength().Should().Be(1, "en Ribera Dos no ha entrado nadie");
    }

    [Fact]
    public async Task Cada_empresa_ve_solo_su_equipo_y_sus_invitaciones()
    {
        var una = await EnEmpresaAsync("Ribera Uno");
        var dos = await EnEmpresaAsync("Ribera Dos");

        await InvitarAsync(una, CorreoNuevo());
        await InvitarAsync(una, CorreoNuevo());

        (await LeerAsync(await una.GetAsync(new Uri("/equipo", UriKind.Relative))))
            .GetProperty("invitaciones").GetArrayLength().Should().Be(2);
        (await LeerAsync(await dos.GetAsync(new Uri("/equipo", UriKind.Relative))))
            .GetProperty("invitaciones").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Un_comercial_ve_el_equipo_pero_no_lo_cambia()
    {
        var duena = await EnEmpresaAsync();
        var token = await InvitarAsync(duena, CorreoNuevo());
        var comercial = api.CreateClient();
        var sesion = await LeerAsync(await comercial.PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative), new { nombre = "Vicent Llopis", contrasena = "Vinaros2026" }));
        comercial.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sesion.GetProperty("token").GetString());

        // Verlo sí: necesita saber a quién asignarle un lead y quién lleva su zona.
        var visto = await LeerAsync(await comercial.GetAsync(new Uri("/equipo", UriKind.Relative)));
        visto.GetProperty("miembros").GetArrayLength().Should().Be(2);
        visto.GetProperty("puedeGestionar").GetBoolean().Should().BeFalse();
        visto.GetProperty("invitaciones").GetArrayLength().Should()
            .Be(0, "las direcciones de quien no ha entrado todavía no son asunto suyo");

        // Cambiarlo, no.
        var miId = visto.GetProperty("miembros").EnumerateArray()
            .First(m => m.GetProperty("rolTexto").GetString() == "propietario").GetProperty("id").GetGuid();

        (await comercial.PostAsJsonAsync("/equipo/invitaciones", new { email = CorreoNuevo(), rol = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await comercial.PutAsJsonAsync($"/equipo/{miId}/rol", new { rol = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await comercial.DeleteAsync(new Uri($"/equipo/{miId}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Al_ascender_a_alguien_sus_permisos_cambian_en_el_siguiente_token()
    {
        var duena = await EnEmpresaAsync();
        var email = CorreoNuevo();
        var token = await InvitarAsync(duena, email);
        var comercial = api.CreateClient();
        await comercial.PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative), new { nombre = "Vicent Llopis", contrasena = "Vinaros2026" });

        var equipo = await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative)));
        var suId = equipo.GetProperty("miembros").EnumerateArray()
            .First(m => m.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();

        (await duena.PutAsJsonAsync($"/equipo/{suId}/rol", new { rol = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // El token viejo sigue diciendo «comercial» hasta que caduque —los permisos van firmados
        // dentro—, así que lo que se comprueba es el siguiente: entrar otra vez y elegir la empresa.
        var entrada = await LeerAsync(await api.CreateClient().PostAsJsonAsync(
            "/auth/login", new { email, contrasena = "Vinaros2026" }));
        var recien = api.CreateClient();
        recien.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", entrada.GetProperty("token").GetString());

        var empresaId = (await LeerAsync(await recien.GetAsync(new Uri("/auth/yo", UriKind.Relative))))
            .GetProperty("empresas")[0].GetProperty("id").GetGuid();
        var conEmpresa = await LeerAsync(await recien.PostAsync(
            new Uri($"/empresas/{empresaId}/seleccionar", UriKind.Relative), null));

        conEmpresa.GetProperty("permisos").EnumerateArray().Select(p => p.GetString())
            .Should().Contain("usuario.gestionar");
    }

    [Fact]
    public async Task Quitarle_el_acceso_deja_a_la_persona_fuera_de_la_empresa()
    {
        var duena = await EnEmpresaAsync();
        var email = CorreoNuevo();
        var token = await InvitarAsync(duena, email);
        await api.CreateClient().PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative), new { nombre = "Vicent Llopis", contrasena = "Vinaros2026" });

        var equipo = await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative)));
        var suId = equipo.GetProperty("miembros").EnumerateArray()
            .First(m => m.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();

        (await duena.DeleteAsync(new Uri($"/equipo/{suId}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // La cuenta sigue existiendo y se puede entrar: lo que se le ha quitado es **esta empresa**.
        var entrada = api.CreateClient();
        entrada.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await LeerAsync(await entrada.PostAsJsonAsync("/auth/login", new { email, contrasena = "Vinaros2026" })))
                .GetProperty("token").GetString());

        (await LeerAsync(await entrada.GetAsync(new Uri("/auth/yo", UriKind.Relative))))
            .GetProperty("empresas").GetArrayLength().Should().Be(0);

        // Y sigue en la lista del equipo, marcada como inactiva: sus contactos siguen asignados a su
        // nombre y la pantalla tiene que poder nombrarla.
        var despues = await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative)));
        despues.GetProperty("miembros").EnumerateArray()
            .First(m => m.GetProperty("email").GetString() == email)
            .GetProperty("activa").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Las_zonas_se_guardan_y_el_reparto_de_leads_las_ve()
    {
        // El primer factor del reparto del Match. Estaba siempre vacío porque no había forma de
        // rellenarlo: repartía por zona sin que nadie tuviera zona.
        var duena = await EnEmpresaAsync();
        var equipo = await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative)));
        var miId = equipo.GetProperty("miembros")[0].GetProperty("id").GetGuid();

        (await duena.PutAsJsonAsync($"/equipo/{miId}/zonas", new { zonas = "Valencia, Castellón" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var despues = await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative)));
        despues.GetProperty("miembros")[0].GetProperty("zonas").EnumerateArray()
            .Select(z => z.GetString()).Should().BeEquivalentTo(["Valencia", "Castellón"]);

        // Y la persona sale entre quienes pueden recibir un lead. Que llevar la zona gane a no llevarla
        // ya lo prueba `PruebasRepartidor.Quien_lleva_la_zona_gana_a_quien_no`: lo que faltaba no era
        // la puntuación, era poder rellenar la zona.
        var comerciales = await LeerAsync(await duena.GetAsync(new Uri("/match/comerciales", UriKind.Relative)));
        comerciales.EnumerateArray().Should().ContainSingle();
    }

    [Fact]
    public async Task Retirar_una_invitacion_deja_el_enlace_sin_valor()
    {
        var duena = await EnEmpresaAsync();
        var token = await InvitarAsync(duena, CorreoNuevo());
        var id = (await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative))))
            .GetProperty("invitaciones")[0].GetProperty("id").GetGuid();

        (await duena.DeleteAsync(new Uri($"/equipo/invitaciones/{id}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var aceptada = await api.CreateClient().PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative), new { nombre = "Vicent", contrasena = "Vinaros2026" });

        aceptada.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LeerAsync(aceptada)).GetProperty("codigo").GetString().Should().Be("invitacion.no_vale");
    }

    [Fact]
    public async Task Una_contrasena_floja_no_gasta_la_invitacion()
    {
        // Equivocarse tecleando la contraseña no puede dejar a alguien fuera y sin enlace.
        var duena = await EnEmpresaAsync();
        var token = await InvitarAsync(duena, CorreoNuevo());

        var flojo = await api.CreateClient().PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative), new { nombre = "Vicent", contrasena = "corta" });
        flojo.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var buena = await api.CreateClient().PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative), new { nombre = "Vicent", contrasena = "Vinaros2026" });
        buena.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Adivinar_la_contrasena_de_una_invitacion_se_corta_sin_estorbar_a_las_demas()
    {
        // Aceptar comprueba una contraseña cuando la cuenta ya existe, así que necesita techo. Lo que
        // hace este caso distinto del de entrar es **de quién es el cubo**: aquí solo se puede adivinar
        // la contraseña de una cuenta, la del correo de esa invitación, así que el cubo es la
        // invitación. Con uno por IP, una oficina entera dándose de alta se habría estorbado a sí
        // misma; compartiéndolo con el de entrar, habría dejado sin acceso a todos los demás.
        var duena = await EnEmpresaAsync();

        var email = CorreoNuevo();
        var cuenta = api.CreateClient();
        await cuenta.PostAsJsonAsync("/auth/registro", new { email, contrasena = "Levante2026", nombre = "Vicent Llopis" });
        var token = await InvitarAsync(duena, email);

        var otroToken = await InvitarAsync(duena, CorreoNuevo());

        HttpResponseMessage? cortado = null;
        for (var i = 0; i < 8 && cortado is null; i++)
        {
            var r = await api.CreateClient().PostAsJsonAsync(
                new Uri($"/invitaciones/{token}", UriKind.Relative), new { contrasena = "MeLoInvento1" });

            if (r.StatusCode == HttpStatusCode.TooManyRequests)
            {
                cortado = r;
            }
            else
            {
                r.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "mientras quedan intentos, la respuesta es la de siempre");
            }
        }

        cortado.Should().NotBeNull("ocho intentos seguidos sobre la misma invitación tienen que agotar el límite");

        // Y la otra invitación sigue funcionando: cubos distintos.
        (await api.CreateClient().PostAsJsonAsync(
            new Uri($"/invitaciones/{otroToken}", UriKind.Relative), new { nombre = "Amparo Gil", contrasena = "Vinaros2026" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_token_inventado_contesta_lo_mismo_que_uno_caducado()
    {
        var cliente = api.CreateClient();

        foreach (var token in new[] { "esto-no-es-un-token", "AAAAAAAAAAAAAAAAAAAAAA" + new string('B', 43) })
        {
            var r = await cliente.GetAsync(new Uri($"/invitaciones/{token}", UriKind.Relative));

            r.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await LeerAsync(r)).GetProperty("codigo").GetString().Should().Be(
                "invitacion.no_vale", "distinguirlos diría a quien prueba tokens cuáles existieron");
        }
    }

    [Fact]
    public async Task Invitar_y_quitar_el_acceso_queda_en_la_auditoria_sin_el_correo_de_nadie()
    {
        var duena = await EnEmpresaAsync();
        var email = CorreoNuevo();
        var token = await InvitarAsync(duena, email);
        await api.CreateClient().PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative), new { nombre = "Vicent Llopis", contrasena = "Vinaros2026" });

        var suId = (await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative))))
            .GetProperty("miembros").EnumerateArray()
            .First(m => m.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();
        await duena.DeleteAsync(new Uri($"/equipo/{suId}", UriKind.Relative));

        var registro = await (await duena.GetAsync(new Uri("/auditoria", UriKind.Relative))).Content.ReadAsStringAsync();

        registro.Should().Contain("equipo.invitado");
        registro.Should().Contain("equipo.acceso_retirado");

        // Dar y quitar acceso a los datos de los clientes es la operación más delicada que hay aquí, y
        // aun así el correo de la persona no entra en el registro: se apunta el rol y el identificador.
        registro.Should().NotContain(email);
    }

    [Fact]
    public async Task Sin_empresa_activa_no_hay_equipo_que_ver()
    {
        var cliente = api.CreateClient();
        var alta = await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = CorreoNuevo(), contrasena = "Levante2026", nombre = "Marta Ruiz",
        });
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", (await LeerAsync(alta)).GetProperty("token").GetString());

        (await cliente.GetAsync(new Uri("/equipo", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Un cliente ya dentro de la empresa con el papel que se pida.</summary>
    private async Task<HttpClient> ComoAsync(HttpClient duena, int rol)
    {
        var token = await InvitarAsync(duena, CorreoNuevo(), rol);
        var cliente = api.CreateClient();
        var sesion = await LeerAsync(await cliente.PostAsJsonAsync(
            new Uri($"/invitaciones/{token}", UriKind.Relative),
            new { nombre = "Vicent Llopis", contrasena = "Vinaros2026" }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sesion.GetProperty("token").GetString());
        return cliente;
    }

    [Fact]
    public async Task Solo_lectura_no_escribe_nada()
    {
        // El papel Solo lectura era inalcanzable hasta el módulo 14, así que su mitad del reparto de
        // permisos —`PermisosDeRol`— se probaba en unitarios y no se ejercía nunca de verdad. Esto es
        // lo que sostiene que esconder botones en la pantalla sea solo cortesía: el servidor dice no.
        var duena = await EnEmpresaAsync();
        var lectura = await ComoAsync(duena, rol: 3);

        var contacto = (await LeerAsync(await duena.PostAsJsonAsync(
            "/contactos", new { nombre = "Rocío Ferrán", email = "rocio@ribera.example" }))).GetProperty("id").GetGuid();

        (await lectura.PostAsJsonAsync("/contactos", new { nombre = "Otro", email = "otro@ribera.example" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await lectura.PostAsJsonAsync($"/contactos/{contacto}/notas", new { cuerpo = "Una nota." }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await lectura.PostAsJsonAsync($"/contactos/{contacto}/llamada", new { resultado = 1 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await lectura.PostAsJsonAsync("/oportunidades", new { contactoId = contacto, titulo = "Nave 3", importe = 1000m }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await lectura.PostAsJsonAsync("/tareas", new { titulo = "Llamar", contactoId = contacto }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await lectura.PostAsJsonAsync($"/cumplimiento/contactos/{contacto}/consentimientos",
            new { finalidad = 1, @base = 2, canal = "feria", textoAceptado = "Acepto." }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Y sí lee, que es para lo que está: la lista de contactos y los informes.
        (await lectura.GetAsync(new Uri("/contactos", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await lectura.GetAsync(new Uri("/informes/embudo", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Exportar sí: es el papel de la gestoría que se lleva los datos y no toca nada.
        (await lectura.GetAsync(new Uri("/informes/embudo.csv", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Un_comercial_vende_pero_no_se_lleva_los_datos()
    {
        // `datos.exportar` no está en el papel de comercial, y es deliberado: quien se va de la empresa
        // no se lleva la base de clientes en un CSV. Los dos botones de «Descargar CSV» y el de
        // «Descargar sus datos» de la ficha están marcados con ese permiso en la interfaz.
        var duena = await EnEmpresaAsync();
        var comercial = await ComoAsync(duena, rol: 2);

        var contacto = (await LeerAsync(await comercial.PostAsJsonAsync(
            "/contactos", new { nombre = "Amparo Sanchis", email = "amparo@ribera.example" }))).GetProperty("id").GetGuid();

        (await comercial.GetAsync(new Uri("/informes/embudo.csv", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await comercial.GetAsync(new Uri("/informes/motivos-perdida.csv", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await comercial.GetAsync(new Uri($"/cumplimiento/contactos/{contacto}/exportar", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Vender sí, que es su papel.
        (await comercial.PostAsJsonAsync("/oportunidades", new { contactoId = contacto, titulo = "Nave 3", importe = 1000m }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await comercial.GetAsync(new Uri("/informes/embudo", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Darse_de_alta_cuenta_como_haber_entrado()
    {
        // La lista del equipo decía «no ha entrado nunca» junto al nombre de quien estaba mirando la
        // pantalla: el último acceso solo se apuntaba al pasar por el login, y registrarse devuelve la
        // sesión ya iniciada sin pasar por ahí. Se vio en una captura, no en un test.
        var duena = await EnEmpresaAsync();

        var equipo = await LeerAsync(await duena.GetAsync(new Uri("/equipo", UriKind.Relative)));

        equipo.GetProperty("miembros")[0].GetProperty("ultimoAccesoEn").ValueKind
            .Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Sin_token_el_equipo_no_se_ve()
    {
        (await api.CreateClient().GetAsync(new Uri("/equipo", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
