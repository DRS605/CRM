# Módulo 13 — Automatización

**Estado**: terminado · **Proyecto**: `Matchketing.Automatizacion` · **Esquema**: `automatizacion`

«Si pasa esto, haz esto otro.» Un disparador, hasta tres condiciones y hasta cuatro acciones.

> **Nota de alcance.** Esto es **F2**, no MVP. La tabla de
> [`hubspot.md`](../producto/hubspot.md) lo marcaba como MVP y estaba mal: tanto
> [`diseno-tecnico-funcional.md`](../producto/diseno-tecnico-funcional.md) —el documento que manda— como
> [`tareas-hoy.md`](tareas-hoy.md) dicen que las configurables son F2 y que en el MVP hay **una sola,
> fija**: lead nuevo → asignar por match → tarea de primera llamada. Corregido allí. El orden no cambia:
> el documento de diseño pone las automatizaciones justo después del correo conectado.
>
> Ese mismo documento dice que lo de F2 entra «solo si lo piden clientes reales», y todavía no hay
> ninguno. Se ha construido por decisión del dueño del producto, y queda dicho.

## Sin lienzo de ramas, y eso es el producto

Una regla se lee de un tirón y en castellano:

> Si pasa «oportunidad.ganada» y provincia es «Valencia» y importe es mayor que «10000», entonces crear
> la tarea «Pedir referencia» para dentro de 30 días, y apuntar «Cliente grande».

En cuanto hay ramas hay que dibujar, y en cuanto hay que dibujar hace falta alguien que sepa dibujarlo:
es la funcionalidad que convierte una herramienta que se entiende sola en una que necesita un
consultor. Por eso:

* **Hasta tres condiciones, y se cumplen todas.** No hay «o». Mezclar «y» con «o» pide paréntesis, y los
  paréntesis piden un lienzo. Quien quiera «Valencia o Alicante» hace dos reglas, que además se leen
  mejor. El mensaje de error lo dice: *«si necesitas más, probablemente necesitas dos reglas»*.
* **La regla entera se enseña leída**, tanto al crearla como en la lista. Si no se entiende de un tirón,
  nadie debería encenderla.

## Cuatro acciones, y ninguna toca el embudo

| Acción | Qué hace |
| --- | --- |
| Crear tarea | Título y a los N días. La más útil, con diferencia. |
| Asignárselo a un comercial | Cambia el propietario del contacto. |
| Mandar un correo | Con una plantilla, **pasando por el permiso**. |
| Apuntar una nota | En la cronología. La más aburrida y la que más se usa. |

Lo que se ha quedado fuera es tan importante como lo que está: **mover una oportunidad de etapa, cambiar
el estado de un contacto o cerrar una venta**. Todas cambian el embudo a espaldas del comercial, y un CRM
que mueve tus oportunidades solo es un CRM del que dejas de fiarte.

Tampoco hay «avisar al móvil». El módulo de [avisos](avisos.md) tiene una regla —uno a la semana y nada
más— y dejar que las reglas manden avisos la rompería el primer día.

## Las cinco cosas que la hacen fiable

### 1. Nace apagada

Y **cambiarla la vuelve a apagar**. Lo que hace no se deshace: las tareas creadas están creadas y los
correos mandados están mandados. Una regla que empieza a disparar en el mismo segundo en que se guarda no
da tiempo a leerla, y un cambio a medias que siga disparando es la forma más rápida de mandar cien
correos que nadie quería.

### 2. Se puede probar sin encenderla

`GET /reglas/{id}/ensayo?contactoId=…` dice si aplicaría y qué haría, **sin hacerlo**. Es la única forma
de probar una regla: encenderla «para ver qué pasa» es exactamente lo que no se debe hacer.

El ensayo funciona con la regla apagada —la pregunta es «¿cumpliría?», no «¿está funcionando?»— y avisa
de dos cosas que son el 90 % de los «mi regla no funciona»: qué condición concreta no se cumple, y si ya
actuó sobre ese contacto.

### 3. Actúa una sola vez por sujeto, para siempre

La garantía es un **índice único** en `(regla_id, sujeto_id)`, no un `if`: un `if` no protege de dos
procesos guardando a la vez, y el precio de equivocarse es mandar dos correos o crear dos tareas.

### 4. Una regla no dispara a otra

Los eventos que generan las acciones **se descartan**. Sin eso, dos reglas podrían peloteárselos entre
ellas para siempre. Es seguro precisamente porque ninguna acción toca el embudo: si algún día se añade
una que sí, esta decisión hay que revisarla.

### 5. Todo lo que hace queda apuntado

`automatizacion.ejecucion` guarda qué hizo, sobre quién y cuándo, ya escrito en castellano —incluido lo
que **no** pudo hacer y por qué—. Un comercial que se encuentra una tarea que no creó tiene que poder
averiguar de dónde salió; una automatización que no se puede auditar es una automatización que se acaba
apagando por si acaso. Y todo lo que apunta en la cronología dice «Regla automática: …».

Crear, cambiar, encender y borrar una regla van también al registro de [auditoría](auditoria.md).

## El permiso sigue siendo el permiso

La acción de mandar un correo pasa por `ServicioCorreo`, que comprueba el consentimiento igual que un
correo escrito a mano —y lo vuelve a comprobar justo antes de que salga—. **Una automatización no es una
excusa para saltarse el RGPD.**

Y una acción que no se puede hacer **no cancela las demás**. El caso real: una regla que manda un correo
y crea una tarea, sobre alguien que no ha dado su consentimiento. El correo no sale —y es correcto que no
salga— pero la tarea de llamarle sí se crea, que es justo cuando más hay que llamar.

## Cuándo se ejecutan: el fallo que costó encontrar

Las reglas cuelgan de los **eventos de dominio**, los mismos que los [webhooks](webhooks.md). Eso
significa que una oportunidad ganada dispara igual desde el tablero, desde el repaso o desde la API, sin
que ninguno de esos sitios sepa que existen las reglas.

Pero el momento **no** es el mismo que el de los webhooks, y ahí estaba el fallo:

| | Cuándo | Por qué |
| --- | --- | --- |
| Webhooks | **Antes** de `SaveChanges` | Sus filas de entrega son escrituras sueltas: así el buzón de salida y el hecho no se pueden separar. |
| Reglas | **Después** de `SaveChanges` | Sus acciones pasan por los servicios de contactos, correo y tareas, y esos servicios **cargan el contacto de la base**. |

Al principio las reglas iban donde los webhooks, antes de guardar. Con el contacto todavía sin guardar,
tres de las cuatro acciones fallaban en silencio: solo funcionaba crear una tarea, que es la única que no
consulta nada. Y solo con los disparadores de contacto, porque en los de oportunidad esa fila ya estaba
guardada. **No daba ningún error**: la regla decía que había actuado y en su registro ponía «no se pudo».

La primera versión de la prueba tampoco lo cazó, porque solo comprobaba que apareciera «no se pudo» —y
aparecía, por el motivo equivocado—. La prueba que lo encontró es
`Las_cuatro_acciones_funcionan_sobre_un_contacto_recien_creado`, que exige que las tres que sí deben
funcionar funcionen.

Cuando hay reglas que ejecutar, los dos guardados van **dentro de una transacción**: el cambio de negocio
y lo que provoca entran o no entran juntos. Cuando no hay ninguna regla encendida —el caso de casi todo
el mundo— se pregunta antes, se guarda una sola vez y no se abre nada.

## API

| Método | Ruta | Qué hace |
| --- | --- | --- |
| GET | `/reglas/catalogo` | Disparadores, campos, operadores y acciones. La pantalla se pinta de aquí. |
| GET | `/reglas` | Las reglas, las encendidas primero, cada una leída. |
| POST | `/reglas` | Crea una, **apagada**. Devuelve cómo ha quedado leída. |
| PUT | `/reglas/{id}` | La cambia **y la apaga**. |
| POST | `/reglas/{id}/encender?encender=` | La enciende o la apaga. |
| GET | `/reglas/{id}/ensayo?contactoId=` | Qué haría, **sin hacerlo**. |
| GET | `/reglas/{id}/ejecuciones` | Qué ha hecho y sobre quién. |
| DELETE | `/reglas/{id}` | La borra. Lo que ya hizo no se deshace. |

Todo el grupo pide `empresa.ajustes`: una regla hace cosas en nombre de la empresa sin que nadie las
pulse, así que no es una pantalla de trabajo.

## Modelo

```
automatizacion.regla
  id, empresa_id, nombre, disparador
  condiciones   -- JSON, hasta 3
  acciones      -- JSON, hasta 4
  activa, creada_en, ultima_vez_en, veces

automatizacion.ejecucion
  id, empresa_id, regla_id, sujeto_id, contacto_id
  que_hizo      -- en castellano, incluido lo que no pudo
  cuando_en
  unique (regla_id, sujeto_id)   -- la garantía de «una sola vez»
```

Las dos con RLS forzada. Condiciones y acciones van en JSON y no en tablas hijas: son siete filas como
máximo por regla, nunca se consultan por separado y siempre se leen con su regla. Lo que se pierde es
poder preguntar «qué reglas usan esta plantilla» en SQL; el día que haga falta, es lo primero que hay que
cambiar.

## Qué está probado

* **Las condiciones**, incluida la que más sorprende: «no es» se cumple cuando el dato **falta**, porque
  «si el sector no es hostelería» tiene que incluir a quien no tiene sector puesto.
* **Que no se puede guardar una regla que no se cumpliría nunca**: un importe con disparador de contacto,
  un «provincia mayor que», un motivo de pérdida sin pérdida. Es la validación que más tiempo ahorra:
  sin ella la regla se guarda, se enciende y no hace nada, y no hay forma de saber por qué mirándola.
* **Que nace apagada y que cambiarla la apaga.**
* **Que actúa una sola vez por sujeto**, y que sobre otro sujeto sí actúa.
* **Que una acción que falla no cancela las demás**, y que aun así queda apuntado.
* **Que ganar desde el repaso también dispara.**
* **Que una regla no dispara a otra.**
* **Que no puede mandar un correo sin permiso**, comprobando que falla **el correo y solo el correo**.
* **Que las cuatro acciones funcionan sobre un contacto recién creado** — la prueba que encontró el fallo
  del momento de ejecución.
* **La pantalla entera** en un navegador: crear, leerla en castellano, probarla en seco, encenderla,
  provocar el disparo creando un contacto, y ver la tarea en Hoy y el registro en la regla.
