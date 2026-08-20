# Módulo 12 — Correo

**Estado**: terminado · **Proyecto**: `Matchketing.Correo` · **Esquema**: `correo`

Escribirle desde la ficha, con plantillas, y —lo que de verdad justifica el módulo— **una pregunta nueva
en el repaso que antes no se podía hacer**.

## Por qué este módulo antes que cualquier otro

Hasta ahora el sistema apuntaba que habías escrito a alguien, pero el correo lo escribías en otra parte.
Eso es exactamente la redundancia por la que un comercial deja de usar un CRM: hacer el trabajo dos
veces.

Pero el motivo fuerte es otro. Con el correo dentro, el [repaso](repaso.md) puede preguntar:

> **Le escribiste hace 6 días. Lo ha abierto 3 veces y no ha contestado.**
> · Le llamo hoy · Ya me contestó · Déjalo estar

Un correo sin respuesta es la situación comercial más común que existe y la que más se queda sin
resolver, porque **no genera ninguna tarea ni ninguna alerta**. Nadie apunta «volver a llamar a quien no
me contestó». Es la séptima pregunta del repaso y probablemente la más rentable de las siete.

## Abrir no es contestar

Esa distinción tiene consecuencias en tres sitios, y es la decisión de diseño más importante del módulo.

Una apertura se apunta con **su propio tipo de actividad** (`TipoActividad.AperturaCorreo`), no como un
correo entrante. Si contara como entrante:

* el repaso dejaría de preguntar por alguien **justo cuando más hay que llamarle** —quien abre tu correo
  tres veces y no contesta es el mejor candidato del día—;
* el informe de la semana contaría respuestas que nadie ha dado.

Va como **entrante** (es algo que hizo la otra persona, así que cuenta como señal de interés para el
Match) pero con tipo propio. Hay una prueba dedicada solo a esto:
`Abrir_el_correo_NO_cuenta_como_contestar`.

Y por eso existe la respuesta **«Ya me contestó»**: el comercial recibe la respuesta en *su buzón*, no
aquí. Sin esa opción solo podría contestar «déjalo estar», y el sistema seguiría creyendo que nadie
contestó. Apuntarlo deja la respuesta donde tiene que estar —en su cronología— y hace que la pregunta no
vuelva. Se descubrió escribiendo la prueba, no diseñando.

## El permiso se comprueba dos veces

Y la segunda es la que cuenta.

```
Pulsar «Enviar»  →  ¿puede?  →  fila en el buzón de salida
                                        ↓  (hasta un minuto después)
                          ¿puede TODAVÍA?  →  SMTP
```

Entre encolar y enviar pasan minutos, y en esos minutos alguien puede darse de baja. Un webhook que sale
tarde no molesta a nadie; **un correo comercial a quien acaba de pedir que no le escriban es una
infracción.** Por eso existe el estado `Cancelado`, que se distingue del fallo: no ha fallado nada, es
que no había que mandarlo.

La regla la decide `ServicioCumplimiento.PuedeEnviarAsync`, donde ya estaba probada. Este módulo la
consume por un puerto (`IPermisoDeEnvio`) para no referenciar al de cumplimiento.

Y **hace falta base legal hasta para contestar**. Un contacto metido a mano no trae ninguna: si añadiste
el correo de alguien a un CRM, tienes que poder decir por qué puedes escribirle. Interés legítimo vale,
pero hay que apuntarlo. La pantalla enseña el borrador igual y dice qué falta y dónde arreglarlo.

## El «para qué» sale de la plantilla, nunca del cliente

Cada plantilla dice si es para **atender una solicitud** o **comercial**, y eso decide qué
consentimiento se exige. Es un dato de la plantilla y no un parámetro del envío por un motivo concreto:
si viniera del cliente, bastaría con mandar `AtenderSolicitud` para saltarse el consentimiento
comercial. Un correo escrito a mano, sin plantilla, solo puede ser para atender una solicitud.

## Los huecos: cuatro, y estrictos por los dos lados

`{{nombre}}` · `{{cuenta}}` · `{{comercial}}` · `{{empresa}}`

Cuatro y cerrados. Con cuarenta campos disponibles llegan plantillas que fallan porque *ese* contacto no
tiene *ese* dato; con cuatro que casi siempre existen se pueden exigir.

* **Al guardar**, un hueco que no existe es un error. Dejarlo pasar significa que el correo saldrá con
  las llaves puestas, y eso no se descubre hasta que lo lee el cliente.
* **Al enviar**, un hueco sin valor es un error. «Hola ,» es peor que no mandar nada: se nota que viene
  de una máquina, que es justo lo que la plantilla intentaba disimular.

`{{nombre}}` es el **nombre de pila**, no el completo: «Hola Manolo García,» no lo escribiría nadie.

## Texto plano

No es una limitación pendiente de resolver. Un correo de un comercial a un cliente es un correo de una
persona a otra, y los que llegan maquetados con cabecera y botones se leen como publicidad porque lo
son. Además evita de golpe todo el trabajo de sanear HTML.

La única parte HTML que existe es la del píxel, y solo si la empresa lo ha activado.

## El seguimiento de aperturas: apagado por defecto

`Empresa.SigueAperturas` empieza en `false`, **y eso es una decisión**. Saber si alguien ha abierto tu
correo es medir su comportamiento, no gestionar un dato que te dio. Que sea una decisión explícita —y no
algo que ya está puesto cuando se abre la cuenta— es la diferencia entre una herramienta que se puede
defender delante de un cliente y una que hay que explicar.

Con el seguimiento apagado, el correo sale **solo en texto plano**: sin parte HTML, sin imagen y sin nada
que cargar.

### Cómo funciona el píxel

`GET /e/{token}.gif` → siempre el mismo GIF de 1×1, exista el token o no.

* **Siempre la misma imagen.** Contestar 404 a un token inventado confirmaría, por eliminación, cuáles
  sí existen. Y a quien abre el correo le da igual: solo quiere una imagen.
* **El token lleva la empresa dentro.** La petición la hace el cliente de correo de la persona: no trae
  sesión y no puede traerla. Sin saber la empresa, la RLS de PostgreSQL no deja ver ninguna fila y la
  apertura no se apuntaría nunca. Es el mismo truco que el enlace de baja. Y así la consulta se hace
  **con el filtro puesto**, no saltándoselo: un token de una empresa no puede leer nada de otra.
* **16 bytes al azar** además de la empresa. Con un token corto o secuencial, cualquiera podría
  recorrerlos y marcar como abiertos correos que nadie ha abierto.
* **`no-store`.** Si se cacheara, la segunda apertura no llegaría nunca.
* Solo cuenta si el correo **llegó a salir**: un píxel de un correo que nunca se envió es alguien
  probando tokens a mano.
* La cronología apunta **solo la primera**. Cinco líneas de «ha abierto el correo» no dicen más que una;
  el recuento queda en el propio correo.

### Y nunca se llama «leído»

En la pantalla dice «se ha abierto 3 veces», no «lo ha leído 3 veces». Hay clientes de correo que
precargan las imágenes sin que nadie mire nada, y otros que no las cargan jamás. Llamarlo «leído» sería
inventarse un dato, y este proyecto no enseña un número sin motivo.

Por lo mismo, **«no lo ha abierto» no significa «no lo ha leído»** y la pregunta del repaso no lo dice:
dice «no ha contestado», que es lo que sí sabemos.

## Buzón de salida

Una fila, no una llamada SMTP al pulsar. Un servidor de correo lento dejaría al comercial mirando una
rueda, y un fallo de red perdería el correo sin rastro. Con la fila, además, **queda constancia del texto
exacto que se mandó**, que en un correo comercial es la mitad del valor.

* La cronología se apunta **al encolar**, no al enviar. Si se apuntara al salir, el comercial vería su
  ficha sin rastro del correo que acaba de mandar y volvería a mandarlo.
* Cuatro intentos en algo más de veinte minutos, mucho más corto que los [webhooks](webhooks.md): un
  correo que sale seis horas tarde ya no sirve porque la conversación siguió por otro lado.
* Un **5xx del SMTP no se reintenta**. Un buzón que no existe no se arregla insistiendo, y hacerlo cuatro
  veces es la forma conocida de que un servidor de correo empiece a marcar todo lo que mandas como no
  deseado. Un 4xx sí.
* La pantalla dice «en cola», no «enviado». Decir «enviado» sería lo cómodo y sería mentira mientras el
  buzón no se vacíe.

## API

| Método | Ruta | Permiso | Qué hace |
| --- | --- | --- | --- |
| GET | `/plantillas/campos` | `contacto.leer` | Los cuatro huecos. |
| GET | `/plantillas` | `contacto.leer` | Las plantillas, las más usadas primero. |
| POST | `/plantillas` | `empresa.ajustes` | Crea una. Rechaza los huecos que no existan. |
| PUT | `/plantillas/{id}` | `empresa.ajustes` | La cambia. No toca los correos ya enviados. |
| DELETE | `/plantillas/{id}` | `empresa.ajustes` | La borra. El historial no se toca. |
| GET | `/correo/borrador?contactoId=&plantillaId=` | `contacto.leer` | Lo que se va a mandar y si se puede. **Sin enviar nada.** |
| POST | `/correo/enviar` | `contacto.gestionar` | Encola. Devuelve **202**, no 200. |
| GET | `/correo/contacto/{id}` | `contacto.leer` | Sus correos, con el texto y las aperturas. |
| GET | `/e/{token}.gif` | — (público) | El píxel. |

Escribir una plantilla pide `empresa.ajustes` porque el texto sale en nombre de la empresa; mandar un
correo pide `contacto.gestionar`, que es lo que ya tiene un comercial.

## Modelo

```
correo.plantilla
  id, empresa_id, nombre, asunto, cuerpo
  para_que      -- 1 atender una solicitud · 2 comercial
  usos, creada_en

correo.mensaje
  id, empresa_id, contacto_id, usuario_id
  para          -- la dirección, congelada al encolar
  asunto, cuerpo, para_que, plantilla_id
  estado        -- 1 en cola · 2 enviado · 3 fallido · 4 cancelado
  intentos, proximo_intento_en, creado_en, enviado_en, ultimo_fallo
  token_apertura  unique   -- empresa + 16 bytes al azar
  primera_apertura_en, ultima_apertura_en, aperturas
```

Las dos con RLS forzada. `correo.mensaje` es de las tablas más sensibles del sistema: guarda el texto
exacto de lo que se le ha escrito a una persona.

El historial de la pantalla **no devuelve el cuerpo por la API pública de entregas** de webhooks ni se
enseña a nadie sin `contacto.leer`.

## Configuración

```jsonc
{
  "Smtp": {
    "Servidor": "smtp.tuservidor.es",
    "Puerto": 587,
    "Usuario": "…",
    "Contrasena": "…",
    "Remitente": "comercial@tuempresa.es",
    "NombreRemitente": "Instalaciones Ribera",
    "Ssl": true
  }
}
```

Si falta, la aplicación **arranca igual** y lo avisa por registro: los correos se encolan y quedan como
fallidos con el motivo escrito. Caerse al arrancar por no poder mandar un correo sería peor que no poder
mandarlo.

El píxel usa `Baja:UrlBase`, la misma dirección pública que los enlaces de baja: es la única que se sabe
que llega desde fuera. Sin ella no hay píxel, aunque el seguimiento esté encendido.

## Qué está probado y qué no

Probado, en verde:

* **Los huecos**, por los dos lados: un campo inventado se rechaza al guardar, y un campo sin valor se
  rechaza al enviar.
* **La doble comprobación de permiso**, incluido el caso que la justifica: encolar con permiso, retirarlo,
  y ver que el correo se **cancela sin llegar a hablar con el servidor de correo**.
* **Que sin base legal no se escribe ni para contestar**, y que el borrador lo explica.
* **Que el «para qué» lo manda la plantilla** y no el cliente.
* **El píxel**: devuelve GIF con token inventado, no se cachea, no pide sesión.
* **Que abrir no cuenta como contestar**, pidiendo el píxel de verdad y comprobando que la pregunta del
  repaso sigue ahí y ahora menciona las aperturas.
* **Que «Ya me contestó» apunta la respuesta** en la cronología y calla la pregunta para siempre.
* **La política de reintentos**, y que un fallo definitivo no gasta ni un reintento.
* **La pantalla entera** en un navegador de verdad: crear plantilla, rechazo del hueco inventado, vista
  previa con los huecos rellenos, el motivo cuando no se puede enviar, y el correo en cola apareciendo en
  el historial y en la cronología.

**No probado aquí**: que un correo llegue de verdad a una bandeja de entrada. Hace falta un servidor SMTP
con credenciales reales. Todo lo que depende de nosotros está cubierto —el texto, el permiso, el
reintento, el estado— y lo que queda es que el servidor conteste 250. Es la misma frontera que en
[avisos](avisos.md), y está dicha aquí en vez de darla por buena.
