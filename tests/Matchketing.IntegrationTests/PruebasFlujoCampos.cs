using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// Los campos propios contra PostgreSQL de verdad. Lo que solo se puede comprobar aquí: que las
/// opciones sobreviven al viaje a un `text[]` y vuelven en el mismo orden, que dos empresas no se ven
/// ni los campos ni los valores, que el índice único de la base sostiene lo que promete el servicio, y
/// que los valores de una persona **se van con ella** cuando ejerce el derecho de supresión.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasFlujoCampos(ApiDePrueba api)
{
    private static async Task<JsonElement> LeerAsync(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string CorreoNuevo() => $"c{Guid.NewGuid():N}@ribera.es";

    private async Task<HttpClient> EnEmpresaAsync(string nombre = "Instalaciones Ribera")
    {
        var cliente = api.CreateClient();
        var alta = await LeerAsync(await cliente.PostAsJsonAsync("/auth/registro", new
        {
            email = CorreoNuevo(),
            contrasena = "Levante2026",
            nombre = "Marta Ruiz",
        }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", alta.GetProperty("token").GetString());

        var empresa = await LeerAsync(await cliente.PostAsJsonAsync(
            "/empresas", new { nombre, provincia = "Valencia" }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", empresa.GetProperty("token").GetString());

        return cliente;
    }

    /// <summary>Un cliente ya dentro de esa empresa con el papel que se pida.</summary>
    private async Task<HttpClient> ComoAsync(HttpClient duena, int rol)
    {
        var invitacion = await LeerAsync(await duena.PostAsJsonAsync(
            "/equipo/invitaciones", new { email = CorreoNuevo(), rol }));

        var cliente = api.CreateClient();
        var sesion = await LeerAsync(await cliente.PostAsJsonAsync(
            new Uri($"/invitaciones/{invitacion.GetProperty("token").GetString()}", UriKind.Relative),
            new { nombre = "Vicent Llopis", contrasena = "Vinaros2026" }));
        cliente.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", sesion.GetProperty("token").GetString());

        return cliente;
    }

    private static async Task<Guid> CampoAsync(
        HttpClient cliente, string nombre, string tipo = "Texto",
        IReadOnlyList<string>? opciones = null, string ambito = "Contacto")
    {
        var r = await cliente.PostAsJsonAsync("/campos", new { ambito, nombre, tipo, opciones });
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> ContactoAsync(HttpClient cliente, string nombre = "Manolo García")
    {
        var r = await cliente.PostAsJsonAsync("/contactos", new { nombre, email = CorreoNuevo() });
        r.IsSuccessStatusCode.Should().BeTrue(await r.Content.ReadAsStringAsync());
        return (await LeerAsync(r)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> FijarAsync(
        HttpClient cliente, Guid campo, Guid entidad, string? valor) =>
        cliente.PutAsJsonAsync($"/campos/{campo}/valor/{entidad}", new { valor });

    [Fact]
    public async Task Un_campo_se_define_se_rellena_y_se_lee_en_la_ficha()
    {
        var duena = await EnEmpresaAsync();
        var poliza = await CampoAsync(duena, "Nº de póliza");
        var contacto = await ContactoAsync(duena);

        (await FijarAsync(duena, poliza, contacto, "AXA-4471")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var ficha = await LeerAsync(await duena.GetAsync(new Uri($"/campos/contacto/{contacto}", UriKind.Relative)));

        ficha.EnumerateArray().Should().HaveCount(1);
        var uno = ficha[0];
        uno.GetProperty("nombre").GetString().Should().Be("Nº de póliza");
        uno.GetProperty("clave").GetString().Should().Be("n_de_poliza");
        uno.GetProperty("valor").GetString().Should().Be("AXA-4471");
    }

    [Fact]
    public async Task Las_opciones_de_una_lista_vuelven_enteras_y_en_su_orden()
    {
        // Van a un `text[]` de PostgreSQL, y ese viaje es lo único que no se puede probar en unitarios.
        // Con una opción que lleva coma dentro: es exactamente lo que habría partido en dos una columna
        // de texto separada por comas, que es como guardan sus listas otros dos módulos.
        var duena = await EnEmpresaAsync();
        var tipo = await CampoAsync(
            duena, "Tipo de instalación", "Lista", ["Gas, de ciudad", "Eléctrica", "Aerotermia"]);

        var definicion = await LeerAsync(await duena.GetAsync(new Uri("/campos", UriKind.Relative)));
        var suyo = definicion.EnumerateArray().First(c => c.GetProperty("id").GetGuid() == tipo);

        suyo.GetProperty("opciones").EnumerateArray().Select(o => o.GetString())
            .Should().Equal("Gas, de ciudad", "Eléctrica", "Aerotermia");

        // Y el valor se guarda como está escrito en el campo, no como lo teclearon.
        var contacto = await ContactoAsync(duena);
        (await FijarAsync(duena, tipo, contacto, "  gas, DE ciudad ")).IsSuccessStatusCode.Should().BeTrue();

        var ficha = await LeerAsync(await duena.GetAsync(new Uri($"/campos/contacto/{contacto}", UriKind.Relative)));
        ficha[0].GetProperty("valor").GetString().Should().Be("Gas, de ciudad");
    }

    [Fact]
    public async Task Cambiar_las_opciones_de_una_lista_se_guarda()
    {
        // Con una lista y un comparador de valores por conversión, EF compara por referencia si nadie se
        // acuerda del `ValueComparer`: el `UPDATE` no se emite y el cambio se pierde en silencio al
        // recargar. Solo se ve yendo a la base y volviendo.
        var duena = await EnEmpresaAsync();
        var tipo = await CampoAsync(duena, "Tipo", "Lista", ["Gas", "Eléctrica"]);

        (await duena.PutAsJsonAsync($"/campos/{tipo}/opciones", new { opciones = new[] { "Gas", "Eléctrica", "Aerotermia" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var definicion = await LeerAsync(await duena.GetAsync(new Uri("/campos", UriKind.Relative)));
        definicion.EnumerateArray().First().GetProperty("opciones").EnumerateArray()
            .Select(o => o.GetString()).Should().Equal("Gas", "Eléctrica", "Aerotermia");
    }

    [Fact]
    public async Task No_se_quita_una_opcion_que_alguien_esta_usando()
    {
        var duena = await EnEmpresaAsync();
        var tipo = await CampoAsync(duena, "Tipo", "Lista", ["Gas", "Eléctrica"]);
        var contacto = await ContactoAsync(duena);
        (await FijarAsync(duena, tipo, contacto, "Gas")).IsSuccessStatusCode.Should().BeTrue();

        var r = await duena.PutAsJsonAsync($"/campos/{tipo}/opciones", new { opciones = new[] { "Eléctrica", "Aerotermia" } });

        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await r.Content.ReadAsStringAsync()).Should().Contain("Cámbiasela");
    }

    [Fact]
    public async Task La_clave_no_cambia_al_renombrar_y_la_repetida_no_entra()
    {
        var duena = await EnEmpresaAsync();
        var campo = await CampoAsync(duena, "Numero de poliza");

        (await duena.PutAsJsonAsync($"/campos/{campo}/nombre", new { nombre = "Nº de póliza" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var definicion = await LeerAsync(await duena.GetAsync(new Uri("/campos", UriKind.Relative)));
        var suyo = definicion.EnumerateArray().First();
        suyo.GetProperty("nombre").GetString().Should().Be("Nº de póliza");
        suyo.GetProperty("clave").GetString().Should().Be("numero_de_poliza");

        // Y ahora el nombre viejo, que da la misma clave, ya no cabe: lo para el servicio, y detrás está
        // el índice único de la base para el caso de dos peticiones a la vez.
        (await duena.PostAsJsonAsync("/campos", new { ambito = "Contacto", nombre = "Numero de poliza", tipo = "Texto" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Un_valor_solo_cuenta_una_vez_por_ficha()
    {
        // El índice único de `(campo_id, entidad_id)` es el que sostiene que la ficha enseñe una fila por
        // campo. Rellenar dos veces tiene que cambiar el valor, no añadir otro.
        var duena = await EnEmpresaAsync();
        var campo = await CampoAsync(duena, "Potencia", "Numero");
        var contacto = await ContactoAsync(duena);

        (await FijarAsync(duena, campo, contacto, "4,6")).IsSuccessStatusCode.Should().BeTrue();
        (await FijarAsync(duena, campo, contacto, "9,2")).IsSuccessStatusCode.Should().BeTrue();

        var ficha = await LeerAsync(await duena.GetAsync(new Uri($"/campos/contacto/{contacto}", UriKind.Relative)));
        ficha.EnumerateArray().Should().HaveCount(1);
        ficha[0].GetProperty("valor").GetString().Should().Be("9.2");
    }

    [Fact]
    public async Task Vaciar_una_casilla_borra_la_fila()
    {
        var duena = await EnEmpresaAsync();
        var campo = await CampoAsync(duena, "Nº de póliza");
        var contacto = await ContactoAsync(duena);
        (await FijarAsync(duena, campo, contacto, "AXA-1")).IsSuccessStatusCode.Should().BeTrue();

        (await FijarAsync(duena, campo, contacto, "")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var definicion = await LeerAsync(await duena.GetAsync(new Uri("/campos", UriKind.Relative)));
        definicion.EnumerateArray().First().GetProperty("rellenos").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Borrar_un_campo_se_lleva_sus_valores_y_dice_cuantos()
    {
        var duena = await EnEmpresaAsync();
        var campo = await CampoAsync(duena, "Nº de póliza");
        var uno = await ContactoAsync(duena);
        var otro = await ContactoAsync(duena, "Pepe Server");
        (await FijarAsync(duena, campo, uno, "AXA-1")).IsSuccessStatusCode.Should().BeTrue();
        (await FijarAsync(duena, campo, otro, "AXA-2")).IsSuccessStatusCode.Should().BeTrue();

        var r = await duena.DeleteAsync(new Uri($"/campos/{campo}", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LeerAsync(r)).GetProperty("valoresBorrados").GetInt32().Should().Be(2);
        (await LeerAsync(await duena.GetAsync(new Uri($"/campos/contacto/{uno}", UriKind.Relative))))
            .EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Un_campo_de_cuenta_se_rellena_en_la_cuenta_y_no_en_el_contacto()
    {
        var duena = await EnEmpresaAsync();
        var sector = await CampoAsync(duena, "Sector CNAE", ambito: "Cuenta");
        var cuenta = (await LeerAsync(await duena.PostAsJsonAsync(
            "/cuentas", new { nombre = "Casa Manolo", provincia = "Valencia" }))).GetProperty("id").GetGuid();
        var contacto = await ContactoAsync(duena);

        (await FijarAsync(duena, sector, cuenta, "4321")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FijarAsync(duena, sector, contacto, "4321")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await LeerAsync(await duena.GetAsync(new Uri($"/campos/cuenta/{cuenta}", UriKind.Relative))))[0]
            .GetProperty("valor").GetString().Should().Be("4321");

        // Y en la ficha del contacto no aparece: son dos ámbitos y dos pantallas.
        (await LeerAsync(await duena.GetAsync(new Uri($"/campos/contacto/{contacto}", UriKind.Relative))))
            .EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Dos_empresas_no_se_ven_ni_los_campos_ni_los_valores()
    {
        // La misma clave en las dos, a propósito: si el aislamiento fallara, el índice único de
        // `(empresa_id, ambito, clave)` sería lo primero en quejarse y se vería aquí.
        var ribera = await EnEmpresaAsync("Instalaciones Ribera");
        var vecina = await EnEmpresaAsync("Clima Vecina");

        var suyo = await CampoAsync(ribera, "Nº de póliza");
        await CampoAsync(vecina, "Nº de póliza");
        var contacto = await ContactoAsync(ribera);
        (await FijarAsync(ribera, suyo, contacto, "AXA-4471")).IsSuccessStatusCode.Should().BeTrue();

        (await LeerAsync(await vecina.GetAsync(new Uri("/campos", UriKind.Relative))))
            .EnumerateArray().Should().HaveCount(1, "cada una ve el suyo y nada más");

        // El campo de la otra no existe desde aquí: ni para leerlo, ni para rellenarlo, ni para borrarlo.
        (await vecina.PutAsJsonAsync($"/campos/{suyo}/nombre", new { nombre = "Otro" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await FijarAsync(vecina, suyo, contacto, "robado")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await vecina.DeleteAsync(new Uri($"/campos/{suyo}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Y el valor sigue donde estaba.
        (await LeerAsync(await ribera.GetAsync(new Uri($"/campos/contacto/{contacto}", UriKind.Relative))))[0]
            .GetProperty("valor").GetString().Should().Be("AXA-4471");
    }

    [Fact]
    public async Task Un_comercial_rellena_campos_pero_no_los_define()
    {
        // Es la línea que decide de quién es esta pantalla: definir un campo cambia la ficha de todos los
        // compañeros, y eso es configuración; rellenarlo es un dato de la ficha, como el teléfono.
        var duena = await EnEmpresaAsync();
        var campo = await CampoAsync(duena, "Nº de póliza");
        var contacto = await ContactoAsync(duena);
        var comercial = await ComoAsync(duena, rol: 2);

        (await FijarAsync(comercial, campo, contacto, "AXA-4471")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await comercial.GetAsync(new Uri($"/campos/contacto/{contacto}", UriKind.Relative)))
            .IsSuccessStatusCode.Should().BeTrue();

        (await comercial.PostAsJsonAsync("/campos", new { ambito = "Contacto", nombre = "Otro", tipo = "Texto" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await comercial.DeleteAsync(new Uri($"/campos/{campo}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await comercial.PutAsJsonAsync($"/campos/{campo}/nombre", new { nombre = "Otro" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Solo_lectura_ve_los_campos_y_no_los_rellena()
    {
        var duena = await EnEmpresaAsync();
        var campo = await CampoAsync(duena, "Nº de póliza");
        var contacto = await ContactoAsync(duena);
        var lectura = await ComoAsync(duena, rol: 3);

        (await lectura.GetAsync(new Uri($"/campos/contacto/{contacto}", UriKind.Relative)))
            .IsSuccessStatusCode.Should().BeTrue();
        (await FijarAsync(lectura, campo, contacto, "AXA-4471"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task El_orden_de_la_ficha_es_el_que_se_puso_en_ajustes()
    {
        var duena = await EnEmpresaAsync();
        var uno = await CampoAsync(duena, "Uno");
        var dos = await CampoAsync(duena, "Dos");
        var tres = await CampoAsync(duena, "Tres");
        var contacto = await ContactoAsync(duena);

        (await duena.PutAsJsonAsync("/campos/orden", new { ambito = "Contacto", orden = new[] { tres, uno, dos } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LeerAsync(await duena.GetAsync(new Uri($"/campos/contacto/{contacto}", UriKind.Relative))))
            .EnumerateArray().Select(c => c.GetProperty("nombre").GetString())
            .Should().Equal("Tres", "Uno", "Dos");
    }

    [Fact]
    public async Task Los_campos_propios_de_una_persona_salen_en_su_copia_de_datos()
    {
        // Son datos suyos aunque el nombre de la columna se lo haya inventado la empresa: «nº de póliza»
        // dice de él tanto como su teléfono.
        var duena = await EnEmpresaAsync();
        var campo = await CampoAsync(duena, "Nº de póliza");
        var contacto = await ContactoAsync(duena);
        (await FijarAsync(duena, campo, contacto, "AXA-4471")).IsSuccessStatusCode.Should().BeTrue();

        var copia = await LeerAsync(await duena.GetAsync(
            new Uri($"/cumplimiento/contactos/{contacto}/exportar", UriKind.Relative)));

        var propios = copia.GetProperty("camposPropios");
        propios.EnumerateArray().Should().HaveCount(1);
        propios[0].GetProperty("campo").GetString().Should().Be("Nº de póliza");
        propios[0].GetProperty("valor").GetString().Should().Be("AXA-4471");
    }

    [Fact]
    public async Task Borrar_a_una_persona_se_lleva_lo_que_habia_en_sus_campos_propios()
    {
        // **La prueba que este módulo tenía que traer escrita.** Un campo propio es donde una empresa
        // mete el dato que este CRM no tiene, así que ahí puede haber un DNI o una dirección. Dejarlo
        // detrás convertiría «borrar es borrar» en una frase falsa, otra vez.
        var duena = await EnEmpresaAsync();
        var campo = await CampoAsync(duena, "DNI del titular");
        var otroCampo = await CampoAsync(duena, "Potencia", "Numero");
        var contacto = await ContactoAsync(duena);
        var vecino = await ContactoAsync(duena, "Pepe Server");

        (await FijarAsync(duena, campo, contacto, "12345678Z")).IsSuccessStatusCode.Should().BeTrue();
        (await FijarAsync(duena, otroCampo, contacto, "4,6")).IsSuccessStatusCode.Should().BeTrue();
        (await FijarAsync(duena, campo, vecino, "87654321X")).IsSuccessStatusCode.Should().BeTrue();

        var borrado = await duena.DeleteAsync(new Uri($"/cumplimiento/contactos/{contacto}", UriKind.Relative));
        borrado.IsSuccessStatusCode.Should().BeTrue(await borrado.Content.ReadAsStringAsync());
        (await LeerAsync(borrado)).GetProperty("camposPropios").GetInt32().Should().Be(2);

        // Y lo del vecino sigue ahí: la supresión es de una persona, no de la tabla.
        var definicion = await LeerAsync(await duena.GetAsync(new Uri("/campos", UriKind.Relative)));
        definicion.EnumerateArray().First(c => c.GetProperty("id").GetGuid() == campo)
            .GetProperty("rellenos").GetInt32().Should().Be(1);
    }
}
