# Despliegue

Lo que hay que hacer distinto en producción. Es corto, y todo lo de aquí importa.

## 1. El rol de la base de datos no puede ser superusuario

**Esto es lo más importante de esta página.** El aislamiento entre empresas se apoya en dos barreras:
el filtro global de EF Core y la *row level security* de PostgreSQL. La segunda **no se aplica a los
superusuarios ni al propietario de la tabla sin `FORCE`**, y aunque todas las tablas de datos llevan
`FORCE ROW LEVEL SECURITY`, un superusuario la salta igual.

Si la aplicación se conecta como `postgres`, la RLS es decoración: queda una sola barrera, la del
código, y basta un `IgnoreQueryFilters()` mal puesto para que una empresa vea los contactos de otra.

Las migraciones sí se aplican con un rol con privilegios (necesita crear esquemas, tablas y
disparadores). La aplicación en marcha, no.

```sql
-- Se ejecuta una vez, con un rol administrador, DESPUÉS de aplicar las migraciones: los permisos se
-- conceden sobre las tablas que ya existen, y `ALTER DEFAULT PRIVILEGES` cubre las que vengan luego.
CREATE ROLE matchketing_app LOGIN PASSWORD 'la-que-sea-larga-y-aleatoria';

GRANT CONNECT ON DATABASE matchketing TO matchketing_app;

DO $$
DECLARE esquema text;
BEGIN
    FOR esquema IN
        SELECT nspname FROM pg_namespace
        WHERE nspname IN ('identidad', 'organizacion', 'contactos', 'embudo', 'tareas',
                          'match', 'captacion', 'cumplimiento', 'auditoria')
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO matchketing_app', esquema);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO matchketing_app', esquema);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO matchketing_app', esquema);
    END LOOP;
END $$;
```

El historial de migraciones vive en el esquema `public` y **no** aparece en esa lista: lo lee y lo
escribe el rol administrador al migrar, y la aplicación en marcha no lo necesita.

Nada de `BYPASSRLS`, y nada de hacerlo propietario de las tablas: el disparador append-only de
`auditoria.registro` deja pasar al propietario a propósito (para que las migraciones y el borrado de
una empresa funcionen), así que un rol de aplicación propietario podría editar el registro de
auditoría.

Para comprobar que ha quedado bien, **con la conexión de la aplicación** (no con la de administrador):

```sql
SELECT current_user, usesuper FROM pg_user WHERE usename = current_user;
-- usesuper tiene que ser false

SET app.empresa_actual = '';
SELECT count(*) FROM contactos.contacto;   -- 0
SELECT count(*) FROM auditoria.registro;   -- 0

SELECT set_config('app.empresa_actual', '<id-de-una-empresa>', false);
SELECT count(*) FROM contactos.contacto;   -- solo las de esa empresa

UPDATE auditoria.registro SET detalle = 'retocado';
-- ERROR: auditoria.registro solo admite INSERT: es un registro de auditoría.
```

Si el primer `count(*)` devuelve algo distinto de 0, la RLS no está actuando y hay que pararse aquí:
el sistema tiene una sola barrera de aislamiento en vez de dos.

Si el rol hay que rehacerlo, `DROP ROLE` falla mientras queden privilegios concedidos. Primero
`REASSIGN OWNED`/`DROP OWNED BY matchketing_app`, y `REVOKE ALL ON DATABASE matchketing FROM
matchketing_app`.

## 2. Secretos

Tres valores que **no** pueden quedarse en los de desarrollo:

| Ajuste | Para qué | Si se rota… |
| --- | --- | --- |
| `Jwt:Clave` | Firma los tokens de sesión. | Todo el mundo tiene que volver a entrar. Inocuo. |
| `Baja:Secreto` | Firma los enlaces de baja. | **Mata todos los enlaces de baja emitidos.** Ver abajo. |
| `ConnectionStrings:Matchketing` | Con el rol del punto 1. | — |

`Baja:Secreto` es distinto del JWT precisamente para que las dos rotaciones no estén atadas. Los
enlaces de baja no caducan por diseño (ver [`modulos/cumplimiento.md`](modulos/cumplimiento.md)), así
que rotar ese secreto es la única forma de invalidarlos y no debería hacer falta nunca. Si hay que
hacerlo, hay que asumir que quien tenga un correo antiguo verá «este enlace no es válido».

`Baja:UrlBase` tiene que ser la dirección pública desde la que se sirve la aplicación: es la que se
pega en los correos.

## 3. Migraciones

En `Development` la aplicación migra sola al arrancar. En producción **no**: dos instancias arrancando
a la vez migrarían a la vez. Se aplica antes de desplegar, con el rol administrador:

```bash
dotnet ef database update \
  --project src/Matchketing.Persistencia \
  --startup-project src/Matchketing.Api
```

## 4. Sonda de salud

`GET /salud` devuelve **503** si no llega a la base de datos, y 200 con `{"estado":"vivo"}` si llega.
Es la ruta que debe mirar el equilibrador de carga: proceso arriba y base de datos caída es
exactamente el estado en el que no hay que mandarle tráfico.

## 5. Trabajos en segundo plano

Tres trabajos corren dentro del propio proceso (`Trabajos/`):

| Trabajo | Cada | Qué hace |
| --- | --- | --- |
| Barrido de Match | 24 h | Recalcula el Match de todos los contactos: el Momento decae con el tiempo y el tiempo pasa sin que nadie pulse nada. |
| Rebote de leads | 30 min | Reasigna los leads sin atender pasadas las horas laborables configuradas. |
| Retención de leads | 24 h | Borra los leads que han cumplido su plazo de conservación. |

Van dentro del proceso porque a esta escala montar un planificador aparte añade una pieza que puede
fallar sola. **Con varias instancias hay que ejecutarlos en una sola**: los tres son idempotentes en
el sentido de que no corrompen nada si se solapan, pero el de retención borraría en paralelo y el de
rebote podría reasignar dos veces el mismo lead. La forma más simple es una variable de entorno que
los active solo en una instancia; si algún día hace falta más, toca un cerrojo en la base.

El rebote va cada media hora y no una vez al día por un motivo concreto: un plazo de cuatro horas
laborables que se comprobara solo de madrugada se convertiría en un plazo de un día.

## 6. Límite de intentos de acceso

`/auth/login` y `/auth/contrasena` admiten 20 intentos cada cinco minutos **por IP de origen**. Si la
aplicación va detrás de un proxy inverso, hay que configurar `ForwardedHeaders` o todas las peticiones
compartirán la IP del proxy y el límite se agotará solo.

No hay bloqueo por cuenta, y es deliberado: bloquear una cuenta tras N fallos convierte el límite en
un arma contra su dueño —basta con fallar adrede para dejarle fuera—, y ese ataque es más fácil y más
dañino que el que se pretendía evitar.

## 7. CORS

Solo dos grupos de rutas aceptan cualquier origen, y los dos tienen que hacerlo:

* `/f/…` — el formulario de captación se pega en la web del cliente, que es otro dominio.
* `/b/…` — la página de baja se abre desde el gestor de correo.

El resto de la API es de mismo origen. Hay un test de integración que comprueba que las demás rutas no
emiten `Access-Control-Allow-Origin`.

## 8. Zona horaria

Las horas laborables del rebote se cuentan en hora de España (`Europe/Madrid`). Si la imagen del
contenedor no trae `tzdata`, el sistema usa UTC+1 fijo y en verano la franja queda corrida una hora.
Instalar `tzdata` cuesta nada y lo arregla.
