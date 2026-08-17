using System.Net;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Matchketing.IntegrationTests;

/// <summary>
/// Lo que hace falta para que esto sea una aplicación en un móvil y no una página web.
///
/// Son pruebas humildes —que los ficheros existen y se sirven bien— y aun así valen: el manifiesto y
/// el trabajador de servicio se rompen **en silencio**. Si el manifiesto se sirve con el tipo
/// equivocado o le falta un icono, el navegador simplemente no ofrece instalar la aplicación; nadie ve
/// un error, y el comercial nunca tiene el icono en su pantalla de inicio.
/// </summary>
[Collection(ColeccionApi.Nombre)]
public sealed class PruebasMovil(ApiDePrueba api)
{
    [Fact]
    public async Task El_manifiesto_se_sirve_con_su_tipo_y_permite_instalar()
    {
        var r = await api.CreateClient().GetAsync(new Uri("/manifiesto.webmanifest", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("application/manifest+json",
            "con otro tipo el navegador lo ignora y no ofrece instalar nada");

        var m = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

        // Los cuatro requisitos que Chrome exige para ofrecer la instalación.
        m.GetProperty("name").GetString().Should().Be("match.keting");
        m.GetProperty("start_url").GetString().Should().Be("/");
        m.GetProperty("display").GetString().Should().Be("standalone");

        var iconos = m.GetProperty("icons").EnumerateArray().ToList();
        iconos.Should().Contain(i => i.GetProperty("sizes").GetString() == "192x192");
        iconos.Should().Contain(i => i.GetProperty("sizes").GetString() == "512x512");

        // Y uno «maskable», o en Android el icono sale dentro de un círculo blanco recortado a medias.
        iconos.Should().Contain(i => i.GetProperty("purpose").GetString() == "maskable");
    }

    [Fact]
    public async Task Todos_los_iconos_del_manifiesto_existen()
    {
        // El fallo más tonto y más probable: cambiar un nombre de fichero y dejar el manifiesto
        // apuntando al viejo. El navegador no dice nada; simplemente no hay icono.
        var cliente = api.CreateClient();
        var manifiesto = JsonDocument.Parse(
            await cliente.GetStringAsync(new Uri("/manifiesto.webmanifest", UriKind.Relative))).RootElement;

        var rutas = manifiesto.GetProperty("icons").EnumerateArray()
            .Select(i => i.GetProperty("src").GetString()!)
            .Concat(manifiesto.GetProperty("shortcuts").EnumerateArray()
                .SelectMany(s => s.GetProperty("icons").EnumerateArray())
                .Select(i => i.GetProperty("src").GetString()!))
            .Distinct()
            .ToList();

        rutas.Should().NotBeEmpty();

        foreach (var ruta in rutas)
        {
            var r = await cliente.GetAsync(new Uri(ruta, UriKind.Relative));
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"el manifiesto declara {ruta}");
            r.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        }
    }

    [Fact]
    public async Task El_trabajador_de_servicio_se_sirve_desde_la_raiz()
    {
        // Desde la raíz y no desde una subcarpeta: el alcance de un trabajador de servicio es la
        // carpeta desde la que se sirve, así que uno en `/js/sw.js` no podría controlar `/`.
        var r = await api.CreateClient().GetAsync(new Uri("/sw.js", UriKind.Relative));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");

        var js = await r.Content.ReadAsStringAsync();

        // La regla del trabajador: guarda el armazón y **nunca** los datos. Una pila de repaso de hace
        // tres días es peor que ninguna, porque tomarías decisiones sobre cosas que ya cambiaron.
        js.Should().Contain("esDato").And.Contain("repaso");
    }

    [Fact]
    public async Task Los_atajos_del_icono_apuntan_a_rutas_que_existen()
    {
        var cliente = api.CreateClient();
        var manifiesto = JsonDocument.Parse(
            await cliente.GetStringAsync(new Uri("/manifiesto.webmanifest", UriKind.Relative))).RootElement;

        var atajos = manifiesto.GetProperty("shortcuts").EnumerateArray().ToList();
        atajos.Should().HaveCount(2);

        foreach (var atajo in atajos)
        {
            var url = atajo.GetProperty("url").GetString()!;
            url.Should().StartWith("/?ir=");

            // La aplicación es de una sola página: el atajo abre la raíz y el parámetro decide la vista.
            (await cliente.GetAsync(new Uri(url, UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task La_pagina_declara_lo_que_un_movil_necesita()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        html.Should().Contain("rel=\"manifest\"");
        html.Should().Contain("apple-touch-icon", "sin esto, en iOS el icono de la pantalla de inicio es una captura de la página");
        html.Should().Contain("viewport-fit=cover", "para que el fondo llegue bajo la barra del sistema");
        html.Should().Contain("theme-color");
    }
}
