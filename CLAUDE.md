# Guía para agentes (CLAUDE.md)

**match.keting** — CRM independiente en **.NET 8 + PostgreSQL**. Diez módulos terminados.

Lee primero [`README.md`](README.md) (estructura y API) y, según lo que vayas a tocar,
[`docs/modulos/<modulo>.md`](docs/modulos/): cada uno explica **por qué** está hecho así, incluidas las
decisiones que parecen raras y los errores que se corrigieron a mitad. Antes de cambiar una regla,
busca si ya está documentada; casi siempre lo está, y casi siempre hay un test que la sujeta.

## Reglas del proyecto

- **Un módulo a la vez, terminado por completo**: dominio · API · persistencia · tests unitarios ·
  tests de integración · documentación. No se empieza el siguiente con el anterior a medias.
- Dominio **en español**: clases, tablas, columnas, endpoints, códigos de error. La interfaz también.
- **Clean Architecture ligera**: `Matchketing.<Modulo>` (dominio + aplicación) no referencia
  frameworks. La infraestructura vive en `Matchketing.Persistencia`.
- **Ningún módulo de negocio referencia a otro.** Las dos únicas excepciones son `Matchketing.Nucleo`
  y `Matchketing.Auditoria`, las dos dominio puro sin frameworks. Si necesitas datos de otro módulo,
  declara un puerto y que lo implemente la persistencia: `IAlmacenPersonal` y `IConsultaMatch` son los
  ejemplos a copiar.
- Fallos **esperados** con `Resultado`/`Error`, nunca con excepciones. Se lanza solo ante errores de
  programación (una constante vacía, un argumento nulo que no puede serlo).
- **Multiempresa obligatorio**: `empresa_id` en toda tabla de datos, filtro global de EF **y** RLS de
  PostgreSQL. Ningún endpoint acepta `empresa_id` por parámetro: sale del JWT y solo del JWT.
- Compilación estricta: `TreatWarningsAsErrors`. No relajes un analizador sin justificarlo por escrito
  en el mismo sitio donde lo relajas.
- **Ningún número sin motivo.** Si una cifra que se le muestra a una persona no se puede explicar en
  una frase, no se muestra. Vale para el Match y vale para todo lo demás.
- **Nunca pidas un dato que el sistema puede deducir**, y nunca preguntes dos veces lo mismo por dos
  caminos. Es la tesis del módulo [Repaso](docs/modulos/repaso.md) y la razón de que un comercial
  abra esto: en cuanto una pantalla pide escribir o repite una pregunta, se abandona.

## Compilar y probar

```bash
dotnet build
dotnet test                            # 495 pruebas; necesita PostgreSQL en localhost:5432
./scripts/comprobar-aislamiento.sh     # la RLS, que ningún test de C# puede comprobar
```

Los tests de integración usan la base **`matchketing_test`** y la borran y recrean al empezar.
Sobrescribe la cadena con `MATCHKETING_TEST_CONEXION` si hace falta.

> **Cuidado con la configuración.** No leas `constructor.Configuration[...]` en variables locales al
> principio de `Program.cs`: en ese momento aún no están las fuentes que añade quien hospeda la
> aplicación, y los tests de integración acabarían apuntando a la base de desarrollo. Ya pasó. Se lee
> siempre de forma diferida, dentro de las fábricas del contenedor, y hay un test que lo vigila
> (`La_api_de_pruebas_usa_la_base_de_pruebas`).

## Migraciones EF Core

```bash
dotnet tool restore
dotnet ef migrations add <Nombre> \
  --project src/Matchketing.Persistencia \
  --startup-project src/Matchketing.Api \
  --output-dir Migraciones
```

Después de generarla, **añade a mano** el SQL que EF no sabe generar y que este proyecto sí necesita:

```sql
ALTER TABLE <esquema>.<tabla> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <esquema>.<tabla> FORCE ROW LEVEL SECURITY;
CREATE POLICY aislamiento_empresa ON <esquema>.<tabla>
    USING (empresa_id::text = current_setting('app.empresa_actual', true))
    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
```

y el `DROP POLICY` correspondiente en el `Down`. Una tabla de datos sin RLS es un agujero silencioso:
todo seguirá funcionando y las pruebas seguirán pasando.

## Trampas conocidas

Cosas que ya han costado tiempo. Están aquí para que no lo vuelvan a costar:

- **`ValueGenerated.Never` en toda clave Guid.** Los identificadores los genera el dominio. Si EF cree
  que los genera la base, al descubrir una entidad hija nueva colgando de un padre ya rastreado emite
  `UPDATE` en vez de `INSERT` y falla con «expected to affect 1 row(s), but actually affected 0». Ya
  está resuelto en `OnModelCreating`; no lo toques.
- **`InvariantGlobalization` está activo**, así que `string.Normalize(FormD)` no hace nada. Para
  quitar acentos hay un mapa explícito en `LectorCsv`.
- **Los filtros van sobre la consulta, no sobre la proyección.** `bd.Contactos.Where(...).Select(...)`,
  nunca al revés: EF no sabe traducir un `WHERE` contra un registro ya proyectado.
- **`ExecuteDelete`/`ExecuteUpdate` se ejecutan al momento** y no esperan a `SaveChanges`. Si en la
  misma operación hay un apunte de auditoría, abre una transacción explícita o quedará el borrado sin
  su rastro.
- **Un disparador `BEFORE ... FOR EACH ROW` no salta si la orden afecta a cero filas.** Con RLS por
  medio esto engaña: parece que la regla no existe cuando lo que pasa es que la otra barrera actuó
  primero. Fija `app.empresa_actual` antes de comprobar nada.
- **Los tests de integración comparten una sola instancia de la API** (`ColeccionApi`). Si añades una
  clase nueva, márcala con `[Collection(ColeccionApi.Nombre)]` o borrará la base mientras otra la usa.
  Y filtra siempre por identificador al consultar: `FirstAsync()` sin `WHERE` recoge filas de otra
  prueba.
- **El servidor no formatea números con `:N0`.** Con `InvariantGlobalization` eso produce «8,400 €»,
  que en España es una cifra distinta, y la cultura `es-ES` no existe sin ICU. Usa
  `Castellano.Euros(...)`.
- **Nada de datos personales en `auditoria.registro`.** Hay una red que tapa correos y teléfonos, pero
  no metas texto libre escrito por usuarios: la tabla es append-only y de ahí no sale nada.
- **`pushManager.subscribe()` puede no fallar nunca.** Si el navegador no alcanza el servicio de push
  de su fabricante, reintenta por dentro y la promesa se queda colgada para siempre: el botón se
  queda gris y en pantalla no aparece nada. Todo lo que dependa de un servicio externo del navegador
  va con plazo. Y `e.message` puede venir vacío, así que un `catch` que solo lo propague no enseña
  nada: usa `motivoDe(...)`, que nunca devuelve vacío. Ver [`docs/modulos/avisos.md`](docs/modulos/avisos.md).
- **El trabajador de servicio sirve el armazón desde la caché**, así que un cambio en `index.html` no
  se ve en la primera recarga. Al probar en un navegador automatizado, usa un perfil limpio; si no,
  estarás mirando la versión anterior y buscando un fallo que ya arreglaste. Pasó.
- **`fetch` solo rechaza cuando no hay red.** Un 400 llega como respuesta normal. La cola del repaso
  depende de distinguirlos (`e.name === 'SinRed'`): sin eso reintentaría para siempre respuestas que el
  servidor ya rechazó. Ver [`docs/movil.md`](docs/movil.md).
- **`JwtBearer` reescribe las reclamaciones al validarlas.** Trae `MapInboundClaims` activado, así que
  la reclamación `email` que se firma llega como el URI largo de WS-Federation. Buscar por el nombre
  corto devuelve `null` y no falla: `/auth/yo` devolvió el correo vacío durante ocho módulos porque la
  interfaz no lo usaba. Si añades una reclamación estándar, busca por las dos formas.
- **Un color tampoco puede ir sin motivo.** El magenta significa «aquí está la acción». Usarlo para
  decir «esta es la última columna» (`:last-child`) hacía que la etapa vacía se llevara la mirada y la
  que tenía 71.800 € quedara apagada. Si algo se pinta, tiene que estar diciendo un dato.

## Interfaz

Es un **único fichero**, `src/Matchketing.Api/wwwroot/index.html`: tokens, estilos, vistas y
JavaScript. Sin dependencias externas ni paso de compilación. Paleta magenta (`--magenta: #D4006E`),
claro y oscuro mediante `:root` + `prefers-color-scheme` + `[data-theme]`. **No hay rojo** en el
sistema: lo que en otras herramientas sería rojo aquí va en ámbar.

Cuando cambies algo de la interfaz, **míralo**. Casi todos los defectos de este proyecto —CORS que
faltaba, conversiones inventadas, Match clavado en 100, JSON crudo en pantalla, el menú que no existía
en el móvil— salieron de una captura, no de un test.

Y **míralo también a 390 px**, midiendo `scrollWidth` contra `innerWidth`. Dos defectos de
desbordamiento vivían ahí desde el módulo 2. Lo adaptable va **al final del estilo**: un `@media`
colocado antes de la regla que quiere anular no hace nada, y eso ya pasó una vez. Ver
[`docs/movil.md`](docs/movil.md).

## Antes de producción

Lee [`docs/despliegue.md`](docs/despliegue.md). Lo más importante: la aplicación **tiene que
conectarse con un rol sin privilegios de superusuario**, o la RLS no se aplica y el aislamiento entre
empresas se queda con una sola barrera en lugar de dos.

## Entorno de ejecución sin SDK (nota)

Si no hay SDK de .NET instalado, `dn.sh` y `dsh.sh` lo ejecutan vía Docker
(`mcr.microsoft.com/dotnet/sdk:8.0`) con `--network host`. Son un apaño del entorno, no parte del
producto. Docker Hub puede estar bloqueado por política; `mcr.microsoft.com` y NuGet sí están
permitidos.
