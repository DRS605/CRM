# Módulo 17 — Campos propios

**Todo negocio tiene un dato que este CRM no tiene.** El nº de póliza, la potencia contratada, la fecha
de la última revisión, el convenio que le aplica, si la comunidad tiene ascensor. Es distinto en cada
negocio, así que no se arregla añadiendo campos de serie: cualquier lista que se escriba aquí se queda
corta en el siguiente cliente.

Y cuando el dato no cabe, no desaparece: se lleva en una hoja aparte. A partir de ese momento **la hoja
es la verdad y el CRM es una copia vieja**, y esa es la forma más común de que un CRM acabe abandonado.
No se abandona porque falte una función: se abandona porque hay que mirar dos sitios y uno de los dos
está siempre mal.

## La tensión, y cómo se resuelve

Este producto se vende diciendo «no rellenes campos, el sistema te dice qué hacer». Un módulo que sirve
para inventarse campos parece lo contrario, y podría serlo.

Se resuelve limitando **para qué** sirven, no cuántos hay:

- Un campo propio **se ve en la ficha** y se rellena cuando hace falta.
- **Sale en la copia de los datos**, la de la persona y la de la empresa.
- **Nunca es obligatorio.** No hay forma de marcarlo como tal, y no la va a haber.
- **No aparece en Hoy y no se pregunta en el repaso.** El sistema no va a pedir que se rellene nada.

Esa última línea es la que sostiene la promesa. Un campo obligatorio, o un aviso de «te falta rellenar
la potencia contratada», convertiría esto en el CRM del que la gente se escapa.

## Seis decisiones

### 1. Diez por objeto

Es un techo bajo y es el punto. Un CRM con cuarenta campos propios por objeto es una base de datos con
una interfaz encima: la ficha deja de leerse, nadie los rellena todos, y los que se quedan a medias
hacen dudar de los que sí están puestos. Diez obliga a elegir, y elegir es lo que hace que los diez
signifiquen algo.

El tope es **por ámbito**: diez de contacto y diez de cuenta. Compartirlo habría hecho que definir
campos de contacto gastara los de cuenta, que no es lo que nadie entiende por «diez por objeto».

### 2. Contacto y cuenta. **No oportunidad**

Y no es un olvido. Un campo propio solo sirve si hay una pantalla donde se ve y se rellena, y las
oportunidades no tienen ficha: son tarjetas en un tablero. Añadir el ámbito a la API sin la pantalla
habría dejado un campo que se puede definir y no se puede rellenar, que es la clase de media
funcionalidad que este proyecto ya se encontró cuatro veces —los tres papeles del módulo 1 sin forma de
llegar a ellos, las zonas de reparto, el rol de solo lectura, el segundo comercial—.

Ese mismo razonamiento **obligó a construir algo aquí**: las cuentas tampoco tenían ficha, solo una
tabla. Así que este módulo trae la ficha de cuenta —sus datos, sus contactos y sus campos propios—,
porque sin ella el ámbito de cuenta habría sido exactamente el error que se acaba de describir.

### 3. La clave no cambia nunca. **El invariante del módulo**

Cada campo tiene un nombre —para las personas— y una **clave** derivada de él: minúsculas, sin acentos,
guiones bajos. `Nº de póliza` da `n_de_poliza`. La clave es lo que va en la cabecera del CSV y lo que
usaría cualquier integración.

**Renombrar el campo no cambia la clave.** Si cambiara, corregir una tilde haría desaparecer sin aviso
la columna de un informe que alguien tiene montado fuera. El nombre es para las personas; la clave, para
las máquinas, y las máquinas no perdonan. La pantalla lo dice al renombrar: «Cambiado. La clave sigue
siendo n_de_poliza».

De ahí sale otra regla: **dos campos no pueden tener la misma clave** en el mismo ámbito. `Nº de póliza`
y `N de poliza` son dos nombres y una sola clave, y dejarlos pasar daría dos columnas iguales en el CSV
y dos filas casi iguales en la ficha. Lo comprueba el servicio y, detrás, un índice único en la base
para el caso de dos peticiones a la vez.

### 4. El tipo no se cambia

Cinco tipos y cerrados: texto, número, fecha, sí o no, y lista de opciones. No hay forma de cambiar el
tipo de un campo después de crearlo, y no es una limitación pendiente de arreglar: un campo de texto que
pasa a número deja sin sentido todos los valores que ya tenía, y convertirlos automáticamente sería
adivinar. Para cambiar de tipo se quita el campo y se crea otro, que además obliga a decidir qué pasa
con lo que había.

Las listas tienen entre **dos y doce** opciones. Con una no hay elección; con más de doce no es una
lista, es un texto libre disfrazado.

### 5. Un valor, una forma

Todo se guarda en **una sola columna de texto**, y el dominio normaliza al escribir:

| Tipo | Se acepta | Se guarda |
| --- | --- | --- |
| Número | `3,5` y `3.5` | `3.5` |
| Fecha | solo `aaaa-mm-dd` | `2026-03-12` |
| Sí o no | `sí`, `S`, `true`, `1`, `verdadero`… | `si` |
| Lista | la opción escrita como sea | **la opción tal como está en el campo** |

Lo último es lo que hace que el dato sirva para agrupar: si se guardara lo que teclearon, «Gas» y «gas»
serían dos grupos en cualquier recuento futuro. Y la fecha solo se acepta como la manda el navegador
porque aceptar `12/03/2026` obligaría a decidir si el 12 es el día o el mes, y esa decisión no se puede
acertar.

**El precio de la columna única, dicho claro:** no se puede filtrar ni ordenar por un campo propio,
porque «12» y «100» ordenados como texto salen al revés. Por eso este módulo no ofrece filtrar por
ellos. El día que alguien lo pida es una columna tipada más y una migración, no un rediseño.

Un valor vacío **no se guarda**: se borra la fila. Una fila con la cadena vacía y una fila que no existe
significan lo mismo para quien lee, y tener las dos formas de decir «no hay dato» garantiza que algún
día una pantalla enseñe «—» y otra enseñe nada.

### 6. Quitar una opción que alguien usa: no

Cambiar las opciones de una lista se rechaza si algún valor guardado se quedaría fuera, y se dice
cuántas fichas hay que arreglar antes. Dejarlo pasar habría dejado valores que la ficha enseña sin poder
cambiar y un grupo fantasma en cualquier recuento. Añadir opciones, en cambio, no molesta a nadie.

Y borrar un campo **se lleva sus valores**, diciendo cuántos antes y cuántos después. Dejarlos habría
sido más prudente en apariencia y peor de verdad: sin el campo no se sabe qué significaban ni de qué
tipo eran, así que serían datos que nadie puede leer ni borrar —y si el campo era de un contacto, datos
personales huérfanos—.

## Quién puede qué

| | Definir campos | Rellenar valores | Ver valores |
| --- | --- | --- | --- |
| Propietario | sí | sí | sí |
| Comercial | **no** | sí | sí |
| Solo lectura | no | no | sí |

Definir va con `empresa.ajustes` y rellenar con `contacto.gestionar`, y la línea está donde está por un
motivo: **definir un campo cambia la ficha de todos los compañeros**, y eso es configuración. Rellenarlo
es un dato de la ficha, como el teléfono.

Quien no puede rellenar ve el dato en una casilla desactivada, no escondida: el papel de solo lectura
existe para leer.

## La supresión

`campos.valor` es, probablemente, **la tabla con los datos más sensibles del sistema**, y no por lo que
guarda de serie sino por lo que no se sabe: es donde una empresa mete lo que este CRM no tiene, y eso
puede ser un DNI, una matrícula o una dirección. Se trata como lo más sensible, no como lo menos.

Así que entra en la supresión del artículo 17 desde el primer día —las dos ramas, contacto y empresa— y
en las dos exportaciones. El valor lleva el **ámbito copiado** del campo a propósito: borrar los valores
de un contacto es una búsqueda por `entidad_id` sin cruzar con la tabla de campos, y en el camino de una
supresión no se juega con las uniones.

La prueba que recorre todas las columnas de la base buscando el identificador después de borrarlo
—`Borrar_un_contacto_no_deja_ni_un_rastro_suyo_en_ninguna_tabla`— ahora **rellena un campo propio antes
de borrar**. Sin eso, el barrido habría pasado por no haber creado nada, que es la forma más silenciosa
de que una red de seguridad no sirva; se comprueba explícitamente que el rastro estaba.

## Aislamiento

Las dos barreras, como todo el sistema: filtro global de EF en las dos tablas y `ENABLE` + `FORCE ROW
LEVEL SECURITY` en `campos.campo` y `campos.valor`. La definición va aislada aunque no lleve datos de
personas: **los nombres de los campos que se inventa una empresa dicen a qué se dedica y cómo trabaja.**

## Lo que no hace

- **Filtrar o segmentar por un campo propio.** Dicho arriba, con el motivo.
- **Campos obligatorios.** Nunca.
- **Campos calculados.** Sería un lenguaje de fórmulas, y eso es otro producto.
- **Importar campos propios desde el CSV.** El importador conoce los campos de serie; hacer que
  descubriera columnas nuevas y las creara solo habría convertido una errata de cabecera en un campo
  propio para siempre.
- **Campos por rol o por equipo.** Un campo es de la empresa. Que un dato exista o no según quién mira
  es la clase de cosa que nadie consigue explicarse después.
