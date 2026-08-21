# La interfaz: letras y color

Un solo fichero, `src/Matchketing.Api/wwwroot/index.html`, con los tokens, el estilo, las vistas y el
guion. Sin paso de compilación y sin ninguna dependencia en tiempo de ejecución. Este documento
explica las dos decisiones que más se tocan y que más fácil es deshacer sin darse cuenta: **de dónde
salen las letras** y **qué significa cada color**.

## Las letras están en el repositorio, no en un CDN

Dos familias, pensadas para ir juntas: **Instrument Serif** para los titulares y las cifras grandes,
**Instrument Sans** para todo lo demás. Cinco ficheros en `wwwroot/tipos/`, 95 KB en total, partidos
por `unicode-range` en latín y latín extendido, de modo que una pantalla en español descarga dos
ficheros y no cinco.

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
