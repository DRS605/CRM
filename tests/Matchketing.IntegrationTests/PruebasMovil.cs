using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        // Y nada se pide a ciegas: cada pestaña de Ajustes carga lo suyo y solo si hay permiso.
        html.Should().Contain("var CARGAS_AJUSTES = {");
        html.Should().Contain("if (puede('formulario.gestionar')) { cargarFormularios(); }");
        html.Should().Contain("if (puede('empresa.ajustes')) { cargarWebhooks(); }");

        // Al abrir Ajustes se enseña una pestaña, no las siete: antes eran cinco peticiones de golpe
        // para pintar paneles que estaban a dos pantallas de scroll.
        var abrir = html[html.IndexOf("function abrirAjustes()", StringComparison.Ordinal)..];
        abrir = abrir[..abrir.IndexOf("\n  }", StringComparison.Ordinal)];
        abrir.Should().Contain("grupoAjustes('empresa')");
        abrir.Should().NotContain("cargarWebhooks()");
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

    [Fact]
    public async Task El_menu_tiene_una_entrada_por_seccion_y_en_el_movil_caben()
    {
        // Eran seis entradas y Ajustes se había convertido en un cajón de sastre con **catorce**
        // paneles apilados: Cuentas y Tareas no existían como pantalla aunque su API estuviera
        // completa, y el equipo se administraba desde un ajuste.
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        foreach (var vista in new[] { "hoy", "repaso", "contactos", "cuentas", "embudo", "tareas", "informes", "equipo", "ajustes" })
        {
            html.Should().Contain($"data-vista=\"{vista}\"", $"«{vista}» tiene que estar en el menú");
            html.Should().Contain($"id=\"vista-{vista}\"", $"«{vista}» tiene que tener su sección");
        }

        // Las vistas se derivan del propio menú: dos listas separadas se desincronizan a la primera.
        html.Should().Contain("var VISTAS = Array.prototype.map.call(");
        html.Should().NotContain("['hoy', 'repaso', 'contactos', 'embudo', 'informes', 'ajustes']");

        // Nueve entradas no caben en una barra de pulgar: cuatro y «Más».
        html.Should().Contain("data-secundario");
        html.Should().Contain(".item[data-secundario] { display: none; }");
        html.Should().Contain("id=\"hoja-mas\"");

        // Y la hoja no es una segunda navegación: pulsa el elemento de menú de verdad.
        html.Should().Contain("b.addEventListener('click', function () { item.click(); });");
    }

    [Fact]
    public async Task Ajustes_deja_de_ser_una_sola_pagina_de_catorce_paneles()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        html.Should().Contain("id=\"aj-pestanas\"");
        foreach (var grupo in new[] { "empresa", "captacion", "automatizacion", "correo", "integraciones", "datos", "cuenta" })
        {
            html.Should().Contain($"class=\"grupo-ajustes\" data-grupo=\"{grupo}\"");
        }

        // El equipo ya no vive en Ajustes: quién entra en la empresa no es un ajuste, es gente.
        //
        // Se recorta desde el principio de la sección de Ajustes hasta el final del documento, que es
        // donde acaba: Ajustes es la última. Cuidado con recortar «hasta la siguiente sección» buscando
        // `id="vista-` sin saltar la primera coincidencia: eso devuelve una cadena vacía y la
        // comprobación pasa sin comprobar nada.
        var desde = html.IndexOf("id=\"vista-ajustes\"", StringComparison.Ordinal);
        desde.Should().BeGreaterThan(-1);
        var ajustes = html[desde..];
        ajustes.Should().Contain("grupo-ajustes", "se ha recortado el trozo correcto");
        ajustes.Should().NotContain("id=\"panel-equipo\"");

        // Y sí vive en su propia vista, antes de Ajustes.
        html.IndexOf("id=\"panel-equipo\"", StringComparison.Ordinal).Should().BeLessThan(desde);
    }

    /// <summary>
    /// El trozo de la página entre dos marcas, para afirmar sobre una función concreta del guion sin
    /// que la afirmación se dé por buena porque la cadena aparece en cualquier otro sitio del fichero.
    /// Las dos marcas tienen que existir y en ese orden, o la prueba falla en vez de mirar un trozo
    /// vacío —que es la forma silenciosa de que una prueba deje de comprobar nada—.
    /// </summary>
    private static string Entre(string html, string desdeMarca, string hastaMarca)
    {
        var desde = html.IndexOf(desdeMarca, StringComparison.Ordinal);
        desde.Should().BeGreaterThan(-1, $"la marca «{desdeMarca}» tiene que existir");
        var hasta = html.IndexOf(hastaMarca, desde + desdeMarca.Length, StringComparison.Ordinal);
        hasta.Should().BeGreaterThan(desde, $"«{hastaMarca}» tiene que venir después de «{desdeMarca}»");
        return html[desde..hasta];
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
    public async Task Las_letras_son_del_propio_servidor_y_no_de_un_tercero()
    {
        var cliente = api.CreateClient();
        var html = await cliente.GetStringAsync(new Uri("/", UriKind.Relative));

        // La razón no es el rendimiento, es la privacidad. Un `<link>` a Google Fonts hace que el
        // navegador de cada comercial pida el fichero a un servidor de Google, y eso le manda su IP
        // y la página que está mirando. En una herramienta que se vende diciendo «tus datos son
        // tuyos», eso es una contradicción, y encima invisible: nadie mira de dónde salen las letras.
        html.Should().NotContain("fonts.googleapis.com");
        html.Should().NotContain("fonts.gstatic.com");
        html.Should().NotContain("use.typekit");

        // Y no vale con no enlazarlas: hay que tenerlas. Cada `url()` del estilo tiene que existir y
        // servirse de verdad, o la página se cae a la letra del sistema sin decir nada.
        var pedidas = Regex.Matches(html, @"url\('(/tipos/[^']+)'\)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        pedidas.Should().NotBeEmpty("el estilo declara las letras con @font-face y url('/tipos/…')");

        foreach (var ruta in pedidas)
        {
            var r = await cliente.GetAsync(new Uri(ruta, UriKind.Relative));
            r.StatusCode.Should().Be(HttpStatusCode.OK, ruta + " está declarada en el estilo pero no se sirve");
            r.Content.Headers.ContentType!.MediaType.Should().Be("font/woff2",
                ruta + " con otro tipo puede no cargar y además no se comprime bien");
        }
    }

    [Fact]
    public async Task Las_letras_se_guardan_para_cuando_no_haya_cobertura()
    {
        var js = await api.CreateClient().GetStringAsync(new Uri("/sw.js", UriKind.Relative));

        // Las letras son armazón, igual que los iconos: sin ellas la aplicación abre, pero abre con
        // otra cara. Y por la lista blanca, lo que no se añade a mano **no se guarda** —falla cerrado,
        // que es lo que se quiere, pero hay que acordarse—. Esta prueba es ese recordatorio.
        js.Should().Contain("/tipos/", "sin esto las letras se piden a la red en cada arranque sin cobertura");

        // Y cuando no hay red ni copia, a un fichero que no es una navegación no se le puede dar el
        // HTML de la raíz: el navegador intentaría leer index.html como si fuera un woff2.
        js.Should().Contain("peticion.mode === 'navigate'",
            "la raíz solo vale como respuesta de emergencia para una navegación");
    }

    [Fact]
    public async Task El_color_de_una_etapa_sale_de_su_probabilidad_y_es_el_mismo_en_las_dos_pantallas()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // La regla de la casa es que si algo se pinta, tiene que estar diciendo un dato. El color de
        // una etapa lo dice su probabilidad, no su posición: así dos empresas con embudos de distinto
        // tamaño pintan igual lo que vale igual, y una etapa que se reconfigura cambia de color.
        html.Should().Contain("function banda(probabilidad)");
        html.Should().Contain("function colorAvance(probabilidad)");

        // Y hay **una sola** función que lo decide. Si el tablero y los informes se lo calculasen
        // cada uno por su cuenta, el día que se toque una escala una de las dos pantallas mentiría.
        Regex.Matches(html, @"function banda\(").Count.Should().Be(1);

        // El tablero del embudo y las barras de Informes tienen que usarla los dos.
        var tablero = Entre(html, "var tab = $('emb-tablero');", "cargarEmbudo");
        var informes = Entre(html, "var cont = $('inf-escalones');", "inf-ganado");
        // Dos usos en cada pantalla, y hay que exigir los dos por separado: el punto que va junto al
        // nombre de la etapa y el relleno de la barra. Con una sola afirmación, quitarle el color a la
        // barra pasaba desapercibido porque el punto ya cumplía la condición.
        tablero.Should().Contain("punto.style.background = colorAvance(col.probabilidad)");
        tablero.Should().Contain("var color = colorAvance(col.probabilidad)");
        informes.Should().Contain("punto.style.background = colorAvance(e.probabilidad)");
        informes.Should().Contain("var colorEtapa = colorAvance(e.probabilidad)");

        // Un valor que no se sabe no tiene color: devuelve cadena vacía, no la banda más baja, que
        // sería pintar «poco probable» donde en realidad dice «no se sabe».
        html.Should().Contain("if (probabilidad === null || probabilidad === undefined) { return ''; }");
    }

    [Fact]
    public async Task Una_etapa_a_cero_no_pinta_nada()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // El suelo del 2 % está para que un importe pequeño se siga viendo. Pero la barra llevaba
        // además `min-width: 2px` en la hoja de estilo, y eso **gana** a un `width: 0`: una etapa sin
        // nada abierto seguía dejando un tope de color. Un color donde no hay dato es justo lo que
        // esta interfaz dice que no hace.
        var barra = Regla(html, ".escalon .pista-e i");
        barra.Should().NotContain("min-width",
            "con min-width la etapa vacía pinta un tope de color y miente");

        html.Should().Contain("Math.max(2, Math.round(e.importeAbierto / maximo * 100))",
            "el suelo del 2 % lo pone el guion, y solo cuando hay algo que enseñar");
    }

    [Fact]
    public async Task Cada_color_de_la_pantalla_dice_algo_distinto()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // Baja y perdido compartían el gris. No son lo mismo: perdido es una venta que no salió y se
        // puede reintentar; baja es que retiró el consentimiento y no se le puede escribir. Igualar
        // los dos en un gris apagado es la clase de detalle con la que se manda un correo ilegal.
        Regla(html, ".e-baja").Should().Contain("--rojo");
        Regla(html, ".e-perdido").Should().Contain("--grafito");

        // Lo que ya va tarde se pinta en rojo, y es el único sitio donde el rojo significa urgencia.
        Regla(html, ".t-vencida").Should().Contain("--rojo");

        // La tarjeta de Hoy lleva en el canto el color de su motivo, y la etiqueta y el canto salen
        // de la misma decisión, así que no pueden discrepar.
        html.Should().Contain("function motivoTarjeta(t)");
        html.Should().Contain("'tarjeta-hoy por-' + motivoTarjeta(t).clase");
        html.Should().Contain(".tarjeta-hoy.por-t-vencida");

        // Y en la cronología el punto dice quién se movió, que es lo que no está escrito en ninguna
        // parte: una ficha entera en turquesa es un contacto caliente; toda en ciruela, alguien a
        // quien persigues sin respuesta.
        html.Should().Contain("function quienSeMovio(tipo)");
        html.Should().Contain("return MOVIO[tipo] || 'sistema'",
            "un tipo que no se reconoce no se le atribuye a nadie");
        html.Should().Contain(".hito.ellos .punto-hito");
    }

    [Fact]
    public async Task La_pantalla_de_campanias_no_deja_elegir_a_quien_esta_de_baja()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // El desplegable de estado tiene tres opciones y ninguna es «baja». No es una comprobación de
        // tiempo de ejecución: el valor no existe en el enumerado del dominio, así que no hay forma de
        // escribir el filtro. Si algún día apareciera aquí, lo único que impediría el envío sería la
        // comprobación del final, y una sola barrera para esto es una barrera de menos.
        var estado = Entre(html, "id=\"cp-seg-estado\"", "</select>");
        estado.Should().Contain("Lead").And.Contain("Cliente").And.Contain("Perdido");
        estado.Should().NotContain("Baja");
        estado.Should().NotContain("baja</option>");
    }

    [Fact]
    public async Task Lanzar_una_campania_dice_a_cuanta_gente_antes_de_hacerlo()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // La confirmación **dice el número**, no «¿seguro?». Un «¿seguro?» se contesta sin leerlo; «se le
        // va a escribir a 412 personas» se lee, y a veces se cancela, que es justo para lo que está. Y
        // antes de preguntar se vuelve a resolver el segmento, porque el número de la lista puede llevar
        // ahí un rato.
        var lanzar = Entre(html, "async function lanzar(c)", "function botonMini");
        lanzar.Should().Contain("api('/segmentos/' + c.segmentoId + '/previa')",
            "el número de la confirmación se pide en ese momento, no se reutiliza el de la lista");
        lanzar.Should().Contain("window.confirm");
        lanzar.Should().Contain("Se va a escribir a ' + cuantos");
        lanzar.Should().Contain("no se puede recoger", "hay que decir que es irreversible");
    }

    [Fact]
    public async Task La_ficha_de_una_campania_ensena_a_cuantos_no_llego()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // Los dos números juntos, en la misma fila de tarjetas: a cuántos se llegó y a cuántos no. Es lo
        // que ninguna plataforma de envío pone junto, y quitarlo convertiría esta pantalla en otra
        // pantalla de entregas más.
        var fila = Entre(html, "async function filaCampania(c)", "async function lanzar(c)");
        fila.Should().Contain("tarjetaKpi('Se les mandó'");
        fila.Should().Contain("tarjetaKpi('Se quedaron fuera'");
        fila.Should().Contain("Por qué no les llegó");
        fila.Should().Contain("detalle.porQueNoLlego");
    }

    [Fact]
    public async Task El_desplegable_de_campanias_solo_ofrece_plantillas_comerciales()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // La API rechaza igual una plantilla de atender solicitudes, pero enseñar en el desplegable algo
        // que va a dar error es hacerle perder el tiempo a quien lo elija. Y si no hay ninguna, el texto
        // de debajo dice dónde se crean en vez de dejar un desplegable vacío sin explicación.
        var carga = Entre(html, "async function cargarCampanias()", "function pintarSegmentos");
        carga.Should().Contain("p.paraQue === 'comercial'");
        html.Should().Contain("No hay ninguna plantilla comercial");
    }

    [Fact]
    public async Task La_linea_del_mes_dice_cuanto_al_dia_y_no_solo_el_porcentaje()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // `PorDiaQueQueda` es lo único del módulo de objetivos que no se puede sacar de ninguna otra
        // pantalla, y es lo que cambia lo que alguien hace esta tarde. Un 39 % no le dice a nadie si
        // tiene que darse prisa; «1.840 € al día» sí. Si esto se cae, la línea deja de servir para nada.
        var mes = Entre(html, "async function pintarMes()", "async function cargarHoy()");
        mes.Should().Contain("a.porDiaQueQueda");
        mes.Should().Contain("' al día'");
        mes.Should().Contain("Objetivo cumplido", "pasarse del objetivo se dice, no se esconde");

        // A cero no se pinta nada, igual que una etapa vacía del embudo: un tope de color donde no hay
        // dato es un color sin motivo.
        mes.Should().Contain("a.porcentaje <= 0 ? '0'");
    }

    [Fact]
    public async Task Sin_objetivo_la_pantalla_no_ensena_la_linea_del_mes()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // Ni la línea ni lo ganado: el número solo dice algo al lado del compromiso. Y si la consulta
        // falla, la pila de acciones se pinta igual — el objetivo es contexto, no la pantalla.
        var mes = Entre(html, "async function pintarMes()", "async function cargarHoy()");
        mes.Should().Contain("if (!mio || !mio.avance)");
        mes.Should().Contain("caja.hidden = true");

        Entre(html, "async function cargarHoy()", "hoy-vacio")
            .Should().Contain("pintarMes();")
            .And.NotContain("await pintarMes()", "la pila no espera al objetivo para pintarse");
    }

    [Fact]
    public async Task Vaciar_la_casilla_quita_el_objetivo_y_no_lo_pone_a_cero()
    {
        var html = await api.CreateClient().GetStringAsync(new Uri("/", UriKind.Relative));

        // Un cero dejaría un 0 % permanente en la pantalla de esa persona. Quitarlo hace desaparecer la
        // línea, que es lo que significa «esta persona no tiene objetivo este mes».
        var tabla = Entre(html, "async function pintarObjetivos()", "// ---------- Campañas ----------");
        tabla.Should().Contain("valor === '' || Number(valor) === 0");
        tabla.Should().Contain("metodo: 'DELETE'");

        // Y la tabla vive en Equipo, no en Ajustes: un objetivo es de una persona.
        html.IndexOf("id=\"panel-objetivos\"", StringComparison.Ordinal)
            .Should().BeLessThan(html.IndexOf("id=\"vista-ajustes\"", StringComparison.Ordinal),
                "los objetivos están en Equipo, que es la pantalla de las personas");
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
