# Módulo 10 — Avisos

**Estado**: terminado · **Proyecto**: `Matchketing.Avisos` · **Esquema**: `avisos`

Un aviso a la semana, al móvil, los viernes a las seis. El que cierra la tesis del repaso.

## El problema que resuelve

El [repaso](repaso.md) consigue que cerrar la semana cueste dos minutos. Eso resuelve el coste, pero
no resuelve el **acordarse**. Sin un empujón, el repaso lo hace quien ya era ordenado —que es justo
quien menos lo necesitaba— y el resto de la plantilla tiene una herramienta buenísima que abre dos
veces en marzo.

Un aviso no es una mejora de la interfaz: es la única pieza que actúa cuando la aplicación está
cerrada, que es el 99 % del tiempo.

## Las reglas del aviso

Todo módulo de avisos acaba, si nadie lo frena, con la gente apagando los avisos. Estas son las
cuatro reglas que lo frenan, y todas están en el código, no en la documentación:

### 1. Uno a la semana. Ni uno más

Viernes a las 18:00, hora de España. No hay avisos de «tienes un lead nuevo», ni de «se te vence una
tarea», ni de nada más. La frecuencia es el presupuesto entero y se gasta en una sola cosa.

### 2. Si no hay nada, no se manda nada

`ServicioAvisos.MinimoParaAvisar = 3`. Con menos de tres decisiones pendientes no sale el aviso. Un
aviso que dice «no tienes nada» enseña a ignorar los avisos, y una vez aprendido eso no se
desaprende. El silencio también es información: si no vibra, es que estás al día.

### 3. El aviso dice el número, y el número es el motivo para abrir

> **11 decisiones te separan de tenerlo al día. Un minuto.**

No «tienes tareas pendientes». El número concreto y el tiempo concreto. Lo escribe
`ServicioAvisos.Redactar` y es lo único que se envía: ni nombres, ni empresas, ni importes. Un aviso
se lee en una pantalla bloqueada, en un bar, con alguien al lado.

### 4. La idempotencia la da el dato, no el reloj

`SuscripcionAviso.UltimoAvisoEn` más tres días de gracia. El trabajo se comprueba cada media hora
dentro de una ventana de una hora, así que **corre dos veces por diseño**; y con dos instancias
detrás de un balanceador, cuatro. Si la garantía fuera «el cron solo dispara una vez», el día que
haya dos procesos llegarían dos avisos, y ese es exactamente el día en que la gente los apaga.

## Cómo funciona por debajo

Web Push, sin dependencias de terceros. Son tres normas y las tres están implementadas en el dominio:

| Norma | Qué aporta | Dónde |
| --- | --- | --- |
| RFC 8292 (VAPID) | Nos identifica ante el servicio de push con un JWT `ES256` | `ClavesVapid` |
| RFC 8291 | Cifra el cuerpo con `aes128gcm` para que el servicio de push no lo lea | `CifradoWebPush` |
| RFC 8188 | La forma del cuerpo: sal, tamaño de registro y clave efímera por delante | `CifradoWebPush` |

Lo importante de RFC 8291: **el servicio de push es un tercero** —Google, Mozilla, Apple— y el aviso
le pasa por delante. El navegador da dos claves al suscribirse (`p256dh` y `auth`), se hace un ECDH
P-256 contra una clave efímera nuestra distinta en cada envío, y de ahí salen la clave y el nonce de
AES-GCM. El servicio de push reenvía bytes que no puede leer.

La sal y la clave efímera son nuevas **en cada aviso**. Repetirlas con la misma clave sería reutilizar
el nonce de AES-GCM, que es la forma conocida de romperlo.

### Qué se hace con cada respuesta del servicio de push

Es la parte que más silenciosamente se rompe, y está cubierta código por código:

| Respuesta | Qué significa | Qué se hace |
| --- | --- | --- |
| 201, 200, 202, 204 | Entregado al servicio | Se marca `UltimoAvisoEn` |
| 404, 410 | El aparato ya no existe | **Se borra la suscripción** |
| 429, 5xx | El servicio tuvo un mal minuto | Se reintenta la semana que viene |
| resto (401, 403, 400, 413) | El problema es nuestro | Se registra con el código; ni se borra ni se reintenta |

Confundir un 410 con un 5xx deja reintentando para siempre contra un móvil que ya no existe, y eso es
lo que hace que un servicio de push empiece a limitar todo lo que mandas. Confundirlos al revés borra
los avisos de alguien porque el servicio se cayó un viernes.

## El permiso se pide donde se acaba de notar el valor

Un navegador deja preguntar **una sola vez**. Si dicen que no, no hay segunda oportunidad: no es un
diálogo que se pueda volver a sacar más tarde con mejores palabras.

Así que no se pregunta al abrir la aplicación, cuando nadie sabe todavía para qué sirve esto. Se
pregunta **al terminar un repaso**, en la pantalla que acaba de decir «has cerrado una venta por
6.200 €». Ahí la pregunta es «¿te aviso el viernes que viene?» y la respuesta es distinta.

Si ya dijeron sí o no, la oferta no vuelve a aparecer. Insistir es la otra forma de perder el permiso.

## Un aparato, no una persona

La identidad de una suscripción es el `endpoint`, la URL que da el servicio de push, y es única por
navegador y por aparato. El mismo comercial en el móvil y en el portátil son dos suscripciones, y eso
está bien: quiere el aviso donde esté.

Caducan solas y sin avisar —el navegador las rota, la gente cambia de móvil— y por eso `Renovar`
existe: el mismo endpoint con claves nuevas se actualiza en su sitio en vez de duplicarse.

En la pantalla de Ajustes se listan los aparatos con **el host del servicio de push, nunca el
endpoint completo**: el endpoint es la credencial con la que se le puede mandar un aviso a ese
aparato, y para enseñar «Chrome, activado el 20 de agosto» no hace falta.

## El fallo tiene que verse

`pushManager.subscribe()` no siempre falla: cuando el navegador no alcanza el servicio de push de su
fabricante —una wifi de empresa que cierra el puerto 5228, un cortafuegos, un bloqueador—, reintenta
por dentro y **la promesa se queda colgada para siempre**. Sin un plazo, el botón se queda gris y en
la pantalla no aparece nada: para quien lo pulsa, la aplicación está rota y sin explicación.

Por eso el alta tiene un plazo de quince segundos y, al agotarse, dice de quién es el problema y qué
probar. Y por eso `motivoDe` nunca devuelve vacío: hay rechazos del navegador que llegan sin
`message`, y un fallo sin texto se vive igual que un botón que no hace nada.

## Qué está probado y qué no

Probado aquí, en verde:

* **El cifrado, contra una implementación ajena.** El cuerpo que produce `CifradoWebPush` se descifra
  con `http_ece` en Node y coincide. Sin eso, «mi código hace lo que yo creo que dice la norma» no es
  una prueba de nada. Hay además un vector fijado en `PruebasCifradoWebPush` para que un cambio futuro
  no lo mueva sin darse cuenta.
* **El token VAPID, verificándolo como lo verifica el servicio de push**: rehaciendo la firma con la
  clave pública que va en `k=`. Cubre los dos errores que se pagan con un 401 sin explicación —la
  firma en DER en vez de P1363, y la audiencia con la ruta dentro—.
* **Las cabeceras y el mapeo de respuestas** de `EmisorWebPush`, código por código, contra un servicio
  de push de mentira que se queda con la petición entera (`PruebasEmisorWebPush`).
* **El trabajador de servicio**, entregándole el push por CDP, que es el mismo camino por el que se lo
  entrega el navegador: el aviso se pinta con su texto, dos avisos seguidos no se apilan porque
  comparten `tag`, y un cuerpo ilegible muestra el genérico en vez de callarse.
* **El plazo del alta**, en un navegador de verdad y en las condiciones que lo provocan.

**No probado aquí**: el alta contra el servicio de push real y la llegada del aviso a un móvil. El
alta la hace el navegador por MTalk —`mtalk.google.com:5228`—, que no es HTTPS y no sale de este
contenedor. Eso se cierra instalando la aplicación en un móvil, activando los avisos en Ajustes y
esperando al viernes; o adelantando el reloj del servidor a un viernes a las 18:00.

## API

| Método | Ruta | Permiso | Qué hace |
| --- | --- | --- | --- |
| GET | `/avisos/clave` | sesión | La clave pública VAPID con la que se suscribe el navegador. |
| GET | `/avisos/aparatos` | sesión | Los aparatos propios con avisos activados. Sin el endpoint. |
| POST | `/avisos/suscripcion` | `tarea.leer` | Da de alta este aparato. Idempotente. |
| DELETE | `/avisos/suscripcion?endpoint=` | sesión | Lo apaga. **Nunca falla**, ni si no existía. |

`DELETE` no falla nunca a propósito: quien dice «no quiero avisos» no puede recibir un error por
respuesta.

## Configuración

```jsonc
{
  "Avisos": {
    "ClavePublica": "…",          // 65 bytes, punto sin comprimir, en base64url
    "ClavePrivada": "…",          // 32 bytes
    "Sujeto": "mailto:avisos@…"   // o una https://; lo exige RFC 8292
  }
}
```

Si no están, se generan al arrancar y se avisa por registro. Sirve para desarrollo y **no** para
producción: unas claves nuevas invalidan todas las suscripciones existentes, así que un reinicio
dejaría a toda la plantilla sin avisos y sin que nadie se enterase hasta el viernes.

La clave privada de VAPID es **distinta** de la de firmar los JWT de sesión. Comparten nada: son dos
secretos con dos ciclos de vida y dos consecuencias distintas si se filtran.

## Modelo

```
avisos.suscripcion
  id, empresa_id, usuario_id
  endpoint      unique   -- la identidad; hasta 600 caracteres
  clave_publica          -- el p256dh del navegador
  secreto                -- el auth, 16 bytes
  creado_en, ultimo_aviso_en
```

Con RLS forzada como todo lo demás. La única consulta que usa `IgnoreQueryFilters` es
`PorEndpointAsync`, y está comentada en el sitio: el endpoint es único **globalmente**, así que buscar
por endpoint dentro de una empresa dejaría insertar un duplicado que la restricción de la tabla
rechazaría después con un error que no dice nada.
