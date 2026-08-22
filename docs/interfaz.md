# La interfaz: letras y color

Un solo fichero, `src/Matchketing.Api/wwwroot/index.html`, con los tokens, el estilo, las vistas y el
guion. Sin paso de compilación y sin ninguna dependencia en tiempo de ejecución. Este documento
explica las dos decisiones que más se tocan y que más fácil es deshacer sin darse cuenta: **de dónde
salen las letras** y **qué significa cada color**.

## Las letras están en el repositorio, no en un CDN

Dos familias: **Fraunces** para los titulares y las cifras grandes, **Nunito Sans** para todo lo demás.
Cinco ficheros en `wwwroot/tipos/`, 220 KB en total, partidos por `unicode-range` en latín y latín
extendido, de modo que una pantalla en español descarga tres ficheros y no cinco.

### Por qué se cambió la pareja anterior

Antes eran Instrument Sans e Instrument Serif, y el motivo del cambio cabe en una frase de quien lo
usa: «la fuente de texto no es amigable». Tenía razón, y era una decisión, no un descuido:

- **Instrument Sans** es una grotesca de contornos rectos y aperturas cerradas. Compone limpísimo y
  compone **frío**: en una pantalla de nueve paneles blancos con líneas de un píxel, el conjunto se
  parecía más a una hoja de cálculo bien hecha que a una herramienta que uno abre por gusto.
- **Nunito Sans** es humanista: aperturas abiertas en la «a», la «e» y la «s», «g» de un piso, curvas
  blandas y una altura de x generosa. A 15,5 px eso se lee como una voz que habla despacio.

Y la línea que no se cruza: **es Nunito *Sans*, no Nunito.** Nunito lleva las terminaciones
redondeadas, y lo redondo de verdad —Nunito, Quicksand, Baloo— es simpático diez minutos y luego parece
de niños. Eso ya se dijo una vez de esta interfaz y no se va a repetir.

**Fraunces** sustituye a Instrument Serif por lo mismo: es una serifa blanda con eje óptico (`opsz`),
así que a 35 px de titular engorda los remates y se vuelve casi manuscrita, y a 19 px de un `h2` se
calma. Hace falta `font-optical-sizing: auto` en el `body`; sin eso, un titular de 35 px se dibuja con
los remates de un texto de 12 y pierde exactamente lo que se ha venido a buscar.

El precio del cambio son 125 KB más de letra, casi todos de Fraunces —una variable de dos ejes—. Se
acepta: se descargan una vez y a partir de la segunda visita los sirve el trabajador de servicio.

Están servidas desde aquí y no enlazadas a Google Fonts **por privacidad, no por velocidad**. Un
`<link rel="stylesheet" href="https://fonts.googleapis.com/…">` hace que el navegador de cada
comercial pida los ficheros a un servidor de Google, y con la petición le manda su dirección IP y la
página que está mirando. En una herramienta que se vende diciendo que los datos son tuyos eso es una
contradicción, y encima una que no se ve: nadie mira de dónde salen las letras.

Lo protegen dos pruebas en `PruebasMovil`:

- `Las_letras_son_del_propio_servidor_y_no_de_un_tercero` prohíbe `fonts.googleapis.com`,
  `fonts.gstatic.com` y `use.typekit` en la página, y además comprueba que **cada** `url('/tipos/…')`
  declarada en el estilo se sirve de verdad y con tipo `font/woff2`. Un `@font-face` que apunta a un
  fichero que no existe no da ningún error: la página se cae a la letra del sistema en silencio.
- `Las_letras_se_guardan_para_cuando_no_haya_cobertura` exige `/tipos/` en la lista blanca del
  trabajador de servicio. La lista es blanca a propósito (ver `docs/movil.md`), así que lo que no se
  añade a mano no se guarda; la prueba es ese recordatorio.

Al añadir `/tipos/` al armazón hubo que arreglar además la respuesta de emergencia. Sin red y sin
copia, el trabajador devolvía la raíz **para cualquier ruta**, que está bien para una navegación —esto
es una aplicación de una sola página— y está mal para un fichero: el navegador intentaría leer
`index.html` como si fuera un woff2. Ahora la raíz solo se sirve si `peticion.mode === 'navigate'`;
para lo demás la petición falla, y `font-display: swap` deja la letra del sistema, que es exactamente
lo que toca.

## Que se acoja: el tono, la esquina y la sombra

Tres decisiones que se tomaron al revés en la primera versión y se cambiaron a la vez. Están juntas
aquí porque las tres responden a lo mismo —«es muy simple, quiero algo cálido»— y por separado no
habrían llegado.

### Todo el gris tiene marrón dentro

El papel es crema (`--papel: #FBF6F0`), la tinta es un marrón muy oscuro y no un negro
(`--tinta: #241B18`) y el gris de los textos secundarios es cálido (`--grafito: #6F5F58`). Son tres
cambios de dos o tres grados de tono, no se pueden señalar mirando un color aislado, y son la mitad de
la sensación de acogida. La otra mitad la ponen las letras.

El tema oscuro va igual: marrón muy oscuro, no negro azulado. Un tema oscuro frío al lado de un tema
claro cálido son dos productos distintos.

Los contrastes se comprobaron uno a uno: el más justo es el ámbar sobre papel, 5,5:1, y el resto pasa
de 5,6:1. El mínimo de AA para texto pequeño es 4,5:1.

### Tres radios, y más grandes

`--r-xs: 6px`, `--r-s: 10px`, `--r-m: 14px`. Antes había **trece** valores distintos entre 2 y 12 px,
que es lo que pasa cuando cada pantalla elige el suyo: nada rima con nada.

Y son más grandes que antes a propósito. La regla vieja decía «radios de 3–4 px y ninguna pastilla»,
con este razonamiento: lo redondo es simpático y esto no va de ser simpático. El razonamiento estaba
mal dirigido: **lo que hacía pueril el primer intento era el bloque de color saturado, no la curva.**
Con el acento guardado para señalar —la acción principal sigue yendo en tinta— 14 px en una tarjeta no
la convierten en un juguete, la convierten en algo que se puede coger. Las pastillas de 999 px vuelven,
pero solo donde el elemento es un dato de una línea: chips de estado, contadores y barras de progreso.

### La sombra vuelve, y es marrón

La regla vieja era «sin sombras: el papel se separa con líneas de un píxel, que es lo que hace un
impreso bien hecho». Bien argumentado y llevaba a una pantalla plana: dieciocho rectángulos de un píxel
de borde, todos a la misma altura, sin nada que invitara a tocar nada.

La sombra que se pone no es la de 2014: `0 1px 2px` casi invisible más `0 12px 28px -20px` de velo, y
**del color de la tinta** —marrón—, no un gris neutro. Una sombra no colorea; solo dice que la tarjeta
está encima del papel. Las tarjetas de Hoy suben un píxel al pasar por encima, y solo ellas: es la
única pantalla donde hay que elegir una entre varias.

## El color dice un dato o no se pinta

La regla es esa, y es lo que separa una paleta de una decoración. Un color que no se mueve cuando el
dato se mueve es un adorno, y con adornos se llega a la pantalla que parece un juguete.

### La escala de avance

Cinco tonos, de frío a verde, en `--av-1` … `--av-5`:

| Token | Probabilidad de la etapa |
|---|---|
| `--av-1` | menos del 20 % |
| `--av-2` | 20-39 % |
| `--av-3` | 40-59 % |
| `--av-4` | 60-79 % |
| `--av-5` | 80 % o más |

El color lo pone la **probabilidad** de la etapa, no su posición en el tablero. Por dos motivos:

- Las etapas se configuran por empresa. Un embudo de tres etapas y otro de siete pintarían escalas
  distintas para lo mismo si el color fuese el índice; con la probabilidad, «casi firmado» es del
  mismo color en las dos empresas.
- Si alguien baja «Propuesta» del 60 % al 25 %, la etapa cambia de color porque ha cambiado el dato.

Va hacia el verde porque el verde ya es el color de *ganado* en el resto de la aplicación: la última
etapa se parece al final, y eso es justo lo que quiere decir.

Lo decide **una sola** función, `banda(probabilidad)`, y la usan tres pantallas:

- **Embudo**: el punto junto al nombre de la columna, la línea de importe y el canto izquierdo de cada
  tarjeta. En el tablero el canto es redundante con la columna, y a propósito: al arrastrar, la
  tarjeta viaja con su color y se ve de dónde sale.
- **Informes**: el punto junto al nombre de la etapa y el relleno de la barra. Misma etapa, mismo
  color que en el tablero, así que se aprende una vez y sirve en las dos pantallas. La leyenda se
  imprime aquí y solo aquí: quien arrastra tarjetas ya sabe lo que es «Cierre», y una leyenda sobre el
  tablero sería una fila de adorno encima del trabajo.
- **Match**: la cifra de match es también una probabilidad, así que se pinta con la misma escala. Un
  84 de match y una etapa al 84 % son del mismo color a propósito.

Un valor que no se sabe **no tiene banda**: `banda` devuelve cadena vacía, no la banda 1. Pintar la
banda más baja diría «poco probable» donde el dato dice «no se sabe», y son cosas distintas —el mismo
motivo por el que un dato que no se puede calcular sale como guion y nunca como 0—.

### Los cuatro colores con significado fijo

| Color | Qué quiere decir | Dónde |
|---|---|---|
| `--rojo` | ya va tarde, o se ha roto, o no se le puede escribir | tarea vencida, rebote, contacto de baja, motivos de pérdida, importe perdido |
| `--ambar` | está parado, o es una acción sin vuelta atrás | oportunidad estancada, «borrar todos sus datos» |
| `--turquesa` | se ha movido el contacto | formulario, visita web, apertura; «sin próximo paso» |
| `--verde` | ganado | oportunidad ganada, contacto que es cliente, importe ganado |

El ciruela de la casa (`--magenta`) no está en esa tabla porque no es un semáforo: señala **lo tuyo**
—el trabajo del comercial— y marca la sección activa. La acción principal va en tinta, no en ciruela;
el acento se guarda para señalar.

### Dos lecturas que antes había que deducir leyendo

**La pila de Hoy.** Cada tarjeta lleva en el canto izquierdo el color de su motivo: rojo si va tarde,
ciruela si es de hoy, ámbar si está parada, turquesa si no tiene próximo paso. Se lee la pila de un
barrido sin leer una palabra. La etiqueta y el canto salen de la misma función, `motivoTarjeta(t)`,
así que no pueden discrepar. La primera tarjeta engorda su canto a 4 px en vez de cambiar de color:
sigue siendo «la siguiente», no «otra cosa».

**La cronología de una ficha.** El punto no repite el tipo de actividad —eso ya lo pone la línea de al
lado— sino **quién se movió**: ciruela si te moviste tú (nota, llamada, correo, reunión), turquesa si
se movió el contacto (formulario, visita web, apertura), rojo si se rompió algo (rebote), gris si lo
hizo la máquina. Una ficha con la columna en turquesa es un contacto caliente; una toda en ciruela es
alguien a quien persigues sin respuesta. Eso, antes, había que deducirlo leyendo nueve líneas.
`quienSeMovio` devuelve `'sistema'` para un tipo que no reconoce: lo prudente es no atribuirle a nadie
lo que no se sabe de quién es.

### Baja y perdido no son el mismo gris

Compartían el gris apagado y no son lo mismo. **Perdido** es una venta que no salió y se puede volver
a intentar. **Baja** es que retiró el consentimiento y no se le puede escribir. Igualar los dos es la
clase de detalle con el que se manda un correo ilegal, así que la baja se pinta como lo que es: un
aviso en rojo, no un estado apagado.

## Una casilla de texto lleva estilo esté donde esté

El estilo de un `input` de texto vivía **solo** en `.campo input`. Cualquier casilla creada fuera de un
`.campo` —los campos propios del módulo 17, entre otras— salía con el estilo del navegador: fondo
blanco, esquina cuadrada, borde azulado. En tema claro pasaba por un campo pálido y nadie lo vio; en
tema oscuro era un rectángulo blanco en medio de la ficha.

Ahora el estilo va por tipo de elemento, dentro de un `:where(...)`. El `:where()` no es un adorno de
sintaxis: deja la regla **con especificidad cero**, así que todas las que ya existían —`.campo input`,
`.buscador`, los tamaños de cada pantalla— siguen ganando sin haber tocado ninguna. Y el `html` lleva
`color-scheme: light dark`, que es lo que hace que el calendario de un `input type="date"` y las barras
de desplazamiento se dibujen con el tema puesto.

Lo cubre `Una_casilla_de_texto_lleva_estilo_esté_donde_esté` en `PruebasMovil`.

## Errores que ya se han cometido aquí

- **`min-width` ganándole a `width: 0`.** La barra de una etapa tenía `min-width: 2px` en la hoja y el
  guion le ponía `width: 0` cuando la etapa estaba vacía. Gana el `min-width`: una etapa sin nada
  abierto pintaba un tope de color. El suelo del 2 % —para que un importe pequeño se siga viendo— lo
  pone el guion, y solo cuando hay algo que enseñar. Lo cubre `Una_etapa_a_cero_no_pinta_nada`.
- **Un color por posición en vez de por dato.** La línea bajo la última columna del tablero era
  ciruela por ser la última, así que «Cierre» vacía se llevaba la mirada y «Nuevo» con 71.800 €
  quedaba apagada.
- **Una afirmación que se cumplía por otro sitio.** La prueba del color de etapa decía
  `Should().Contain("colorAvance(e.probabilidad)")`, y quitarle el color a la barra no la rompía
  porque el punto de al lado ya contenía esa cadena. Ahora se exigen los dos usos por separado.
