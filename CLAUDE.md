# Guía para agentes (CLAUDE.md)

**match.keting** — CRM independiente en **.NET 8 + PostgreSQL**. Trece módulos terminados.

Lee primero [`README.md`](README.md) (estructura y API) y, según lo que vayas a tocar,
[`docs/modulos/<modulo>.md`](docs/modulos/): cada uno explica **por qué** está hecho así, incluidas las
decisiones que parecen raras y los errores que se corrigieron a mitad. Antes de cambiar una regla,
busca si ya está documentada; casi siempre lo está, y casi siempre hay un test que la sujeta.

## Reglas del proyecto

- **Un módulo a la vez, terminado por completo**: dominio · API · persistencia · tests unitarios ·
  tests de integración · documentación. No se empieza el siguiente con el anterior a medias.
- Dominio **en español**: clases, tablas, columnas, endpoints, códigos de error. La interfaz también.
- **Clean Architecture ligera**: `Matchketing.<Modulo>` (dominio + aplicación) no referencia
  frameworks. La infraestructura vive en `Matchketing.Persistencia`.
- **Ningún módulo de negocio referencia a otro.** Las dos únicas excepciones son `Matchketing.Nucleo`
  y `Matchketing.Auditoria`, las dos dominio puro sin frameworks. Si necesitas datos de otro módulo,
  declara un puerto y que lo implemente la persistencia: `IAlmacenPersonal` y `IConsultaMatch` son los
  ejemplos a copiar.
- Fallos **esperados** con `Resultado`/`Error`, nunca con excepciones. Se lanza solo ante errores de
  programación (una constante vacía, un argumento nulo que no puede serlo).
- **Multiempresa obligatorio**: `empresa_id` en toda tabla de datos, filtro global de EF **y** RLS de
  PostgreSQL. Ningún endpoint acepta `empresa_id` por parámetro: sale del JWT y solo del JWT.
- Compilación estricta: `TreatWarningsAsErrors`. No relajes un analizador sin justificarlo por escrito
  en el mismo sitio donde lo relajas.
- **Ningún número sin motivo.** Si una cifra que se le muestra a una persona no se puede explicar en
  una frase, no se muestra. Vale para el Match y vale para todo lo demás.
- **Nunca pidas un dato que el sistema puede deducir**, y nunca preguntes dos veces lo mismo por dos
  caminos. Es la tesis del módulo [Repaso](docs/modulos/repaso.md) y la razón de que un comercial
  abra esto: en cuanto una pantalla pide escribir o repite una pregunta, se abandona.

## Compilar y probar

```bash
dotnet build
dotnet test                            # 823 pruebas; necesita PostgreSQL en localhost:5432
./scripts/comprobar-aislamiento.sh     # la RLS, que ningún test de C# puede comprobar
```

Los tests de integración usan la base **`matchketing_test`** y la borran y recrean al empezar.
Sobrescribe la cadena con `MATCHKETING_TEST_CONEXION` si hace falta.

> **Cuidado con la configuración.** No leas `constructor.Configuration[...]` en variables locales al
> principio de `Program.cs`: en ese momento aún no están las fuentes que añade quien hospeda la
> aplicación, y los tests de integración acabarían apuntando a la base de desarrollo. Ya pasó. Se lee
> siempre de forma diferida, dentro de las fábricas del contenedor, y hay un test que lo vigila
> (`La_api_de_pruebas_usa_la_base_de_pruebas`).

## Migraciones EF Core

```bash
dotnet tool restore
dotnet ef migrations add <Nombre> \
  --project src/Matchketing.Persistencia \
  --startup-project src/Matchketing.Api \
  --output-dir Migraciones
```

Después de generarla, **añade a mano** el SQL que EF no sabe generar y que este proyecto sí necesita:

```sql
ALTER TABLE <esquema>.<tabla> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <esquema>.<tabla> FORCE ROW LEVEL SECURITY;
CREATE POLICY aislamiento_empresa ON <esquema>.<tabla>
    USING (empresa_id::text = current_setting('app.empresa_actual', true))
    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
```

y el `DROP POLICY` correspondiente en el `Down`. Una tabla de datos sin RLS es un agujero silencioso:
todo seguirá funcionando y las pruebas seguirán pasando.

## Trampas conocidas

Cosas que ya han costado tiempo. Están aquí para que no lo vuelvan a costar:

- **`ValueGenerated.Never` en toda clave Guid.** Los identificadores los genera el dominio. Si EF cree
  que los genera la base, al descubrir una entidad hija nueva colgando de un padre ya rastreado emite
  `UPDATE` en vez de `INSERT` y falla con «expected to affect 1 row(s), but actually affected 0». Ya
  está resuelto en `OnModelCreating`; no lo toques.
- **`InvariantGlobalization` está activo**, así que `string.Normalize(FormD)` no hace nada. Para
  quitar acentos hay un mapa explícito en `LectorCsv`.
- **Los filtros van sobre la consulta, no sobre la proyección.** `bd.Contactos.Where(...).Select(...)`,
  nunca al revés: EF no sabe traducir un `WHERE` contra un registro ya proyectado.
- **`ExecuteDelete`/`ExecuteUpdate` se ejecutan al momento** y no esperan a `SaveChanges`. Si en la
  misma operación hay un apunte de auditoría, abre una transacción explícita o quedará el borrado sin
  su rastro.
- **Un disparador `BEFORE ... FOR EACH ROW` no salta si la orden afecta a cero filas.** Con RLS por
  medio esto engaña: parece que la regla no existe cuando lo que pasa es que la otra barrera actuó
  primero. Fija `app.empresa_actual` antes de comprobar nada.
- **Los tests de integración comparten una sola instancia de la API** (`ColeccionApi`). Si añades una
  clase nueva, márcala con `[Collection(ColeccionApi.Nombre)]` o borrará la base mientras otra la usa.
  Y filtra siempre por identificador al consultar: `FirstAsync()` sin `WHERE` recoge filas de otra
  prueba.
- **El servidor no formatea números con `:N0`.** Con `InvariantGlobalization` eso produce «8,400 €»,
  que en España es una cifra distinta, y la cultura `es-ES` no existe sin ICU. Usa
  `Castellano.Euros(...)`.
- **Nada de datos personales en `auditoria.registro`.** Hay una red que tapa correos y teléfonos, pero
  no metas texto libre escrito por usuarios: la tabla es append-only y de ahí no sale nada.
- **`pushManager.subscribe()` puede no fallar nunca.** Si el navegador no alcanza el servicio de push
  de su fabricante, reintenta por dentro y la promesa se queda colgada para siempre: el botón se
  queda gris y en pantalla no aparece nada. Todo lo que dependa de un servicio externo del navegador
  va con plazo. Y `e.message` puede venir vacío, así que un `catch` que solo lo propague no enseña
  nada: usa `motivoDe(...)`, que nunca devuelve vacío. Ver [`docs/modulos/avisos.md`](docs/modulos/avisos.md).
- **El trabajador de servicio sirve el armazón desde la caché**, así que un cambio en `index.html` no
  se ve en la primera recarga. Al probar en un navegador automatizado, usa un perfil limpio; si no,
  estarás mirando la versión anterior y buscando un fallo que ya arreglaste. Pasó.
- **`fetch` solo rechaza cuando no hay red.** Un 400 llega como respuesta normal. La cola del repaso
  depende de distinguirlos (`e.name === 'SinRed'`): sin eso reintentaría para siempre respuestas que el
  servidor ya rechazó. Ver [`docs/movil.md`](docs/movil.md).
- **`JwtBearer` reescribe las reclamaciones al validarlas.** Trae `MapInboundClaims` activado, así que
  la reclamación `email` que se firma llega como el URI largo de WS-Federation. Buscar por el nombre
  corto devuelve `null` y no falla: `/auth/yo` devolvió el correo vacío durante ocho módulos porque la
  interfaz no lo usaba. Si añades una reclamación estándar, busca por las dos formas.
- **Un color tampoco puede ir sin motivo.** El magenta significa «aquí está la acción». Usarlo para
  decir «esta es la última columna» (`:last-child`) hacía que la etapa vacía se llevara la mirada y la
  que tenía 71.800 € quedara apagada. Si algo se pinta, tiene que estar diciendo un dato. El corolario:
  el color de una etapa sale de su **probabilidad**, nunca de su índice —así dos empresas con embudos
  de distinto tamaño pintan igual lo que vale igual—, y lo decide una sola función (`banda`) que usan
  el tablero, los informes y el match. Ver [`docs/interfaz.md`](docs/interfaz.md).
- **`min-width` le gana a `width: 0`.** La barra de una etapa vacía seguía pintando un tope de color
  porque la hoja tenía `min-width: 2px` y el guion le ponía `width: 0`. El suelo para que un importe
  pequeño se vea lo pone el guion, y solo cuando hay algo.
- **Un embudo no puede ensanchar por el camino.** La conversión entre etapas se calcula sobre «cuántas
  **llegaron hasta aquí o más allá**», nunca sobre «cuántas estuvieron en esta etapa»: el tablero deja
  saltarse etapas, así que lo segundo daba etapas de más adelante con más oportunidades que las de
  antes y el informe enseñaba «↓ 200 % pasa a propuesta». Contando el punto más lejano alcanzado, la
  serie es decreciente por construcción. Ver [`docs/modulos/informes.md`](docs/modulos/informes.md).
- **Un módulo que guarde datos de una persona tiene que entrar en la supresión.** `AlmacenPersonal`,
  las dos ramas —contacto y empresa—. Se olvidó durante cinco módulos y la supresión del artículo 17
  dejaba en la base los correos enviados, con dirección y texto completo. Hay una prueba que recorre
  **todas** las columnas de la base buscando el identificador del contacto después de borrarlo, así que
  si te olvidas te enteras al momento; lo que no puede pasar es «arreglarla» quitando la tabla de la
  prueba. Ver [`docs/modulos/cumplimiento.md`](docs/modulos/cumplimiento.md).
- **Y esa prueba solo mira donde alguien haya escrito.** El barrido busca el identificador del contacto,
  así que si su preparación no guarda nada en tu tabla nueva, **pasa sin mirar nada**. Comprobado a
  propósito en el módulo 17: quitando el borrado de los campos propios, la prueba del módulo falló y el
  barrido pasó. Al cerrar un módulo que guarde datos de una persona hay que **dejar rastro en esa
  preparación** y añadir su tabla a la lista de `antes.Should().Contain(...)`.
- **Un ámbito, un tipo o un estado nuevo necesita una pantalla donde usarse.** No se añade el valor al
  enumerado «para después»: queda algo que se puede definir y no se puede rellenar. Por eso los campos
  propios no tienen ámbito de oportunidad (no hay ficha de oportunidad) y por eso el módulo 17 tuvo que
  construir la **ficha de cuenta**, que no existía: sin ella, el ámbito de cuenta habría sido ese error.
- **Antes de añadir `id="…"` al HTML, comprueba que no está usado.**
  `grep -o 'id="[a-z0-9-]*"' index.html | sed 's/id="//;s/"//' | sort | uniq -d` tiene que salir vacío.
  El prefijo `cp-` es de campañas, y los campos propios estrenaron seis identificadores repetidos que
  habrían hecho que `$('cp-lista')` devolviera el panel equivocado.
- **Un namespace nuevo puede tapar una clase con el mismo nombre.** Al aparecer `Matchketing.Campos`,
  `Campos.Todos` en un archivo de la API dejó de resolver a `Correo.Dominio.Campos` —los huecos de una
  plantilla— y el proyecto no compiló. Se arregla cualificando en ese archivo, no renombrando el módulo.
- **Una lista con conversión de valor necesita `ValueComparer`.** Sin él EF la compara por referencia,
  no detecta el cambio y **no emite el `UPDATE`**: se pierde en silencio y solo se ve recargando. Pasa
  en webhooks (los tipos de evento) y en campos propios (las opciones de una lista).
- **Hay un solo «hoy», y es el de España.** `HorasLaborables.DiaDeTrabajo(instante)`. Había nueve sitios
  contando el día en UTC y tres en hora local, y entre medianoche y las dos de la mañana en verano no
  eran el mismo día: una tarea que el sistema creaba «para hoy» no aparecía en Hoy, y el trabajo hecho a
  las 00:30 se contaba como de ayer. Todo lo que convierta un instante en fecha —o una fecha en rango de
  instantes, con `LimitesDelDia`— pasa por ahí. Y `current_date` de PostgreSQL **es UTC**: no lo uses
  para «hoy» ni en las pruebas.
- **Npgsql solo escribe `DateTimeOffset` con desfase 0.** «only offset 0 (UTC) is supported». Un límite
  de día calculado en hora local hay que pasarlo por `ToUniversalTime()` antes de que llegue a un
  `WHERE`; si no, la consulta revienta en ejecución y no al compilar.
- **`Results.Ok(null)` devuelve el cuerpo vacío**, que no es JSON válido. Para «esto no existe y es
  normal», `Results.NoContent()`.
- **Un envío desde un trabajo de fondo no tiene sesión.** `ServicioCorreo.DireccionAsync` leía el
  usuario del contexto, así que al encolar desde el trabajo de campañas devolvía nulo y el correo se
  rechazaba con «ese contacto no tiene una dirección válida». El contacto sí la tenía; faltaba la
  sesión. Quién firma un correo es un **parámetro explícito**, no estado ambiente.
- **Dos pruebas comprobaban el número de permisos con una cifra escrita a mano** y fallaron al añadir
  `campania.leer`. Un recuento derivado se compara contra su fuente (`Permisos.Todos`,
  `PermisosDeRol.De(...)`), nunca contra un número.
- **Las tipografías van en el repositorio, nunca en un CDN.** Un `<link>` a Google Fonts le manda a un
  tercero la IP de cada comercial que abre la aplicación, en una herramienta que se vende diciendo que
  los datos son tuyos. Y al meterlas en el armazón del trabajador de servicio hay que acordarse de que
  la raíz solo vale como respuesta de emergencia para una **navegación**: devolver `index.html` para un
  woff2 es peor que fallar.
- **El trabajador de servicio decide por lista blanca, no por lista negra.** Antes tenía una expresión
  con los prefijos de la API y guardaba en caché todo lo que no encajara. Al añadir `/webhooks`, su ruta
  no estaba en la lista y el trabajador **servía datos de la API desde la caché**: se creaba un webhook
  y el listado seguía devolviendo el de antes. Si añades ficheros estáticos, mételos en `esArmazon`; si
  añades una ruta de API, no tienes que hacer nada, y eso es el objetivo.
- **Dos funciones con el mismo nombre en `index.html` no dan ningún error.** Todo vive en un IIFE, así
  que la última declaración gana por el izado y la primera desaparece sin rastro. Pasó con `boton`, y se
  vio solo porque los botones salían sin su clase. Antes de añadir una función, comprueba que el nombre
  esté libre.
- **Cuando cambies un endpoint, reinicia la API antes de probar en el navegador.** Los estáticos se
  sirven de disco y se recargan solos; el código compilado no. Media hora perdida buscando un 404 que
  era un proceso viejo.
- **Un endpoint público que lea una tabla con RLS necesita saber la empresa antes de tocar la base.**
  `IgnoreQueryFilters()` no basta: salta el filtro de EF pero la política de PostgreSQL sigue devolviendo
  cero filas sin `app.empresa_actual`. El patrón es meter la empresa en el propio token —enlace de baja,
  píxel de apertura— y luego `contextoPublico.FijarEmpresa` + `bd.ReaplicarEmpresaAsync`. Así además la
  consulta va **con** las dos barreras puestas en vez de sin ninguna.
- **Un botón deshabilitado tiene que parecerlo.** Con solo bajar la opacidad, un `.btn.pri` seguía siendo
  magenta y se leía como «púlsame». En esta paleta el magenta significa «aquí está la acción», y no puede
  significar eso cuando la acción no se puede hacer.
- **`GuardarCambiosAsync` despacha en dos momentos, y no da igual cuál.** Los webhooks van **antes** de
  guardar, porque sus filas de entrega son escrituras sueltas y así entran en el mismo `SaveChanges`. Las
  reglas van **después**, porque sus acciones pasan por servicios que **cargan de la base** el contacto
  sobre el que actúan: antes de guardar, ese contacto todavía no existe y fallan en silencio. Si añades
  algo que consuma eventos, decide en qué lado va. Ver
  [`docs/modulos/automatizacion.md`](docs/modulos/automatizacion.md).
- **Un parámetro de constructor opcional en el `DbContext` no se resuelve: se rellena con su valor por
  defecto.** `IServiceProvider? servicios = null` llegaba nulo en producción y las automatizaciones no se
  ejecutaban nunca, sin ningún error. Los parámetros del contexto van obligatorios, y la factoría de
  diseño pasa lo que haga falta.
- **Una aserción que se cumple por casualidad es peor que no tenerla.** La prueba de «una regla no puede
  mandar un correo sin permiso» solo miraba que apareciera «no se pudo», y pasaba porque fallaban también
  las otras acciones por un motivo distinto. Si compruebas que algo falla, comprueba **qué** falla.
- **Un método público sin llamantes no da ningún error.** Compila, pasa los analizadores y aparenta que la
  funcionalidad existe. `Empresa.Actualizar` y `AjustarSeguimiento` llevaban trece y dos módulos sin
  endpoint ni pantalla: el NIF se enseñaba en Ajustes sin poder rellenarse nunca, y el seguimiento de
  aperturas —píxel, recuento y la séptima pregunta del repaso— era código inalcanzable con un párrafo de
  documentación encima que decía «es una decisión explícita de la empresa». Al terminar un módulo,
  comprueba que **todo lo que el dominio permite tenga por dónde entrar**.
- **La pantalla de una empresa recién creada es la que nadie prueba**, porque para probar cualquier otra
  cosa hay que meter datos antes. Ahí es donde se ven los campos que no se pueden rellenar y los estados
  vacíos que no dicen nada. Un barrido con un inquilino vacío encuentra lo que ninguno con datos.
- **La empresa que se fija a mano gana al token, y hay que saberlo.** `IContextoEmpresaPublico.FijarEmpresa`
  la usan cuatro endpoints —formulario público, enlace de baja, píxel de apertura, invitación— y en
  ellos el inquilino sale de algo firmado o de una fila. Antes ganaba el token: una petición a
  `/f/{clave}` con la sesión de otra empresa abierta habría guardado el lead en la empresa **de la
  sesión**. No pasaba porque el navegador no adjunta el token a esas rutas, pero eso es una casualidad
  del transporte. Si añades una ruta pública que derive la empresa de un token, fíjala y llama a
  `bd.ReaplicarEmpresaAsync` **antes** de la primera consulta.
- **Una pantalla escrita cuando solo había un rol se rompe al llegar el segundo.** Ajustes pedía sus
  ocho paneles sin mirar permisos, porque la única persona posible en una empresa era su propietaria.
  Con el primer comercial dentro eran **cinco 403 y una promesa sin recoger**. Al añadir un panel a
  Ajustes, mételo en `PANELES_AJUSTES` con el permiso que necesita.
- **Un botón que no se puede pulsar no se pone.** Todo lo que dispare un endpoint con permiso va marcado
  con `data-permiso="…"`; lo esconde `aplicarPermisos` y, para lo que se pinta después, un
  `MutationObserver`. Esconder **no es la seguridad** —esa la hace el servidor, endpoint a endpoint—;
  es no prometer algo que va a contestar 403. Si añades una acción, márcala: un `if` por botón se olvida
  en el siguiente botón.
- **Dos formas de esconder lo mismo se pisan.** El primer intento de lo anterior usaba `hidden`, y duró
  hasta la primera ficha de contacto: `pintarPrivacidad` hace `$('pv-alta').hidden = deBaja` —o sea
  **false** para un contacto normal— y volvía a enseñar lo que se acababa de esconder. Los permisos van
  con clase propia (`.sin-permiso`) y un `!important`; `hidden` se queda para las vistas.
- **Una API completa no es una funcionalidad.** `/cuentas` y `/tareas` estaban enteras desde los
  módulos 2 y 4, con sus tests. En la pantalla, las cuentas eran **un desplegable que no se podía
  rellenar** —así que estaba siempre vacío y todos los contactos eran B2C para siempre— y las tareas
  solo se veían de una en una en Hoy. Al cerrar un módulo, comprueba que se pueda **hacer** desde la
  interfaz, no solo desde `curl`.
- **Dos listas de lo mismo se desincronizan.** Las vistas estaban escritas a mano en el conmutador y
  otra vez como botones en el HTML; añadir una pantalla era una oportunidad de olvidar la segunda.
  Ahora `VISTAS` sale de `document.querySelectorAll('#menu .item[data-vista]')`. Misma regla para
  cualquier catálogo que ya exista en el DOM.
- **Un cubo de límite de intentos es del recurso, no de la IP.** Aceptar una invitación comprueba una
  contraseña, así que necesita techo; pero si el cubo fuera la IP, una oficina entera dándose de alta se
  estorbaría a sí misma, y compartirlo con el de entrar dejaría sin acceso a todo el mundo cinco
  minutos. Lo que se puede adivinar ahí es la contraseña de **una** cuenta, así que el cubo es la
  invitación: cinco intentos, y la de al lado sigue funcionando.
- **Un estilo de etiqueta encima de un dato lo cambia.** `.campo label` pone versalitas, y las casillas de
  eventos del webhook viven dentro de un `.campo`: `lead.creado` se leía **LEAD.CREADO**, que es justo el
  texto que hay que teclear tal cual en el otro sistema. Lo mismo con la dirección de la vista previa del
  correo. Misma especificidad, la regla de después gana **solo en las propiedades que redeclara**: lo que
  no repites lo sigues heredando. Y pasa cualquier revisión, porque el HTML es correcto y el CSS también.

## Eventos de dominio

Los agregados registran eventos (`RegistrarEvento`) y **hay un consumidor**: `DespachadorEventos`, que
los convierte en entregas de webhook dentro de `GuardarCambiosAsync`. Dos consecuencias:

- Si añades un evento de dominio y quieres que salga hacia fuera, se traduce en `DespachadorEventos`.
  La mayoría **no** se traducen, y eso es lo normal: el catálogo público son cinco cosas.
- Si emites un evento desde un sitio nuevo, sale por webhook **gratis**. Es lo que hace que ganar una
  oportunidad desde el repaso emita igual que ganarla desde el tablero. Ver
  [`docs/modulos/webhooks.md`](docs/modulos/webhooks.md).

## Consentimiento

**Nada que salga hacia una persona se manda sin pasar por `ServicioCumplimiento.PuedeEnviarAsync`.** Lo
usan el correo y lo usará cualquier canal futuro (WhatsApp, SMS), siempre por un puerto para no
referenciar el módulo. Dos cosas que no son obvias:

- Hace falta base legal **hasta para contestar**. Un contacto metido a mano no trae ninguna. No es un
  descuido: si añadiste el correo de alguien a un CRM, tienes que poder decir por qué puedes escribirle.
- Se comprueba **dos veces**: al encolar y otra vez justo antes de que salga. Entre lo uno y lo otro
  alguien puede darse de baja, y un correo comercial a quien acaba de pedir que no le escriban no es un
  fallo técnico. Ver [`docs/modulos/correo.md`](docs/modulos/correo.md).

## Automatizaciones

Si añades un tipo de acción a las reglas, mira antes
[`docs/modulos/automatizacion.md`](docs/modulos/automatizacion.md). Dos reglas que lo sujetan:

- **Ninguna acción toca el embudo.** Es lo que hace seguro descartar los eventos que generan las
  acciones, que es lo que impide que dos reglas se peloteen. Una acción que gane, pierda o mueva una
  oportunidad rompe las dos cosas a la vez.
- **Nada que salga hacia una persona se salta el consentimiento.** Una automatización no es una excusa.

## Interfaz

Es un **único fichero**, `src/Matchketing.Api/wwwroot/index.html`: tokens, estilos, vistas y
JavaScript. Sin dependencias externas ni paso de compilación. Paleta **ciruela**
(`--magenta: #5C2340`; el token conserva el nombre viejo para no tocar doscientas referencias), claro y
oscuro mediante `:root` + `prefers-color-scheme` + `[data-theme]`. **No hay rojo** en el sistema: lo
que en otras herramientas sería rojo aquí va en ámbar.

Y tres reglas de acabado, que son las que separan «herramienta» de «juguete»:

1. **El acento no rellena bloques.** La acción principal va en tinta; el color de marca aparece al
   pasar por encima, en la línea del elemento activo y en lo que de verdad avisa. Un bloque saturado de
   120 px es lo primero que hace que una pantalla parezca de plástico.
2. **Radios de 3–4 px, y ninguna pastilla de 999 px.** Lo redondo es simpático; esto no va de ser
   simpático.
3. **Serifa en titulares y cifras, pesos de 400 a 600.** El contraste tipográfico hace el trabajo que
   antes se le pedía al color, y `font-weight: 800` en todo era la mitad del problema.

Cuando cambies algo de la interfaz, **míralo**. Casi todos los defectos de este proyecto —CORS que
faltaba, conversiones inventadas, Match clavado en 100, JSON crudo en pantalla, el menú que no existía
en el móvil— salieron de una captura, no de un test.

Y **míralo también a 390 px**, midiendo `scrollWidth` contra `innerWidth`. Dos defectos de
desbordamiento vivían ahí desde el módulo 2. Lo adaptable va **al final del estilo**: un `@media`
colocado antes de la regla que quiere anular no hace nada, y eso ya pasó una vez. Ver
[`docs/movil.md`](docs/movil.md).

Al medir desbordes, **descarta lo que está dentro de un contenedor que se desliza a propósito**
(el tablero del embudo, las tablas anchas): ahí salirse del ancho de la ventana no es un defecto, y
contarlo tapa los que sí lo son. Lo que se mide es la ventana, no cada caja.

Dos cosas que un barrido con datos no ve, y hay que buscarlas a mano: **la empresa recién creada**
—estados vacíos y campos que no se pueden rellenar— y **el texto que un estilo cambia** (versalitas
encima de un nombre técnico o de una dirección de correo).

Y al añadir una sección: entra en `#menu` con su `data-vista`, su `<section id="vista-…">` y su entrada
en `ALENTRAR`. Si no es de uso diario, márcala `data-secundario` y aparecerá en «Más» en el móvil. Los
paneles de Ajustes van dentro de un `.grupo-ajustes`, y lo que ese grupo tenga que pedir al abrirse va
en `CARGAS_AJUSTES`: nada se pide a ciegas.

## Antes de producción

Lee [`docs/despliegue.md`](docs/despliegue.md). Lo más importante: la aplicación **tiene que
conectarse con un rol sin privilegios de superusuario**, o la RLS no se aplica y el aislamiento entre
empresas se queda con una sola barrera en lugar de dos.

## Entorno de ejecución sin SDK (nota)

Si no hay SDK de .NET instalado, `dn.sh` y `dsh.sh` lo ejecutan vía Docker
(`mcr.microsoft.com/dotnet/sdk:8.0`) con `--network host`. Son un apaño del entorno, no parte del
producto. Docker Hub puede estar bloqueado por política; `mcr.microsoft.com` y NuGet sí están
permitidos.
