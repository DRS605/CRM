# El móvil

Un comercial no está sentado delante de un portátil. Está en el coche, en un portal, esperando a que
le abran. Si el repaso solo se puede hacer en la oficina, se hará los viernes que se acuerde y se
dejará de hacer el tercer viernes.

Esta página es lo que hemos hecho para que match.keting sea una aplicación en un móvil, y lo que
todavía no.

## Lo que se arregló, y cómo se encontró

Nada de esto salió leyendo el CSS. Salió abriendo la aplicación en una pantalla de 390 px con
Playwright y midiendo.

### El menú no existía en el móvil

Había un `@media (max-width: 760px)` que escondía la barra lateral… colocado **antes** de la regla que
pretendía anular. En CSS, a igual especificidad gana la última, así que no hacía absolutamente nada: en
un teléfono la barra se apilaba encima del contenido y salía cortada por arriba.

Ahora todo lo adaptable está **al final del estilo**, donde sí gana, y en pantallas estrechas el menú
deja de ser una columna y pasa a ser una **barra abajo**, que es donde llega el pulgar. La marca y el
bloque de usuario desaparecen de ahí: no caben en una barra de pulgar y tampoco hacen falta, porque
quien tiene la aplicación abierta ya sabe cuál es y quién es.

### La página se estiraba a lo ancho

`scrollWidth` 1052 contra `innerWidth` 390 en el embudo, y 785 contra 390 en contactos. El tablero mide
1000 px a propósito y debía deslizarse dentro de su caja; en vez de eso **estiraba la página entera** y
todo quedaba corrido hacia la derecha.

La causa es la trampa clásica de flex y grid: un elemento no baja de su contenido porque su
`min-width` es `auto`. Y no bastaba con arreglarlo en la caja que desliza — había que hacerlo en **toda
la cadena**: `main` es un elemento de rejilla, las secciones son elementos flex y sus hijos también.

No era un defecto solo del móvil: pasaba igual en un portátil con la ventana estrecha.

### «Arrastra una tarjeta» era mentira

El arrastre de HTML5 no existe en los navegadores táctiles. En un móvil el tablero era de solo lectura
y el texto de ayuda pedía algo imposible, que es la forma más rápida de que alguien piense que la
aplicación está rota.

Ahora cada tarjeta tiene un botón **Mover** que abre la lista de etapas, y el texto de ayuda cambia
según el aparato. El botón está también con ratón: dos toques nunca son peor que un arrastre, y hay
gente que arrastra fatal.

### La tarjeta del repaso, para un pulgar

Cada respuesta ocupa el ancho y mide **50 px de alto**, que es el mínimo que recomiendan Android y iOS
para un objetivo de dedo. El número de la tecla desaparece: en un teléfono no hay teclado, y enseñar un
atajo que no existe es ruido.

Y `@media (hover: none)` quita los `:hover`, porque en táctil se quedan pegados y dejan botones
«encendidos» después de pulsarlos.

## Instalable

Con manifiesto, iconos e `apple-touch-icon`: se añade a la pantalla de inicio y abre a pantalla
completa, sin barra de direcciones. Un comercial no escribe una URL, abre un icono.

Los iconos se generan con un **codificador PNG escrito a mano** (`zlib` + `struct`) en vez de con una
librería de imágenes: el icono es geometría plana —el punto de la marca sobre magenta— y no merecía una
dependencia. Hay versiones `maskable` con margen, porque sin ellas Android recorta el círculo por la
mitad del punto.

Manteniendo pulsado el icono aparecen dos **atajos**, Repaso y Hoy, que abren directos en su pantalla
(`/?ir=repaso`).

### El número en el icono

Con la aplicación instalada, las decisiones pendientes salen **en el icono**, con
`navigator.setAppBadge`.

Es el aviso más barato que existe y el que menos molesta: no suena, no vibra, no interrumpe. Solo está
ahí diciendo «tienes once cosas que decidir». Y un cero **borra** el distintivo en lugar de pintarlo:
un cero permanente en un icono es una regañina, y de las regañinas se huye desinstalando.

### El trabajador de servicio guarda el armazón, nunca los datos

`sw.js` hace una sola cosa: guardar la página y los iconos para que abra al instante y para que abra
también sin cobertura, que es lo normal en un polígono.

Lo que **no** hace, a propósito:

* **No guarda respuestas de la API.** Una pila de repaso de hace tres días es peor que ninguna: tomarías
  decisiones sobre cosas que ya cambiaron. Los datos son de ahora o no son.
* **No encola respuestas para enviarlas luego.** Contestar «Ganada» y que se envíe mañana significa que
  durante un día el embudo miente. Si no hay red, la aplicación lo dice y no finge.

## El aviso del viernes: ya está

El distintivo en el icono es el 80 % del efecto, pero solo lo ves si mueves el teléfono. El aviso de
verdad —Web Push, con claves VAPID y el cuerpo cifrado— es el [módulo 10](modulos/avisos.md), y está
terminado: un aviso a la semana, los viernes a las seis, y solo si hay al menos tres decisiones
pendientes.

Lo que **no** se ha podido ver funcionar desde aquí es el último tramo: el alta del navegador contra
el servicio de push de su fabricante va por MTalk (`mtalk.google.com:5228`), que no es HTTPS y no sale
de este contenedor. Todo lo demás sí está comprobado, incluido el cifrado contra una implementación
ajena y el trabajador de servicio recibiendo un push de verdad. El detalle de qué está probado y qué
no está en [`modulos/avisos.md`](modulos/avisos.md); no diremos que funciona el tramo que no hemos
visto funcionar.

## Lo que falta

**Trabajar sin cobertura.** Hoy la aplicación abre sin red pero no puede hacer nada: es honesto y es
poco. Encolar respuestas exigiría resolver qué pasa cuando dos comerciales contestan lo mismo desde dos
sitios, y eso es un módulo, no un detalle.

**Probar en aparatos de verdad.** Todo esto está medido en un Chromium con la ventana a 390 px, que
comprueba el CSS pero no un Safari de iPhone ni un Android viejo. La barra inferior usa
`env(safe-area-inset-bottom)` para el hueco del gesto, pero eso hay que verlo en un teléfono con
muesca.

## Cómo se comprueba

```bash
dotnet test --filter PruebasMovil    # el manifiesto, los iconos y el trabajador
```

Son pruebas humildes —que los ficheros existen y se sirven con su tipo— y valen justo por eso: el
manifiesto y el trabajador **se rompen en silencio**. Si el manifiesto se sirve con el tipo equivocado
o le falta un icono, el navegador simplemente no ofrece instalar nada. Nadie ve un error, y el
comercial nunca tiene el icono en su pantalla de inicio.

Lo visual se mira con Playwright a 390 px, midiendo `scrollWidth` contra `innerWidth` en cada vista.
Ese número es el que encontró los dos defectos de arriba.
