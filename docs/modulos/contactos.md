# Módulo 2 — Contactos

Contactos, cuentas y la cronología unificada: la ficha donde está **todo lo que ha pasado con una
persona**, en orden. Es lo mejor que tiene HubSpot y lo que había que copiar sin complejos.

## Modelo

`Contacto` { nombre, **email**, **teléfono**, cargo, cuenta?, origen, estado, propietario, activo,
fusionadoEnId }. `Cuenta` { nombre, NIF, sector, provincia, tamaño, web } es **opcional**: en B2C no
se rellena y no estorba. `Actividad` { contacto, tipo, sentido, cuerpo, resultado, autor, fecha }.

Estados: **Lead → Cliente**, o **Perdido**, o **Baja**. Tipos de actividad: nota, llamada, correo,
reunión, formulario, visita web y sistema.

## Las invariantes que sostienen el módulo

- **C1 — Al menos un medio de contacto.** Sin correo ni teléfono no es un contacto: es un nombre.
- **C2 — Normalización al guardar.** El correo va en minúsculas y recortado; el teléfono a formato
  internacional (`96 123 45 67`, `0034961234567` y `+34 961 234 567` se guardan los tres como
  `+34961234567`, con España por defecto). **No es cosmético**: sin esto, la deduplicación no
  encuentra nada.
- **C3 — El sistema propone, la persona aprueba.** Duplicado = mismo correo o mismo teléfono
  normalizados dentro de la empresa. Nunca se fusiona solo: fusionar automáticamente es la forma más
  rápida de destrozar una base de clientes.
- **C4 — Fusionar no pierde historia.** El superviviente **rellena sus huecos** con los datos del
  absorbido (nunca pisa lo que ya tiene), se trae **todas** sus actividades y deja un apunte del
  sistema. El absorbido se desactiva con el rastro de dónde fue; no se borra.
- **C5 — Las actividades son append-only.** Una conversación es un hecho, no un campo. Lo único que
  se le puede cambiar a una actividad es a qué contacto pertenece, y solo al fusionar.

Reglas de fusión que conviene conocer: si uno de los dos ya era **cliente**, el superviviente lo es;
si uno de los dos pidió la **baja**, la baja manda.

## Aislamiento entre empresas

Este es el módulo donde entra la **doble barrera**:

1. **Filtro global de EF Core** sobre `Contacto`, `Cuenta` y `Actividad`. Si no hay empresa activa,
   `EmpresaActual` es `null` y no casa con ninguna fila: **falla cerrado**.
2. **RLS de PostgreSQL** con política `aislamiento_empresa` y `FORCE ROW LEVEL SECURITY` en las tres
   tablas. `InterceptorEmpresa` fija `app.empresa_actual` **en cada conexión que se abre**, también
   cuando no hay empresa: dejar el valor de la petición anterior en una conexión reutilizada del
   pool sería justo la fuga que esto viene a evitar.

> **Aviso honesto**: un rol `SUPERUSER` (o con `BYPASSRLS`) se salta las políticas. Con el usuario
> `postgres` de un equipo de desarrollo, la barrera efectiva es la de EF Core. En producción la
> aplicación debe conectarse con un rol normal.

## Importación CSV

Dos pasos: **previsualizar** (valida y avisa, sin guardar nada) y **confirmar**. Nadie debería
descubrir que su fichero estaba mal después de haber metido 400 filas basura.

El lector detecta el separador (`;`, `,` o tabulador), respeta los campos entrecomillados —incluidas
las comillas dobles escapadas— y reconoce las columnas **por su nombre, sin acentos ni mayúsculas y
en cualquier orden**: *nombre*, *correo/email*, *teléfono/móvil*, *cargo*, *origen*. Los errores se
devuelven con **número de línea contando la cabecera como la 1**, que es como los cuenta la persona
que abre el fichero. Los duplicados —del propio fichero o ya existentes— se omiten y se cuentan.

> Detalle de implementación: quitar acentos se hace con un mapa explícito y no con
> `Normalize(FormD)`, porque el proyecto compila con `InvariantGlobalization` y en ese modo la
> normalización Unicode no hace nada. Para cabeceras en español basta y es determinista.

## API

| Método | Ruta | Permiso | Descripción |
|---|---|---|---|
| `GET` | `/contactos?busqueda=&estado=` | `contacto.leer` | Listado con búsqueda por nombre, correo, teléfono o cargo |
| `GET` | `/contactos/{id}` | `contacto.leer` | Ficha con la cronología completa |
| `POST` | `/contactos` | `contacto.gestionar` | Crea. **201** |
| `PUT` | `/contactos/{id}` | `contacto.gestionar` | Actualiza |
| `PUT` | `/contactos/{id}/estado` | `contacto.gestionar` | Cambia el estado |
| `DELETE` | `/contactos/{id}` | `contacto.gestionar` | Desactiva (no borra) |
| `POST` | `/contactos/{id}/notas` | `contacto.gestionar` | Añade una nota |
| `POST` | `/contactos/{id}/llamada` | `contacto.gestionar` | Registra la llamada en un clic |
| `GET` | `/contactos/duplicados` | `contacto.gestionar` | Parejas propuestas, con el motivo |
| `POST` | `/contactos/{id}/fusionar` | `contacto.gestionar` | Fusiona otro dentro de este |
| `POST` | `/contactos/importar` | `contacto.gestionar` | CSV, con previsualización |
| `GET` `POST` | `/cuentas` | `contacto.*` | Cuentas |

> **Las cuentas no tuvieron pantalla hasta mucho después.** Estos dos endpoints existían desde este
> módulo, pero en la interfaz una cuenta era **solo un desplegable** en el alta de contactos: se podían
> elegir y no se podía crear ninguna, así que el desplegable estaba siempre vacío y todos los contactos
> eran B2C para siempre. Hoy hay una sección **Cuentas** en el menú, con su lista y su alta. Una API
> completa no es una funcionalidad.

## Persistencia

Esquema **`contactos`**: `contacto`, `cuenta`, `actividad`. Índices de deduplicación
`(empresa_id, email)` y `(empresa_id, telefono)` — **no únicos a propósito**: un duplicado se detecta
y se propone, no se rechaza; rechazarlo obligaría a resolverlo antes de poder guardar.

## Interfaz

Listado con búsqueda instantánea, alta, importación con previsualización, panel de duplicados con
fusión en un clic, y la ficha con su cronología en línea de tiempo. El registro de llamada es un
desplegable y un botón: contactado · no contesta · no le interesa · volver a llamar.

## Tests

- **Unitarios (41)**: normalización de teléfono en siete formas de escribirlo, contacto sin medio,
  fusión (rellena huecos sin pisar, estado cliente y baja, empresas distintas, consigo mismo, dos
  veces), llamada sin resultado, y el lector de CSV (separadores, comillas escapadas, cabeceras con
  acentos, alias en cualquier orden).
- **Integración (13)**: alta y listado, **una empresa no ve los contactos de otra**, la ficha de otra
  empresa devuelve 404, búsqueda por teléfono normalizado, llamada y nota en la cronología,
  previsualización que no guarda, importación con errores por línea, omisión de duplicados,
  detección de duplicado con el teléfono escrito distinto, **fusión que no pierde ninguna
  actividad**, y comprobación de que las tres políticas de RLS existen en la base.
