# Módulo 6 — Captación

El formulario que se pega en la web del cliente, y el flujo que cumple el criterio de aceptación del
producto: **de la web a una tarjeta en Hoy, sin que nadie lo toque**.

Medido en las pruebas de integración: **~500 ms** de extremo a extremo. El objetivo eran 60 segundos.

## El flujo estrella

`POST /f/{clave}` es público —quien rellena el formulario en una web no está autenticado— y hace
todo esto en una petición:

1. Busca el formulario por su clave. **La clave dice de qué empresa es**, y a partir de ahí se fija
   el inquilino de la petición.
2. **Comprueba el consentimiento.** Sin la casilla marcada no entra nada, ni el contacto.
3. Crea el contacto con el origen del formulario, y **lo guarda ya**: es el ancla de todo lo que
   viene después.
4. Guarda el **consentimiento con su prueba**: texto aceptado, canal, IP y navegador.
5. Guarda el **envío entero** en JSON, tal cual llegó.
6. Escribe en la **cronología** lo que la persona pidió.
7. Registra la **señal** `FormularioEnviado` (35 puntos) y recalcula el Match.
8. **Asigna** al comercial con mejor match y deja escrito por qué.
9. Crea la tarea de **primera llamada**.

Si no hay comerciales, el lead entra igual: perderlo por no saber a quién dárselo sería el peor
desenlace.

## El script de una línea

```html
<script src="https://tudominio/f/CLAVE/script.js" async></script>
```

Se pega donde tenga que salir el formulario. **Sin librerías, sin iframe y sin estilos que peleen**
con los de la web del cliente: se inserta justo donde está la etiqueta.

El script lleva dentro los campos que pide ese formulario y su texto de consentimiento, así que la
web del cliente no tiene que saber nada.

## Decisiones que conviene conocer

**La clave es la credencial.** 22 caracteres aleatorios de un alfabeto sin `l`, `o`, `0` ni `1`, para
que no se confunda al dictarla por teléfono. Es pública por diseño: va en el `src` del script.

**`captacion.formulario` no lleva RLS**, y es deliberado: hay que poder leerlo *antes* de saber de
qué empresa es. Lo protege la clave, y el filtro global de EF sigue aplicando a todo el acceso
autenticado. Las tablas que se escriben *después* de conocer la empresa —`envio_formulario`,
`consentimiento`— sí llevan política.

**CORS abierto, pero solo en `/f`.** El script corre en el dominio del cliente, que es otro origen;
sin CORS el navegador bloquearía el envío y la captación no funcionaría fuera de nuestro dominio. No
sabemos en qué dominio está cada cliente y pedirle que lo registre sería una fricción absurda. Lo
que protege el endpoint es la clave, no el origen, y no hay credenciales de por medio. **El resto de
la API sigue siendo de mismo origen**, y hay un test que lo comprueba.

**`IContextoEmpresaPublico`** existe para un solo caso: fijar la empresa sin token en esta entrada.
En cualquier endpoint autenticado la empresa sale del JWT y solo del JWT (invariante T2).

**La página de gracias solo admite `http` y `https`.** Acaba en un `location.href` del navegador del
visitante: un `javascript:` ahí sería un agujero abierto de par en par.

## El consentimiento

No es un `bool`. `Consentimiento` { contacto, **finalidad**, **base legal**, canal, **texto
aceptado**, IP, navegador, otorgado en, retirado en }. Si algún día hay que demostrar que ese correo
se podía enviar, un booleano no demuestra nada.

Dos finalidades distintas y dos registros distintos: **atender una solicitud no es lo mismo que
poder mandar promociones**. Un consentimiento sirve para lo que dice y nada más.

Vive en el módulo **Cumplimiento**, que aquí nace con solo esta pieza; la baja, la exportación, el
borrado y la retención llegan en el módulo 8.

## Seguimiento web

`POST /f/{clave}/visita` registra la señal `VisitaWeb` de un contacto **ya conocido**, el que volvió
a la web después de dejarnos sus datos. **No se identifica a visitantes anónimos**: eso es lo que
hace el *buyer intent* de HubSpot y es justo lo que decidimos no hacer.

## API

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| `GET` `POST` | `/formularios` | `formulario.gestionar` | Lista y crea |
| `PUT` `DELETE` | `/formularios/{id}` | `formulario.gestionar` | Actualiza y desactiva |
| `GET` | `/f/{clave}` | público | Definición del formulario, para pintarlo |
| `GET` | `/f/{clave}/script.js` | público | El script de una línea |
| `POST` | `/f/{clave}` | público | **Entrada de leads** |
| `POST` | `/f/{clave}/visita` | público | Visita de un contacto conocido |

## Interfaz

Los formularios viven **dentro de Ajustes**, no en una sexta opción de menú: son configuración, y la
regla de las cinco opciones no se rompe por esto. Se ve el nombre, la clave, cuántos envíos lleva, el
código para copiar y **una vista previa de cómo quedará** con sus campos reales.

## Tests

- **Unitarios (19)**: clave de longitud fija, 50 claves sin repetir, alfabeto sin caracteres
  confundibles, nombre y **texto de consentimiento obligatorios**, página de gracias que acepta
  `http`/`https` y **rechaza `javascript:`**, normalización del origen; y del consentimiento: la
  prueba que guarda, canal obligatorio, retirada e imposibilidad de retirarlo dos veces, y que las
  dos finalidades son registros distintos.
- **Integración (13)**: definición pública, clave inexistente, **sin consentir no entra ni el
  contacto**, el lead completo con su tiempo medido, mensaje y asignación en la cronología, el
  consentimiento con su navegador guardado, el envío contado, formulario desactivado que deja de
  aceptar, **el script se sirve con sus campos**, **CORS abierto en `/f` y cerrado en el resto**,
  visita web que suma señal, aislamiento entre empresas y lead sin medio de contacto rechazado.
