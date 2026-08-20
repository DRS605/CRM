# Módulo 11 — Webhooks

**Estado**: terminado · **Proyecto**: `Matchketing.Webhooks` · **Esquema**: `webhooks`

Avisamos a otro sistema cuando pasa algo aquí. El caso que justifica el módulo: **oportunidad ganada
→ pedido en el ERP, sin teclearlo dos veces.**

## Cinco eventos, y son cinco a propósito

Un catálogo de cuarenta eventos parece generosidad y es lo contrario: nadie sabe a cuál suscribirse,
la mitad no se emiten nunca, y cada uno es un sitio más por donde se puede escapar un dato. La regla
para entrar en la lista es que **otro sistema haga algo distinto al recibirlo**. «Se ha editado un
contacto» no la pasa.

| Evento | Cuándo | Para qué |
| --- | --- | --- |
| `lead.creado` | Se crea un contacto | Avisar a un comercial por otro canal |
| `oportunidad.movida` | Cambia de etapa | El pulso del embudo en un cuadro de mando ajeno |
| `oportunidad.ganada` | Venta cerrada | **El enlace con el ERP** |
| `oportunidad.perdida` | Venta perdida, con motivo | Análisis de por qué se pierde |
| `contacto.baja` | Alguien se da de baja | Dejar de escribirle desde el sistema de correo |

El nombre en texto es **contrato público**: va en el JSON, en la cabecera y en la columna `tipos` de la
base. No se cambia sin romperle la integración a alguien.

`contacto.baja` es el único que se puede llamar obligatorio: una baja que no llega al sistema que
manda los correos es una baja que no existe, y eso ya no es un fallo técnico.

## Qué viaja, y qué no

**El webhook dice qué ha pasado y a quién apunta. Ni teléfonos, ni correos, ni texto libre escrito
por personas.**

El motivo es que la URL la elige el cliente, y muchas veces no es un servidor suyo sino una plataforma
de automatización que guarda cada carga útil que recibe, para siempre y sin que nadie vuelva a mirarla.
Un teléfono que se escapa por ahí se ha escapado por nuestra culpa, no por la de quien montó el flujo.
Con el identificador y la API se puede pedir el resto cuando de verdad haga falta; lo que se manda sin
pensar no se puede recuperar.

Nombre y origen sí van: sin ellos el evento no sirve para nada y obligaríamos a una llamada de vuelta
para cualquier cosa, que es la forma segura de que nadie lo use.

**Una excepción, razonada:** `contacto.baja` lleva el correo. El propósito exacto de ese evento es que
otro sistema deje de escribir a esa dirección, y allí la clave es la dirección, no nuestro
identificador. Exigir una llamada a la API para cumplir una obligación legal es peor que mandar el dato
que la cumple.

El sobre es siempre el mismo:

```jsonc
{
  "id": "8f14e45f-…",              // identificador de esta entrega: con él se descartan repetidos
  "tipo": "oportunidad.ganada",
  "ocurridoEn": "2026-08-21T16:00:00+00:00",
  "empresaId": "f6059b3d-…",
  "datos": { "oportunidadId": "…", "contactoId": "…", "importe": 42000.00 }
}
```

## La firma

Sin firma, un webhook es una URL a la que **cualquiera** puede hacer un POST. Si eso desemboca en
«crear el pedido en el ERP», el agujero no es de quien recibe: es nuestro, por no haberle dado con qué
comprobarlo.

```
X-Matchketing-Firma: t=1787227200,v1=<hex de 64>
```

Se firma con HMAC-SHA256 sobre **`t.cuerpo`**, con el secreto de la suscripción. Se copia el formato
que ya usa medio internet para que quien lo reciba lo reconozca sin leerse esto.

Cómo se comprueba, que es lo que hay que mandarle a quien monte la integración:

```python
esperado = hmac.new(secreto.encode(), f"{t}.{cuerpo}".encode(), hashlib.sha256).hexdigest()
if not hmac.compare_digest(esperado, v1):      # comparación en tiempo constante
    return 401
if abs(time.time() - int(t)) > 300:            # cinco minutos de tolerancia
    return 401
```

Dos detalles que parecen de adorno y no lo son:

* **La marca de tiempo va dentro de lo firmado.** Si solo se firmara el cuerpo, quien interceptara una
  entrega podría reenviarla mañana igual de válida —«oportunidad ganada» dos veces— y la firma seguiría
  cuadrando. Firmando `t.cuerpo`, cambiar la `t` invalida la firma. Y por eso también hace falta la
  ventana de cinco minutos: sin ella, la firma es válida para siempre.
* **La comparación tiene que ser en tiempo constante.** Un `==` de cadenas se rinde en el primer byte
  distinto, y ese tiempo se puede medir para adivinar la firma byte a byte.

El secreto se enseña **una sola vez**, al crear la suscripción. Después solo se puede rotar, y rotar
corta en seco: lo que salga desde ese momento va firmado con el nuevo.

## Cómo se garantiza que no se pierde un evento

Una fila, no una llamada HTTP en el momento del cambio. Si al ganar una oportunidad se hiciera el POST
allí mismo:

* una URL lenta dejaría al comercial mirando una rueda por algo que no le importa;
* un fallo de red perdería el evento para siempre, sin rastro;
* y si la transacción se deshiciera después, ya habríamos avisado de una venta que no existe.

La fila se escribe **en la misma transacción** que el cambio de negocio. Es el patrón del buzón de
salida, y es la única forma conocida de que «pasó» y «se avisó» no puedan separarse.

La consecuencia hay que decirla en voz alta: la entrega es **al menos una vez**, nunca exactamente una
vez. Un reintento tras un tiempo de espera agotado puede llegar dos veces. Por eso cada entrega lleva
su identificador estable en la cabecera `X-Matchketing-Entrega` y en el cuerpo, y **el reintento
conserva el mismo**: si cambiara, la deduplicación del otro lado no serviría de nada.

El orden **no** está garantizado. Usa `ocurridoEn` si te importa.

### Reintentos

Seis esperas: 1 min, 5 min, 25 min, 2 h, 10 h, 24 h. Siete intentos que abarcan más de un día y medio,
para que un despliegue del otro lado, una noche de mantenimiento o un fin de semana caben dentro.
Reintentar diez veces en un minuto y rendirse es lo mismo que no reintentar.

Solo un **2xx** cuenta como entregado. Todo lo demás se reintenta, **incluido el 404**: es la
diferencia con los [avisos push](avisos.md), donde un 404 significa que el móvil ya no existe; aquí
casi siempre significa que el servicio del otro lado está a medio desplegar.

Y no se sigue una redirección. Un 301 hacia otro dominio convertiría nuestra petición firmada, con el
cuerpo entero dentro, en una petición a un sitio que el cliente nunca configuró.

### Se apaga solo

A las **cinco entregas agotadas seguidas** la suscripción se apaga y se guarda el motivo, para poder
leerlo en la pantalla en vez de adivinarlo. Un endpoint que lleva dos días devolviendo 500 no va a
arreglarse porque insistamos, y seguir insistiendo tiene dos costes reales: la tabla de entregas crece
sin parar, y desde el otro lado nuestro reintento cada minuto contra una URL muerta se parece bastante
a un ataque.

Una entrega buena borra el historial: cuentan los fallos **seguidos**, así que un endpoint que falla un
martes al mes no acaba apagado por acumulación.

No se reactiva sola. Si se apagó porque la URL estaba mal, hay que arreglar la URL; volver a intentarlo
por nuestra cuenta solo repetiría el fallo cinco veces más.

## Por qué cuelga de los eventos de dominio

Los eventos de dominio existían desde el primer módulo y **no los consumía nadie**: los agregados los
acumulaban y EF los ignoraba. Este módulo es su primer consumidor.

`DespachadorEventos` se engancha en `ContextoMatchketing.GuardarCambiosAsync`, que es el único guardado
de la aplicación. La consecuencia concreta: **una oportunidad ganada desde el repaso emite igual que una
ganada desde el tablero**, sin que el repaso sepa que existen los webhooks. Colgarlo de los endpoints
habría dejado fuera la mitad de los caminos, y nadie lo habría notado hasta que un cliente preguntara
por qué a veces no llega. Hay una prueba dedicada a eso: `Ganar_desde_el_repaso_encola_igual_que_ganar_desde_el_tablero`.

Va **antes** de `SaveChangesAsync` y no en un interceptor: así las filas de entrega entran en el mismo
guardado que el cambio que las provocó, sin depender de en qué momento EF recoge los cambios.

## API

| Método | Ruta | Permiso | Qué hace |
| --- | --- | --- | --- |
| GET | `/webhooks/eventos` | `empresa.ajustes` | El catálogo. |
| GET | `/webhooks` | `empresa.ajustes` | Los de la empresa. **Nunca devuelve el secreto.** |
| POST | `/webhooks` | `empresa.ajustes` | Da de alta y devuelve el secreto, la única vez. |
| GET | `/webhooks/{id}/entregas` | `empresa.ajustes` | Los últimos 20 intentos. Sin el cuerpo. |
| PUT | `/webhooks/{id}` | `empresa.ajustes` | Cambia descripción y eventos. La URL no: para eso se crea otro. |
| POST | `/webhooks/{id}/secreto` | `empresa.ajustes` | Secreto nuevo. El anterior deja de valer al momento. |
| POST | `/webhooks/{id}/reactivar` | `empresa.ajustes` | Vuelve a encender uno apagado. |
| DELETE | `/webhooks/{id}` | `empresa.ajustes` | Lo borra. Lo que quedara en cola se abandona sin intentarse. |

Todo el grupo pide `empresa.ajustes`: un webhook saca datos de la empresa hacia fuera, así que no es
una pantalla de consulta, es una decisión de administración.

El historial **no** devuelve el cuerpo, a propósito: una baja lleva el correo dentro y esa pantalla la
puede tener abierta cualquiera con permiso de ajustes.

## Modelo

```
webhooks.suscripcion
  id, empresa_id
  url            -- solo https, hasta 500
  secreto        -- whsec_ + 32 bytes en hexadecimal
  descripcion
  tipos          -- «lead.creado,oportunidad.ganada»: nombres, no números
  activa, motivo_apagado, fallos_seguidos
  creada_en, ultima_entrega_en

webhooks.entrega
  id             -- el que viaja en el cuerpo y en la cabecera
  empresa_id, suscripcion_id, tipo
  cuerpo         -- el JSON congelado: es lo que se firma
  estado, intentos, proximo_intento_en
  creada_en, entregada_en, ultimo_codigo, ultimo_fallo
```

Las dos con RLS forzada. Los `tipos` van en una columna de texto y no en una tabla aparte: son cinco
valores como mucho, nunca se consultan por separado, y una tabla de unión costaría un `JOIN` en el
camino caliente. Y se guardan con su nombre público en vez de con el número porque cuando un webhook
no dispara lo primero que se hace es mirar la fila, y `3,5` obliga a ir a buscar el enumerado.

`ultimo_fallo` nunca lleva el cuerpo de la respuesta ajena: un error de otro servidor puede traer
dentro una traza, una consulta o una credencial, y acabaría en nuestra tabla y en nuestra pantalla sin
que nadie lo hubiera decidido.

## Qué está probado

* **La firma**, rehaciéndola desde cero como la rehará quien la reciba, más los dos ataques que
  importa: tocar el cuerpo y reenviar una entrega vieja.
* **La política de reintentos** entera: el escalado, el agotamiento, y que abarque más de un día.
* **El apagado automático** a las cinco y que una entrega buena borre el historial.
* **Las cabeceras y el mapeo de cada código** de `EnviaWebhook`, contra un receptor de mentira.
* **Que no se guarda nada del cuerpo de la respuesta ajena.**
* **El contenido de cada evento**, leyendo la fila de la base: que un lead no lleva teléfono ni correo,
  que una baja sí lleva el correo, y que un movimiento dice de dónde a dónde.
* **Que ganar desde el repaso emite igual que ganar desde el tablero.**
* **Que sin ningún webhook dado de alta no se escribe nada**: el coste para quien no los usa es cero.

Lo que **no** se ha probado aquí: una entrega contra un servidor real por internet. Hace falta una URL
https de verdad al otro lado. Todo lo que depende de nosotros —la firma, las cabeceras, el cuerpo, los
reintentos— está cubierto; lo que queda es que el otro lado conteste 200.
