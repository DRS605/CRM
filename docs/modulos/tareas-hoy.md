# Módulo 4 — Tareas y Hoy

**La pantalla que justifica el producto.** Un comercial abre match.keting por la mañana, ve una lista
de menos de diez acciones, las hace y cierra. Nada más.

## Tarea

`Tarea` { título, contacto?, oportunidad?, **venceEl**, responsable, origen, estado, vecesAplazada }.
Estados: pendiente → **hecha** o **descartada**. Origen: **manual** o **automática** (la creó el
sistema).

Decisiones pequeñas que importan:

- **Sin fecha, vence hoy.** Una tarea sin día es una tarea que no se hace nunca.
- **La fecha es un día, no una hora.** Nadie planifica su semana al minuto.
- **`vecesAplazada` se cuenta y se enseña** a partir de la tercera. Aplazar cinco veces es una señal,
  no un accidente.
- **Descartar no es completar**: cierra la tarea pero no emite `TareaCompletada`. Se guarda igual,
  porque enseña tanto como haberla hecho.

### H2 — Aplazar exige fecha, y futura

`Aplazar(null)` falla. `Aplazar(hoy)` y `Aplazar(ayer)` también. **No existe «aplazar
indefinidamente»**: eso es descartar, y descartar tiene su propio botón. Es la regla que impide que
Hoy se convierta en un cementerio de cosas que nunca se harán.

## La pila de Hoy

Hoy no es una lista de tareas: es **todo lo que merece tu atención**, de tres sitios distintos.

| Tarjeta | De dónde sale | Motivo que se enseña |
|---|---|---|
| **Tarea** | Pendientes que vencen hoy o antes | «Toca hoy» · «Tenía que haberse hecho ayer» · «Lleva N días esperando» |
| **Parada** | Oportunidades abiertas más días de los que su etapa tolera | «Lleva N días parada en «Propuesta»» |
| **Sin próximo paso** | Contactos vivos sin tarea pendiente **ni** oportunidad abierta | «Un contacto sin próxima acción es un contacto que se pierde» |

Esa tercera fila es el invariante **H1** hecho pantalla, y es la diferencia entre un CRM que registra
y uno que empuja.

**Ninguna tarjeta se enseña sin motivo.** Es la misma regla que gobernará el Match: un número o una
alerta sin porqué no la usa nadie.

### El orden

> **Nota de alcance honesta**: el orden definitivo lo pondrá el **Match del módulo 5**. Hasta
> entonces se usa una urgencia provisional —lo vencido primero (100 + días), luego lo de hoy (60),
> luego lo parado (40 + días de más), y al final lo que no tiene próximo paso (20)—, desempatando por
> importe. Ya es mucho mejor que una lista alfabética, pero **no es el producto todavía**.

### El vacío es una funcionalidad

Cuando la pila se acaba, Hoy dice **«Hecho por hoy»** y se para. No hay más pantallas que mirar, ni
un panel de gráficas al que ir a hacer tiempo. Si has hecho algo, lo cuenta: *«3 acciones hechas y
nada más pendiente. Cierra y vete.»*

## La única automatización del MVP

Una llamada que acaba en **«volver a llamar»** crea sola la tarea de seguimiento para mañana. Y **no
la duplica**: si ya hay un seguimiento pendiente para ese contacto, no se crea otro. Hoy debe ser una
lista corta, no un montón de recordatorios repetidos.

Es la única automatización fija del MVP. Las configurables («si pasa X, haz Y») son de F2.

## API

| Método | Ruta | Permiso | Descripción |
|---|---|---|---|
| `GET` | `/hoy` | `tarea.leer` | La pila del día, ya ordenada y con sus motivos |
| `GET` | `/tareas?soloPendientes=` | `tarea.leer` | Lista de tareas |
| `POST` | `/tareas` | `tarea.gestionar` | Crea. Sin fecha, vence hoy. **201** |
| `POST` | `/tareas/{id}/completar` | `tarea.gestionar` | Hecha |
| `POST` | `/tareas/{id}/descartar` | `tarea.gestionar` | Descartada (se guarda) |
| `POST` | `/tareas/{id}/aplazar` | `tarea.gestionar` | **Exige fecha futura** |

## Persistencia

Esquema **`tareas`**, tabla `tarea`, con filtro global y política de RLS como el resto. El índice que
sostiene la pantalla es `(empresa_id, estado, vence_el)`.

`ConsultaHoy` vive en persistencia porque cruza los tres módulos —tareas, contactos y embudo— y
ninguno debe conocer a los otros.

## Interfaz

Pila de tarjetas: quién, por qué ahora, y qué hacer. **Llamar despliega los cuatro resultados dentro
de la propia tarjeta** —un clic para abrir, otro para registrar—, y al registrarlo completa la tarea.
Aplazar es un desplegable en lenguaje natural: *a mañana · al lunes · dos semanas*.

**Solo la primera tarjeta lleva el botón en magenta.** La pila se trabaja de arriba abajo, y cinco
botones magenta apilados dejarían de señalar nada.

## Tests

- **Unitarios (15)**: sin fecha vence hoy, título obligatorio, completar y cerrar dos veces,
  descartar que no emite `TareaCompletada`, **aplazar sin fecha**, aplazar a hoy y al pasado,
  contador de aplazamientos, cerrada que no se aplaza ni se edita, vencida y toca-hoy.
- **Integración (12)**: empresa nueva sin nada que hacer, **contacto vivo sin próximo paso aparece**,
  crear tarea lo quita de esa lista, **lo vencido va por delante de lo de hoy**, completar lo saca de
  la pila y suma a hechas, aplazar sin fecha da 400, aplazar a mañana lo saca, **la llamada de
  «volver a llamar» crea la tarea sola**, no se duplica, «contactado» no crea ninguna, **la
  oportunidad parada sale en Hoy** (envejecida en la base para no esperar cuatro días), y una empresa
  no ve las tareas de otra.
