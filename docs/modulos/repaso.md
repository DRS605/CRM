# Módulo 9 — Repaso

**Estado**: terminado · **Proyecto**: `Matchketing.Repaso` · **Esquema**: `repaso`

Una semana de seguimiento comercial en menos de dos minutos, sin escribir nada.

## El problema que resuelve

Los comerciales no usan los CRM. No porque sean feos, sino por una asimetría: **el coste lo paga
quien introduce los datos y el valor se lo lleva quien lee los informes**. El comercial teclea lo que
ya sabe para que otro lo mire. Ninguna mejora de usabilidad arregla eso.

Los siete módulos anteriores hacen bien su trabajo y siguen teniendo ese problema. Este lo invierte.

## Las tres inversiones

### 1. El comercial no rellena; el sistema pregunta y él contesta

Escribir una nota son cuarenta segundos y nadie tiene cuarenta segundos veinte veces. El sistema mira
lo que **ya tiene en la base**, deduce qué debería haber pasado y pregunta, con la respuesta probable
ya puesta delante. Contestar son tres segundos.

Seis preguntas, todas derivadas y ninguna pidiendo un dato nuevo:

| Tipo | Sale cuando | Se contesta con |
| --- | --- | --- |
| `TareaVencida` | Una tarea mía pendiente con la fecha pasada | Hecha · Aún no · Ya no hace falta |
| `LeadSinTocar` | Lead mío sin **nada**: ni salida, ni oportunidad, ni tarea | Hablé con él · No contesta · No le interesa |
| `CierrePasado` | Oportunidad abierta con la fecha de cierre pasada | Se retrasa dos semanas · Ganada · Perdida |
| `OportunidadEstancada` | Más días en la etapa de los que esa etapa tolera | Sigue viva · Ganada · Perdida |
| `SilencioCaliente` | Match ≥ 65 y 21 días sin actividad | Le llamo hoy · Déjalo estar |
| `ClienteSinSiguientePaso` | Le vendiste hace 45 días y no hay nada previsto | Le llamo hoy · Déjalo estar |

El orden es el del enum, y no es arbitrario: primero lo que **rompe una promesa** (dijiste que lo
harías), después lo que tiene dinero encima, y al final los avisos. Si lo primero que ves es «este
contacto está callado» mientras tienes tres tareas vencidas, el repaso parece ruido.

Cada pregunta se **redacta en el servidor**, no en el cliente: así la frase es la misma en la web, en
el móvil y en cualquier integración futura, y el motivo se puede probar. Igual que en Hoy: una tarjeta
sin motivo no se enseña.

### 2. Fricción proporcional a la consecuencia

**R2**: nada irreversible en un toque.

* «No contesta» es un toque. Se apunta en la ficha y sale un recordatorio.
* «Ganada» es un toque. Es buena noticia y deshacerla es una conversación con el jefe, no un botón.
* «Perdida» pide el motivo. Son dos toques, y es el **único dato extra de todo el módulo**: también es
  una lista cerrada. Se pide porque alimenta el informe de por qué se pierde, el único que de verdad
  cambia cómo se vende.
* «Ahora no» está siempre. Si tocar rápido da miedo, nadie toca rápido.

**R1**: una respuesta que no pertenece a la pregunta se rechaza. Sin esto, un cliente podría mandar
«Ganada» a una tarea vencida y el servidor buscaría una oportunidad que no existe.

### 3. Devolverle algo

Al vaciar la pila aparece **su semana**: llamadas —con la comparación con la anterior—, contactos
nuevos, oportunidades, tareas cerradas y su ratio de cierre. Solo lo suyo, y solo lo ve él.

El titular nunca regaña. Si la semana ha sido floja lo dice sin adjetivos: «6 llamadas esta semana.
Ninguna cerrada todavía.» Un CRM que echa la culpa se cierra y no se vuelve a abrir.

## Que se pueda vaciar

Esto es la mitad del módulo. Una pantalla que nunca llega a cero se abandona en dos semanas.

Toda respuesta —incluidas «sigue viva» y «ahora no»— quita la pregunta de la pila, mediante
`repaso.pospuesta`, el único dato que este módulo guarda. Sin él, una oportunidad estancada a la que
contestas «sigue viva» vuelve mañana.

La alternativa era tocar `entro_en_etapa_en` para que dejara de contar como estancada, y eso habría
sido **falsear el histórico del embudo para arreglar un problema de pantalla**. Lo que se guarda es
que alguien la revisó: quién y cuándo. Un jefe puede distinguir «lo miró y decidió que sigue» de
«nadie lo ha mirado», que es justo lo que un CRM normal no sabe decir.

Cuánto se aparca cada cosa:

| Respuesta | Vuelve en |
| --- | --- |
| «Ahora no» | 3 días — sirve para pasar de pantalla, no para esconder |
| «Sigue viva» y el resto | 7 días — cada cuánto se repasa |
| «Déjalo estar» en un silencio | 30 días — volver a la semana es insistir |
| «Déjalo estar» en un cliente | 90 días |

## Nada redundante: las dos exclusiones que costaron un test

`LeadSinTocar` no se limita a «sin actividad saliente». Excluye también a quien tenga **una
oportunidad** o **cualquier tarea**, y las dos exclusiones salieron del test que cuenta los toques:

* Si le he abierto una oportunidad, es evidente que he hablado con él. Preguntármelo me dice que el
  sistema no se entera de nada.
* Si hay una tarea suya, la tarea ya es la pregunta, más arriba en la misma pila. Y si la **acabo de
  cerrar** en este repaso, preguntarme acto seguido si he hablado con él es el mata-topos que hace que
  se cierre la pestaña: contestas una tarjeta y aparece otra sobre lo mismo.

La primera versión hacía las dos cosas. Ninguna prueba de las que había lo habría detectado.

## El test que mide la promesa

`Una_semana_entera_se_cierra_en_un_toque_por_tarjeta` siembra la semana de un comercial —tareas que se
le pasaron, leads que no llamó, oportunidades con la fecha vencida y otras paradas—, vacía la pila
pidiéndola y contestándola en bucle, y comprueba tres cosas:

1. **La pila se puede vaciar.** Si algo no se aparcara, el bucle no terminaría.
2. **Un toque por tarjeta**, contados. Once tarjetas, once interacciones.
3. **Cero texto libre.** Si alguien añade mañana un campo obligatorio a cualquier respuesta, el test se
   pone rojo antes de que nadie lo descubra en una demo.

La promesa del módulo no está en el README: está en un test.

## Arquitectura: dos puertos para no romper nada

El repaso orquesta cinco módulos —contactos, embudo, tareas, match y organización— y referenciarlos
habría convertido la arquitectura en una bola.

* **`IConsultaRepaso`** deriva los hallazgos. Se implementa en persistencia con **seis consultas y
  ninguna por contacto**: si hubiera una por ficha, con doscientos contactos tardaría más en pintarse
  que en contestarse, y una pantalla lenta no se abre los viernes.
* **`IAccionesRepaso`** aplica lo que la respuesta implica. El adaptador vive en la API —la única capa
  que conoce a todos— y es **pura delegación sin decisiones**. Qué hacer con cada respuesta se decide
  en `ServicioRepaso`, que por eso se prueba entero sin base de datos. Si algún día aparece un `if` en
  el adaptador, está en el sitio equivocado.

Un solo `SaveChanges` por respuesta: el efecto y el apunte de que la pregunta queda aparcada van en la
misma transacción. Si fueran dos, un fallo entre medias dejaría la tarea cerrada y la pregunta viva.

## La pantalla

Una tarjeta grande y **sola**. Ver diez a la vez convierte una decisión en una lista, y una lista se
posterga.

Gobernada por teclado: `1`…`4` responden, `Esc` es «ahora no», y el foco va siempre a la respuesta
probable para que `Intro` avance sin mover la mano. Es lo que separa tres segundos por tarjeta de diez.
Solo la primera opción va en magenta: si todas destacan, ninguna destaca.

La pila se descarga **una vez** y se contesta en memoria. Volver a pedirla en cada respuesta metería un
viaje al servidor entre toque y toque, y con tres segundos por tarjeta eso se nota como lentitud. Al
acabar el lote se vuelve a pedir, y ahí se corrige cualquier desajuste: ganar una oportunidad puede
haber tumbado también su tarjeta de cierre pasado.

Si el servidor rechaza una respuesta, **la tarjeta se queda**. Nunca desaparece de la pantalla algo que
no se llegó a hacer.

## API

| Método | Ruta | Permiso | Qué hace |
| --- | --- | --- | --- |
| GET | `/repaso` | `tarea.leer` | La pila, con las respuestas ya escritas y los segundos estimados. |
| POST | `/repaso/responder` | `tarea.gestionar` | Contesta una pregunta y la quita de la pila. |
| GET | `/repaso/resumen?dias=7` | `tarea.leer` | Su semana. No es un cuadro de mando. |

La pila se corta en 30 preguntas pero dice cuántas hay en total: servir doscientas tarjetas y dejar que
la persona descubra sola que esto no se acaba es la forma más rápida de que no vuelva.

`/repaso/responder` no devuelve cuántas quedan. Contarlas obligaría a rehacer las seis consultas en
cada toque —seis consultas por cada tres segundos de trabajo— y el cliente ya lo sabe porque tiene la
pila delante.
