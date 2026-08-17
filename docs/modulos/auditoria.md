# Auditoría

**Estado**: terminado · **Proyecto**: `Matchketing.Auditoria` · **Tabla**: `auditoria.registro`

Quién hizo qué y cuándo, en las operaciones que no se pueden deshacer.

No es un módulo del menú: es una pieza transversal que usan los demás. Existe porque hay una docena
de operaciones en match.keting que cambian el pasado —fusionar dos contactos, cerrar una venta,
apuntar un permiso, borrar a una persona— y sobre ninguna de ellas se podía responder «¿quién hizo
esto?» sin abrir la base de datos y adivinar.

## Qué se audita, y qué no

La lista es corta y cerrada (`Acciones.Todas`, con un test que lo comprueba):

| Acción | Cuándo |
| --- | --- |
| `contacto.fusionado` | Al fusionar duplicados. Es la operación más difícil de deshacer del sistema. |
| `contacto.asignado` | Solo cuando **cambia de manos**; no cuando se reparte un lead nuevo. |
| `contacto.baja` | Por el enlace público (sistema) o apuntada a mano (usuario). |
| `contacto.borrado` | Supresión RGPD, con el recuento de filas. |
| `contacto.exportado` | Quién se llevó los datos de quién. |
| `oportunidad.ganada` / `.perdida` | Cambian todos los informes y el histórico del Encaje. |
| `consentimiento.otorgado` / `.retirado` | La base legal de todo lo que se envía. |
| `ajustes.cambiados` | Incluye el plazo de conservación, que decide cuándo se borra gente. |
| `retencion.aplicada` | El trabajo nocturno que borra leads caducados. |
| `empresa.exportada` / `empresa.borrada` | Portabilidad y cierre de cuenta. |

Lo que **no** se audita: leer, listar, buscar, crear un contacto, mover una oportunidad de etapa,
completar una tarea. Son la actividad normal de un CRM y ya dejan rastro donde se ve —la cronología
del contacto, el histórico de etapas—. Un registro que lo apunta todo es un registro que nadie mira,
y entonces vuelve a no haber registro.

## Las tres decisiones que lo sostienen

### 1. Se escribe en la misma transacción que la operación

`IRegistradorAuditoria` **no guarda**: añade la entidad al mismo `ContextoMatchketing` y deja que la
guarde quien estaba guardando. Los dos se confirman o se deshacen juntos.

Auditar en una transacción aparte produce el peor registro posible: uno que puede mentir en los dos
sentidos —apuntar algo que se deshizo, o perder algo que se hizo— y que por tanto no sirve para lo
único que sirve un registro.

Las operaciones que usan `ExecuteDelete` (borrar un contacto, borrar la empresa) se ejecutan al
momento y no esperan a `SaveChanges`, así que sus endpoints abren una **transacción explícita**. Sin
ella, un fallo al guardar el apunte dejaría los datos borrados y sin rastro de quién lo hizo.

### 2. En el detalle nunca van datos personales, y no es una convención

`RegistroAuditoria.Detalle` lleva JSON con cifras e identificadores. La regla es fácil de escribir en
un comentario y fácil de romper seis meses después, cuando alguien añada un campo de más al objeto
que se serializa. Y la tabla es append-only: si un correo entra ahí, no sale.

Así que la regla se cumple en `Detalles.Tapar`, que sustituye lo que parece un correo o un teléfono
antes de guardar. El caso interesante es el falso positivo: un UUID cuyo primer tramo sea todo
dígitos encaja de sobra en cualquier patrón de teléfono, y taparlo dejaría el apunte sin lo único que
lo hace útil. Se resuelve por número de dígitos —un teléfono tiene entre 9 y 15, un UUID tiene 32—, y
hay un test con ese UUID concreto para que nadie lo rompa al «mejorar» la expresión regular.

El mismo criterio, a mano, en los puntos de llamada: al perder una oportunidad se apunta el motivo
(un enum) y **no** el detalle escrito a mano, que es donde la gente cuenta cosas de personas.

### 3. Append-only en la base de datos, no solo en el código

Que la entidad de C# no tenga forma de modificarse está muy bien mientras todo el mundo pase por la
entidad. Un `UPDATE auditoria.registro SET detalle = ...` desde una consola, o un futuro caso de uso
que use `ExecuteUpdate` sin pensarlo, se lo saltarían sin enterarse.

La migración crea un disparador `auditoria.solo_anadir` que rechaza `UPDATE` y `DELETE`. Deja pasar al
**propietario de la tabla**, y a propósito: las migraciones tienen que poder tocarla y el borrado de
una empresa tiene que poder llevarse su auditoría. La aplicación se conecta con un rol sin
privilegios de propietario (ver [`despliegue.md`](../despliegue.md)), que es exactamente para quien
está puesta la regla.

Hay un test de integración que hace `SET ROLE` a un rol no propietario y comprueba que las dos órdenes
fallan. Ese test enseñó algo: **sin fijar la empresa activa, la RLS no deja ver ni una fila y el
`UPDATE` afecta a cero**, así que el disparador —que es `BEFORE ... FOR EACH ROW`— no llega a saltar.
Parecía que la regla no existía cuando lo que pasaba es que la otra barrera actuó primero.

## Aislamiento

`EmpresaId` es **obligatorio**, no opcional. Todas las acciones auditadas pertenecen a una empresa, y
tenerlo obligatorio es lo que permite que la RLS proteja también esta tabla: una política que tuviera
que dejar pasar filas sin empresa dejaría abierto justo el hueco por el que se ve todo.

`ActorId` sí es opcional: nulo significa «el sistema». Lo usan el trabajo nocturno de retención y la
baja pública, donde no hay ningún usuario nuestro a quien atribuir la acción. El listado hace el join
con `usuario` en LEFT para que esas líneas no desaparezcan, y muestra «el sistema».

## Un módulo transversal, como Núcleo

`Matchketing.Auditoria` es dominio puro, sin frameworks. Los módulos de negocio la referencian igual
que referencian `Matchketing.Nucleo`, y esa es la **primera excepción** a la regla de que ningún
módulo referencia a otro.

Está justificada: la alternativa era auditar en los endpoints, y entonces el apunte vive lejos de la
decisión que lo provoca. En cuanto haya dos caminos hacia la misma operación —un endpoint y un
trabajo nocturno, por ejemplo— uno de los dos se olvidará de auditar, y nadie se enterará hasta que
haga falta el registro.

## API

| Método | Ruta | Permiso | Qué hace |
| --- | --- | --- | --- |
| GET | `/auditoria?cuantos=100` | `empresa.ajustes` | Últimas acciones, lo más reciente primero. |

No hay POST, PUT ni DELETE. El registro se escribe solo desde dentro, como efecto de las operaciones
auditadas, y no hay ninguna ruta que permita añadir una línea a mano.

En la interfaz sale traducido a castellano: «Ganó una oportunidad · Marta Ruiz · 17/08/26 · 7.482 €».
El JSON crudo se queda en la base; enseñárselo a quien vende cocinas no es enseñarle nada.
