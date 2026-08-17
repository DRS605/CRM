# Módulo 8 — Cumplimiento

**Estado**: terminado · **Proyecto**: `Matchketing.Cumplimiento` · **Esquema**: `cumplimiento`

Los derechos de las personas cuyos datos están aquí dentro, hechos código.

Un CRM es una base de datos de personas que no la han pedido. Este módulo existe para que la respuesta
a «¿le puedo mandar esto?» no sea nunca «supongo que sí», y para que cuando alguien diga «borradme»
se pueda hacer en un clic en vez de en una reunión.

## Las tres invariantes

* **G1 — sin base legal vigente no se envía nada.** La comprobación la hace el servidor y devuelve el
  motivo, no un booleano. Hay un endpoint para que cualquier integración pueda preguntar antes.
* **G2 — la baja es irreversible desde nuestro lado.** Solo el interesado puede volver, y no por
  aquí. Ni siquiera se le puede apuntar un permiso nuevo: eso convertiría la baja en un adorno.
* **G3 — borrar es borrar.** La supresión quita filas de las tablas; no pone `activo = false`. Lo
  único que sobrevive es una línea de auditoría con cifras y sin un solo dato personal.

## Consentimiento: no es un `bool`

`Consentimiento` guarda **finalidad, base legal, canal, el texto exacto que se aceptó, la IP y el
agente**. Si algún día hay que demostrar que a esa persona se le podía escribir, un booleano no
demuestra nada.

La distinción que más cambia el día a día es la **finalidad**:

* `AtenderSolicitud` — rellenó un formulario pidiendo un presupuesto. Puedes contestarle.
* `Comercial` — te dio permiso para mandarle ofertas.

Son cosas distintas y el sistema no las confunde. Un lead que entra por un formulario recibe
automáticamente el primero y **no** el segundo: pedir un precio no es apuntarse a una lista de correo.
Hay un test de integración que recorre ese flujo entero para comprobarlo.

La base legal admite `InteresLegitimo` y `Contrato`, no solo consentimiento. Un cliente de hace tres
años al que le vendiste una caldera es un caso legítimo, y forzar a que todo fuera «consentimiento»
habría empujado a la gente a apuntar consentimientos que nunca existieron.

## La baja de un clic

Un token **firmado, no guardado**: dentro van la empresa y el contacto, y detrás una firma HMAC-SHA256
con un secreto del servidor.

**No caduca, a propósito.** Un enlace de baja muerto es peor que no poner ninguno, porque convierte
una baja de un clic en una reclamación. Sin tabla no hay nada que expirar ni que limpiar, y el enlace
del correo de 2026 sigue valiendo en 2029. La única palanca para invalidarlos todos es cambiar el
secreto, y es la correcta: nunca hace falta revocar una baja concreta.

El secreto es **distinto** al del JWT. Los tokens de sesión caducan en horas y su clave puede rotarse
sin avisar; compartirla habría atado las dos rotaciones, y la primera rotación del JWT habría matado
todos los enlaces de baja emitidos.

### El GET pregunta; solo el POST da de baja

`GET /b/{token}` pinta una página con un botón. `POST /b/{token}` es lo que ejecuta la baja.

Esto no es puntillismo con los verbos HTTP. Los antivirus de correo, las vistas previas de los
mensajeros y el prefetch del navegador **abren los enlaces de un correo sin que nadie los pulse**. Un
GET que diera de baja daría de baja a gente que jamás lo pidió, y por G2 eso no se puede deshacer.
Hay un test de integración que abre el enlace con GET y comprueba que el contacto sigue activo.

La página se sirve entera desde el endpoint, con sus estilos en línea y sin una sola petición fuera:
es la única pantalla del sistema que tiene que funcionar cuando todo lo demás esté caído, porque del
otro lado hay alguien que ya está molesto. Y va con CORS abierto, como la captación, porque el
navegador la abre desde el gestor de correo.

La baja **retira todos los consentimientos vigentes**, no solo marca el contacto. Dejar uno vigente
significaría que el siguiente envío encontraría base legal y saldría. Y es **idempotente**: quien
pulsa dos veces el enlace no ve un error, porque pidió una cosa y esa cosa está hecha.

## Acceso, portabilidad y supresión

| Derecho | Cómo |
| --- | --- |
| Acceso y portabilidad | `GET /cumplimiento/contactos/{id}/exportar` → JSON con **todo**. |
| Supresión | `DELETE /cumplimiento/contactos/{id}` → borra filas de nueve tablas. |
| Portabilidad de la empresa | `GET /cumplimiento/empresa/exportar`. |
| Cierre de cuenta | `POST /cumplimiento/empresa/borrar`, escribiendo el nombre exacto. |

La exportación incluye las cosas que uno preferiría no mostrar: la puntuación Match que le hemos
puesto, los motivos con los que se calculó y las notas internas del comercial. Son sus datos; que
resulten incómodos no los convierte en nuestros.

El borrado de un contacto **elimina el envío de formulario entero**, no le quita el `contacto_id`.
Dentro lleva el nombre, el correo y el mensaje que escribió la persona: desvincularlo dejaría el dato
personal donde estaba y solo habría escondido a quién pertenece.

Cerrar la cuenta pide **teclear el nombre de la empresa**. Es la única operación del sistema que no
tiene vuelta, y un «¿seguro?» con un botón se pulsa sin leerlo. El apunte de auditoría se escribe
*después* del borrado, y por eso sobrevive: es lo único que queda, y dice cuándo se fue y cuántas
filas se llevó.

## Retención

Los leads que siguen siendo lead, que nadie ha tocado y que nunca tuvieron una oportunidad se borran
al cumplir el plazo. Por defecto **24 meses**: bastante para que una oportunidad lenta madure, poco
para acabar con una base de datos de gente que preguntó un precio hace media década.

El mínimo configurable son **3 meses**. Por debajo, el sistema borraría leads que todavía se están
trabajando, y un CRM que se come los leads no es un CRM.

Qué se mira para decidir que un lead está muerto:

* no es cliente,
* no tiene **ninguna** oportunidad, ni abierta ni cerrada,
* su `actualizado_en` es anterior al límite,
* y no tiene ninguna actividad posterior al límite.

Se mira la última actividad, no la fecha de alta: un lead de hace tres años al que se llamó el mes
pasado se está trabajando. Los que pidieron la baja **también** entran, y con más razón: conservar dos
años el teléfono de quien dijo que no quería saber nada es exactamente lo que no toca.

## Un puerto para no romper la arquitectura

Los derechos del RGPD **cruzan los siete módulos**: los datos de una persona están repartidos entre
contactos, embudo, tareas, match y captación. Cumplimiento no puede referenciarlos todos para llegar
a ellos.

Así que declara `IAlmacenPersonal` —reunir, borrar, contar, dar de baja— y la infraestructura lo
implementa en `AlmacenPersonal`, la única clase del sistema que conoce todas las tablas donde puede
haber datos de una persona. La frontera queda en su sitio y la responsabilidad, en el módulo al que le
toca. La marca de baja se aplica ahí y no aquí porque la invariante «de la baja no se vuelve» es del
agregado `Contacto`.

## API

| Método | Ruta | Permiso | Qué hace |
| --- | --- | --- | --- |
| GET | `/cumplimiento/contactos/{id}` | `contacto.leer` | Panel de privacidad: estado, explicación, permisos y enlace de baja. |
| POST | `/cumplimiento/contactos/{id}/consentimientos` | `contacto.gestionar` | Apunta un permiso con su prueba. |
| DELETE | `/cumplimiento/contactos/{id}/consentimientos?finalidad=` | `contacto.gestionar` | Lo retira. Inmediato. |
| GET | `/cumplimiento/contactos/{id}/puede-enviar?finalidad=` | `contacto.leer` | **G1**: sí o no, con el motivo. |
| GET | `/cumplimiento/contactos/{id}/exportar` | `datos.exportar` | Todo lo que hay de esa persona. |
| DELETE | `/cumplimiento/contactos/{id}` | `empresa.ajustes` | Supresión real. |
| GET | `/cumplimiento/empresa/exportar` | `empresa.ajustes` | Copia completa de la empresa. |
| POST | `/cumplimiento/empresa/borrar` | `empresa.ajustes` | Cierre de cuenta con confirmación. |
| POST | `/cumplimiento/retencion` | `empresa.ajustes` | Aplica la retención ya. |
| PUT | `/empresas/activa/ajustes-retencion` | `empresa.ajustes` | Cambia el plazo (3–120 meses). |
| GET | `/b/{token}` | — (público) | Página de baja. **Pregunta**, no da de baja. |
| POST | `/b/{token}` | — (público) | Confirma la baja. |

`puede-enviar` responde **200 con `puede: false`**, no un error: quien pregunta antes de enviar está
haciendo lo correcto y su petición no ha fallado.

Borrar un contacto exige `empresa.ajustes`, no `contacto.gestionar`: un comercial gestiona contactos,
pero eliminar a una persona de todas las tablas es otra clase de decisión.
