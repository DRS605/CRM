# Módulo 5 — Match v1

**El corazón del producto.** Donde HubSpot te da una base de datos y tú decides qué hacer,
match.keting empareja y te propone la jugada. Y la explica: **ningún número sin motivo**.

## La puntuación

`Match = w × Encaje + (1 − w) × Momento`, con **w = 0,5** por defecto y ajustable por empresa desde
Ajustes.

### Momento — qué ha pasado últimamente

Cada señal aporta su peso **multiplicado por un decaimiento exponencial de semivida siete días**:
`peso × 0,5 ^ (días / 7)`. A la semana vale la mitad; a las dos, la cuarta parte. El interés caduca
solo y nadie tiene que acordarse de enfriar una lista a mano.

| Señal | Peso | Tope diario |
|---|---:|---:|
| Formulario enviado | 35 | 2 |
| Respuesta a un correo | 30 | 3 |
| Reunión realizada | 30 | 2 |
| Llamada contestada | 25 | 2 |
| Oportunidad abierta | 20 | 1 |
| Clic en un enlace | 15 | 3 |
| Correo abierto | 8 | 3 |
| Visita a la web | 6 | 5 |
| **Sin ninguna señal en 30 días** | **−20** | suelo en 0 |

**Todas las señales tienen tope diario.** Sin él, un robot que abre el mismo correo veinte veces —o
una importación que crea diez oportunidades de golpe— convertiría a un contacto cualquiera en el más
caliente de la lista. Que no cojan el teléfono **no** es señal: solo se registra la llamada
contestada.

### Encaje — cuánto se parece a quien ya te compra

Se calcula contra **tu** histórico de ganadas, no contra una plantilla:

| Factor | Peso |
|---|---:|
| El sector está entre los 3 con más ganadas | 30 |
| La provincia está entre las que tienen ganadas | 20 |
| El origen convierte por encima de tu media | 20 |
| El tamaño cae en el rango que sueles cerrar | 15 |
| Tienes su correo **y** su teléfono | 15 |

Un origen se considera bueno con **al menos tres cierres** y una conversión por encima de la media:
dos casualidades no son una tendencia.

**M2 — Sin veinte cierres, el Encaje es 50 y se dice.** *«Todavía sin histórico para calibrar el
encaje.»* Es preferible decir «no lo sé» a inventar un número.

## M1 — Ningún número sin motivo

Si no hay **ni un solo factor** que redactar, `Match` es `null` y la interfaz enseña un guion. Se
muestran hasta **tres motivos**, los que caben en una frase que se lea de un vistazo.

Y una regla que salió de una prueba: **un aviso tiene plaza reservada**. Si el contacto lleva dos
meses en silencio, eso sale aunque haya tres factores positivos que puntúen más alto. Esconder la
mala noticia detrás de tres buenas es justo el tipo de puntuación bonita e inútil que no queremos.

## El reparto lead ↔ comercial

Donde HubSpot reparte por turnos, aquí se reparte por afinidad real:

| Factor | Puntos |
|---|---:|
| Lleva esa provincia (`Membresia.Zonas`, se rellena en [Equipo](equipo.md)) | 30 |
| Su tasa de cierre en ese sector, normalizada | 0–30 |
| Carga: oportunidades abiertas frente al máximo del equipo, invertida | 0–20 |
| Velocidad: tiempo medio de primera respuesta, invertida | 0–20 |

Empate → gana **quien menos carga tiene**. Repartir trabajo también es repartir atención.

> **Nota**: el factor de la provincia no valió nada hasta el módulo 14. `Membresia.Zonas` existía desde
> el módulo 1 y **no había forma de rellenarlo**, así que el reparto por zona repartía sin que nadie
> tuviera zona, y los otros tres factores decidían siempre. Se rellena en Ajustes › Equipo.

**M4 — A quien acaba de entrar no se le penaliza por no tener histórico**: arranca con la media del
equipo. Si se le castigara por no tener datos, nunca recibiría un lead y nunca los tendría.

`POST /match/contactos/{id}/asignar` hace las tres cosas de una vez: pone propietario, **escribe en
la cronología por qué le tocó a esa persona**, y crea la tarea de primera llamada. Un lead asignado
sin próximo paso no sirve de nada, y una asignación sin explicación parece una lotería.

## Cuándo se recalcula

- **Al llegar una señal**, en la misma petición. La señal recién creada se le pasa a mano al cálculo:
  todavía no está en la base, y sin eso el contacto se quedaría con la puntuación vieja.
- **`POST /match/recalcular`** para toda la empresa, que es lo que hará el barrido nocturno. Existe
  porque el Momento **decae**: sin recalcular, un lead de hace un mes seguiría marcando 90.

## Hoy ordena por Match

Esta es la promesa del módulo. **El orden lo pone el Match**, no la antigüedad de un recordatorio; a
igual Match desempata la urgencia. Un contacto **sin puntuar va detrás** de cualquiera que sí lo
esté, aunque el otro puntúe bajo: «no sé nada de este» no puede adelantar a «sé que este vale 30».
Al principio, cuando nadie tiene puntuación, todos empatan y manda la urgencia — el comportamiento
del módulo 4.

## API

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/match/contactos/{id}` | Puntuación con encaje, momento y motivos |
| `POST` | `/match/recalcular` | Recalcula toda la empresa |
| `GET` | `/match/contactos/{id}/comercial` | Qué comercial encaja mejor, y por qué |
| `POST` | `/match/contactos/{id}/asignar` | Asigna, deja constancia y crea la primera llamada |

## Persistencia

Esquema **`match`**: `senal` (append-only) y `puntuacion_match` (una por contacto, única). Filtro
global y RLS como el resto. Se añade `zonas` a `identidad.membresia`.

La puntuación se guarda calculada para que listar cien contactos no signifique cien cálculos.

## Tests

- **Unitarios (41)**: decaimiento a 7 y 14 días, jerarquía de pesos, topes diarios (por día, no en
  total; todas las señales tienen tope), techo de 100, penalización por inactividad, señales del
  futuro ignoradas, redacción de los motivos; los cinco factores del Encaje por separado y juntos,
  encaje neutro sin histórico; media ponderada, pesos 0 y 1, **sin motivos no hay número**, tope de
  tres motivos, **el aviso tiene plaza reservada**, peso fuera de rango; y el reparto: zona, carga,
  afinidad, velocidad, **la persona nueva no penalizada**, siempre con motivo.
- **Integración (11)**: contacto nuevo que dice que no tiene histórico, la llamada contestada sube
  el Momento y la no contestada no, la oportunidad también puntúa, el número solo aparece cuando hay
  algo que contar, el reparto propone y explica, asignar deja rastro y crea tarea, **Hoy pone
  delante al de mejor Match**, el barrido recalcula, **el Momento decae solo al pasar los días**, y
  una empresa no ve las señales de otra.
