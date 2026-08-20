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
    public async Task El_trabajador_de_servicio_sabe_recibir_un_aviso_push()
    {
        var js = await api.CreateClient().GetStringAsync(new Uri("/sw.js", UriKind.Relative));

        // Sin estos dos oyentes, el aviso del viernes llega al navegador y no pasa nada. Es la clase de
        // fallo que solo se descubre un viernes a las seis y una semana después.
        js.Should().Contain("addEventListener('push'");
        js.Should().Contain("addEventListener('notificationclick'");

        // Un push que no muestra nada puede costar el permiso: el navegador lo revoca y se pierden
        // todos los avisos futuros. Por eso hay un texto genérico para cuando el cuerpo no se entiende.
        js.Should().Contain("showNotification");
        js.Should().Contain("Tienes algo que revisar.");

        // La etiqueta hace que un aviso sustituya al anterior. Tres avisos del repaso apilados en la
        // bandeja son tres motivos para apagarlos.
        js.Should().Contain("tag: 'repaso'");
    }

    [Fact]
    public async Task Sin_cobertura_la_pagina_no_dice_que_estas_al_dia()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // Antes, cualquier fallo al pedir la pila enseñaba «Al día. No hay nada que decidir», que es
        // lo contrario de la verdad: no es que no haya nada, es que no se ha podido preguntar. Decirle
        // a alguien que está al día cuando no se sabe es la clase de mentira por la que no se vuelve a
        // abrir una herramienta.
        html.Should().Contain("rp-sinred", "sin red hace falta un estado propio, no el de «al día»");
        html.Should().Contain("Sin cobertura");

        // Y para poder distinguirlo hay que saber si el fallo fue de red o del servidor.
        html.Should().Contain("'SinRed'");
    }

    [Fact]
    public async Task La_cola_de_respuestas_es_de_una_persona_en_una_empresa()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // El mismo navegador lo pueden usar dos personas, y el mismo usuario puede estar en dos
        // empresas. Mandar las respuestas de una con la sesión de la otra sería mucho peor que
        // perderlas, así que la clave lleva las dos cosas dentro.
        html.Should().Contain("'mk-cola-repaso:'");
        html.Should().Contain("sesion.usuario.id + ':' + sesion.empresaId");
    }

    [Fact]
    public async Task El_aviso_de_una_respuesta_rechazada_no_vive_dentro_de_la_tarjeta()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        html.Should().Contain("rp-rechazadas");

        // Es el detalle que lo hace funcionar: el aviso normal del repaso (`rp-aviso`) vive **dentro**
        // de la tarjeta, así que un rechazo que llega estando en «sin cobertura» o en el resumen se
        // escribiría en un panel oculto y no se vería nunca. Y una respuesta que se creía dada y no se
        // aplicó es justo la que no se puede perder.
        var subVistas = html[html.IndexOf("function subVistaRepaso", StringComparison.Ordinal)..];
        subVistas = subVistas[..subVistas.IndexOf(']', StringComparison.Ordinal)];
        subVistas.Should().NotContain(
            "rp-rechazadas",
            "si entra en las subvistas, se esconde al cambiar de pantalla y el aviso se pierde");
    }

    [Fact]
    public async Task La_cola_no_recalcula_nada_por_su_cuenta()
    {
        var cliente = api.CreateClient();
        var js = await cliente.GetStringAsync(new Uri("/sw.js", UriKind.Relative));

        // La objeción de siempre a encolar respuestas es buena: si se contesta «Ganada» y se envía
        // mañana, durante un día el embudo miente. Se resuelve no recalculando nada aquí —el embudo,
        // Hoy y los informes siguen contando lo que dice el servidor— y enseñando la cola como lo que
        // es. Lo que sujeta la primera mitad es que el trabajador de servicio siga sin guardar datos.
        js.Should().Contain("esDato");
        js.Should().Contain("No guarda respuestas de la API");
        js.Should().Contain("nada se recalcula en el móvil");

        var html = await cliente.GetStringAsync(new Uri("/", UriKind.Relative));

        // Y la cola se ve siempre que tenga algo dentro. Una cola escondida es una cola en la que
        // nadie confía, con razón.
        html.Should().Contain("rp-cola-cuantas");
        html.Should().Contain("respuestas sin enviar");
    }

    [Fact]
    public async Task La_pagina_pone_plazo_al_alta_de_avisos()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // `pushManager.subscribe()` no siempre falla: cuando el navegador no alcanza el servicio de
        // push de su fabricante, reintenta por dentro y la promesa se queda colgada para siempre. Sin
        // plazo, el botón se queda gris y en pantalla no aparece nada.
        html.Should().Contain("conPlazo(", "un alta sin plazo deja el botón gris para siempre");
        html.Should().Contain("EsperaSuscripcion");
        html.Should().Contain("PlazoAgotado");

        // Y un rechazo sin `message` tampoco puede quedarse en silencio.
        html.Should().Contain("function motivoDe(");
        html.Should().NotContain(
            "catch(function (e) { return e.message; })",
            "un catch que solo propaga e.message no enseña nada cuando el rechazo viene sin texto");
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
