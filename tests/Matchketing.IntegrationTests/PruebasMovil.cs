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
        js.Should().Contain("esArmazon");
    }

    [Fact]
    public async Task El_trabajador_decide_por_lista_blanca_y_no_por_lista_negra()
    {
        var js = await api.CreateClient().GetStringAsync(new Uri("/sw.js", UriKind.Relative));

        // Esto ya falló una vez y por eso está aquí. La regla era una lista **negra** con los prefijos
        // de la API, y todo lo que no estuviera en ella se guardaba en caché. Al añadir el módulo de
        // webhooks su ruta no estaba, así que el trabajador servía `/webhooks` desde la caché: se creaba
        // uno y el listado seguía devolviendo el de antes hasta recargar la página.
        //
        // Una lista negra falla abierto: se rompe sola cada vez que alguien añade un módulo, y sin
        // avisar. La lista blanca falla cerrado: una ruta nueva va a la red, que es lo correcto por
        // defecto, y lo que hay que acordarse de añadir es un fichero estático —que se nota al momento
        // porque deja de guardarse—.
        js.Should().Contain("esArmazon", "la decisión se toma sobre lo que SÍ es armazón");
        js.Should().Contain("if (!esArmazon)");

        // Y ni rastro de la lista negra de antes.
        js.Should().NotContain("esDato");
        js.Should().NotContain("|contactos|");
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
        js.Should().Contain("esArmazon");
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
    public async Task El_pie_dice_cuantos_modulos_hay_de_verdad()
    {
        // El pie de Ajustes decía «Los ocho módulos terminados» con trece terminados. Estuvo mal cinco
        // módulos seguidos, y es un número que el usuario lee como una promesa sobre el producto.
        //
        // La única defensa contra un número escrito a mano es atarlo a otro sitio donde sí se
        // actualiza: la tabla de estado del README, que es lo que se revisa al cerrar cada módulo.
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        var filas = System.Text.RegularExpressions.Regex.Matches(
            LeerDelRepositorio("README.md"), @"^\| (\d+)\. ", System.Text.RegularExpressions.RegexOptions.Multiline);
        filas.Should().NotBeEmpty("la tabla de estado del README es la fuente del número");
        var cuantos = filas.Count;

        var pie = System.Text.RegularExpressions.Regex.Match(html, @"<footer class=""nota"">.*?</footer>");
        pie.Success.Should().BeTrue();
        pie.Value.Should().Contain(
            $"{cuantos} módulos terminados",
            $"el README declara {cuantos} módulos y el pie tiene que decir lo mismo");
    }

    [Fact]
    public async Task Ningun_nombre_tecnico_ni_direccion_se_ensena_en_versalitas()
    {
        // `.campo label` pone en versalitas todas las etiquetas de campo, y está bien. El problema es
        // lo que hereda sin querer: las casillas de eventos del webhook viven dentro de un `.campo`,
        // así que `lead.creado` se leía **LEAD.CREADO**, y ese es el texto que hay que teclear tal cual
        // en el ERP del otro lado. Lo mismo con la dirección de la vista previa del correo, que salía
        // en mayúsculas: una dirección en mayúsculas es otra dirección para quien la lee.
        //
        // Las dos son la misma clase de fallo —un estilo de etiqueta encima de un dato— y las dos
        // pasan cualquier revisión de código, porque el HTML es correcto y la hoja de estilos también.
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        var casillas = Regla(html, ".casillas label");
        casillas.Should().Contain("text-transform: none");
        casillas.Should().Contain("letter-spacing: 0");

        // Y la dirección va en su propio hueco, con el estilo de etiqueta solo en la palabra «Para».
        html.Should().Contain(@"<span class=""direccion"" id=""cr-para"">");
        html.Should().NotContain(
            @"<span class=""rp-porque"" id=""cr-para"">",
            "`.rp-porque` es versalitas: ahí no cabe una dirección de correo");
        html.Should().Contain("$('cr-para').textContent = borrador.para;");
    }

    [Fact]
    public async Task La_pantalla_deja_rellenar_y_corregir_los_datos_de_la_empresa()
    {
        // El NIF se **enseñaba** en Ajustes y no había ni un sitio donde escribirlo: ni en el alta ni
        // después. Un campo que solo se puede mirar no es un campo, es una promesa incumplida.
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        html.Should().Contain("id=\"emp-nif\"", "el alta de la empresa pregunta el NIF");
        html.Should().Contain("<input id=\"aj-nif\"", "y en Ajustes se corrige, no solo se lee");
        html.Should().Contain("<input id=\"aj-nombre\"");
        html.Should().Contain("/empresas/activa', {");

        // Y quien no administra la empresa no ve un formulario que le va a contestar 403.
        html.Should().Contain("var soloLectura = !puede('empresa.ajustes');");
    }

    [Fact]
    public async Task La_medicion_de_aperturas_tiene_interruptor_y_explica_lo_que_hace()
    {
        // Todo el seguimiento de aperturas —el píxel, el recuento, la séptima pregunta del repaso—
        // dependía de `Empresa.SigueAperturas`, que nacía apagado y **no tenía interruptor**: ni
        // endpoint ni pantalla. La documentación decía «una decisión explícita de la empresa» y no
        // había forma de tomarla, así que era código inalcanzable con una frase encima.
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        html.Should().Contain("id=\"aj-aperturas\"");
        html.Should().Contain("/empresas/activa/ajustes-correo");

        // Y lo dice en castellano y por delante: encenderlo es medir a una persona.
        html.Should().Contain("medir el comportamiento");
        html.Should().Contain("solo en texto plano");
    }

    [Fact]
    public async Task Ajustes_no_le_ensena_a_un_comercial_paneles_que_le_van_a_dar_403()
    {
        // Mientras una empresa solo pudo tener a su propietaria, esto no hacía falta. Con el primer
        // comercial dentro, abrir Ajustes lanzaba cinco peticiones que el servidor contestaba con 403 y
        // una de ellas ni se recogía: la pantalla se quedaba a medias.
        //
        // Esconderlos no es la seguridad —esa la hace el servidor, permiso a permiso— sino no enseñar
        // cinco paneles que solo pueden dar error.
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        html.Should().Contain("var PANELES_AJUSTES = [");
        html.Should().Contain("function abrirAjustes()");
        html.Should().Contain("if (puede('formulario.gestionar')) { cargarFormularios(); }");

        // Y ninguna carga de las que piden permiso se lanza a ciegas al abrir la pestaña.
        var conmutador = html[html.IndexOf("b.dataset.vista === 'ajustes'", StringComparison.Ordinal)..];
        conmutador = conmutador[..conmutador.IndexOf('}', StringComparison.Ordinal)];
        conmutador.Should().Contain("abrirAjustes()");
        conmutador.Should().NotContain("cargarWebhooks()");
    }

    [Fact]
    public async Task La_pantalla_de_invitacion_dice_a_donde_te_invitan_antes_de_pedirte_nada()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        html.Should().Contain("id=\"pantalla-invitacion\"");
        html.Should().Contain("id=\"inv-empresa\"");
        html.Should().Contain("id=\"inv-rol\"");

        // La contraseña la elige quien entra, y se dice por delante: es lo que sostiene que la auditoría
        // pueda afirmar quién hizo qué.
        html.Should().Contain("quien te ha invitado no la ve");

        // Y el enlace de la invitación se ve una sola vez, como el secreto de un webhook.
        html.Should().Contain("id=\"eq-enlace-caja\"");
        html.Should().Contain("No se puede volver a ver");
    }

    [Fact]
    public async Task La_pantalla_no_ofrece_lo_que_el_permiso_no_deja_hacer()
    {
        // Toda la interfaz se escribió cuando la única persona posible en una empresa era su
        // propietaria, con los once permisos. Con tres papeles de verdad, un botón que contesta 403 al
        // pulsarlo es peor que no estar: promete algo que no va a pasar.
        //
        // El mecanismo es un atributo y **un solo sitio** que lo aplica. Un `if` por botón se olvida en
        // el siguiente botón.
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        html.Should().Contain("function aplicarPermisos(");
        html.Should().Contain("function vigilarPermisos(");
        html.Should().Contain("new MutationObserver(", "las listas se pintan cuando llegan los datos, no al entrar");

        // Solo esconde, nunca enseña: los permisos van firmados en el token y no cambian en la sesión.
        html.Should().Contain("if (!puede(n.dataset.permiso)) { n.classList.add('sin-permiso'); }");

        // Y esconde con **clase propia**, no con `hidden`. El primer intento usaba `hidden` y duró
        // hasta la primera ficha: `pintarPrivacidad` hace `$('pv-alta').hidden = deBaja` —false para un
        // contacto normal— y volvía a enseñar el formulario que se acababa de esconder. Dos mecanismos
        // para lo mismo se pisan; con una clase y un `!important` conviven.
        html.Should().Contain(".sin-permiso { display: none !important; }");
        html.Should().NotContain("if (!puede(n.dataset.permiso)) { n.hidden = true; }");

        // Los tres que más importan, cada uno con su permiso de verdad.
        html.Should().Contain("id=\"pv-exportar\" data-permiso=\"datos.exportar\"");
        html.Should().Contain("id=\"pv-borrar\" data-permiso=\"empresa.ajustes\"");
        html.Should().Contain("id=\"inf-csv-embudo\" data-permiso=\"datos.exportar\"");

        // El repaso es una cola de decisiones: quien no puede contestarlas no lo ve ni en el menú.
        html.Should().Contain("data-vista=\"repaso\" data-permiso=\"tarea.gestionar\"");
    }

    /// <summary>El cuerpo de una regla CSS de la hoja incrustada, para poder afirmar sobre ella.</summary>
    private static string Regla(string html, string selector)
    {
        var desde = html.IndexOf(selector + " {", StringComparison.Ordinal);
        desde.Should().BeGreaterThan(-1, $"la regla «{selector}» tiene que existir");
        var hasta = html.IndexOf('}', desde);
        return html[desde..hasta];
    }

    /// <summary>
    /// Un fichero del repositorio, buscando la raíz hacia arriba desde donde corre la prueba. Hace
    /// falta para las pruebas que atan la interfaz a la documentación; si no se encuentra la raíz la
    /// prueba falla, que es mejor que darse por buena sin haber comprobado nada.
    /// </summary>
    private static string LeerDelRepositorio(string relativo)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Matchketing.sln")))
        {
            carpeta = carpeta.Parent;
        }

        carpeta.Should().NotBeNull("no se ha encontrado la raíz del repositorio desde " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(carpeta!.FullName, relativo));
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
