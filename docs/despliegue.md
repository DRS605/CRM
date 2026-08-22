# Despliegue

Cinco contenedores y ningún paso manual:

```bash
cp .env.ejemplo .env          # y rellenarlo: no hay secretos por defecto, a propósito
docker compose -f docker-compose.produccion.yml up -d --build
./scripts/comprobar-despliegue.sh https://crm.tuempresa.es
```

| Servicio | Qué hace | Cuándo |
| --- | --- | --- |
| `bd` | PostgreSQL 16. **No publica ningún puerto.** Crea el rol de la aplicación la primera vez. | siempre |
| `migraciones` | Aplica el esquema con el rol **dueño** y termina. | en cada despliegue |
| `permisos` | Da al rol de la aplicación permisos sobre lo que las migraciones acaban de crear, y termina. | en cada despliegue |
| `app` | La aplicación, con el rol **restringido**. Se comprueba a sí misma con `--comprobar-salud`. | siempre |
| `proxio` | Caddy: HTTPS pedido y renovado solo, y la IP real del cliente hacia dentro. | siempre |

Y una cosa que conviene leer entera: **las cuatro secciones siguientes existen porque este producto
funcionó perfectamente durante diecisiete módulos y no se podía desplegar.** Cada una es un fallo que
solo aparece fuera del portátil, y las cuatro se encontraron el mismo día, al arrancarlo por primera
vez como se arranca de verdad.

## 1. El rol de la base de datos no puede ser superusuario

**Lo más importante de esta página.** El aislamiento entre empresas se apoya en dos barreras: el filtro
global de EF Core y las políticas por fila de PostgreSQL. Las segundas **no se aplican a un
superusuario**. Si la aplicación se conecta como `postgres`, la RLS es decoración: queda una sola
barrera, la del código, y basta un `IgnoreQueryFilters()` mal puesto para que una empresa vea los
contactos de otra.

Eso ya estaba escrito aquí. Lo que faltaba es que **nada lo comprobaba**, y por eso ahora hay tres
cosas que sí:

1. **La sonda de salud lo mira.** `GET /salud` contesta **503** si el rol de la conexión es
   superusuario, diciéndolo con palabras. Un equilibrador de carga con sonda no manda tráfico a una
   instancia enferma, así que un despliegue mal hecho se cae en el primer minuto en vez de servir un
   producto que promete aislamiento y tiene la mitad. Se puede desactivar con
   `Aislamiento:PermitirSuperusuario=true`, y hay que escribirlo a mano: lo usan las pruebas de
   integración, que crean y borran la base y por eso no pueden ser un rol normal.
2. **Hay pruebas que corren con un rol restringido**: `PruebasRolRestringido`. Crea el rol con el mismo
   `scripts/bd/permisos.sql` del despliegue y levanta la API con él.
3. **El `docker compose` de producción lo hace solo.** El rol `matchketing_app` se crea en el primer
   arranque (`scripts/bd/01-rol-aplicacion.sh`) con `NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`,
   y la aplicación se conecta con él.

### Lo que esto escondía: no se podía crear una empresa

Al arrancar con un rol normal, la primera pantalla después de registrarse devolvía un 500:

```
new row violates row-level security policy for table "embudo"
```

Quien crea una empresa **no pertenece a ninguna todavía**, así que `app.empresa_actual` estaba vacío y
la política rechazaba la empresa, su embudo y sus cinco etapas. El producto no se podía usar, y ninguna
prueba podía verlo porque todas eran superusuario.

El arreglo es de una línea y está comentado en `EndpointsOrganizacion`: la empresa activa de esa
petición es **la que se está creando**, así que se fija antes de guardar. La lección general: cualquier
escritura que ocurra **antes** de que haya empresa en la sesión —crear una empresa, aceptar una
invitación, la entrada pública de un lead— tiene que fijar el inquilino a mano
(`IContextoEmpresaPublico.FijarEmpresa` + `ReaplicarEmpresaAsync`).

### Y otra cosa que escondía: no se podían hacer copias

`FORCE ROW LEVEL SECURITY` aplica las políticas **también al dueño de la tabla**, que es lo que se
quiere. Y `pg_dump` lee con `COPY`:

```
pg_dump: error: query failed: ERROR: query would be affected by row-level security policy for table "registro"
```

O sea: **sin arreglarlo no hay copias de seguridad**, y eso se descubre el día que hacen falta. La
salida no es quitar `FORCE`, es que el rol que copia pueda saltarse las políticas. De menos a más
privilegio:

```sql
ALTER ROLE matchketing_dueno BYPASSRLS CREATEDB;   -- lo justo: copiar y restaurar
```

En el `docker compose` de producción el dueño es el usuario inicial de PostgreSQL, que ya es
superusuario, así que ahí no hay que hacer nada. En una instalación a mano, sí — y `scripts/copia.sh`
lo comprueba **antes** de escribir el fichero y dice la orden exacta.

## 2. Secretos: la aplicación no arranca sin ellos

Dos secretos tenían un valor por defecto **escrito en este repositorio**, y un despliegue sin
configurarlos funcionaba perfectamente:

- `Jwt:Clave` firma las sesiones. Con la de desarrollo, cualquiera se fabrica un token con el
  identificador de empresa que quiera. Es el aislamiento entero, sin romper nada.
- `Baja:Secreto` firma los enlaces de baja. Con el de desarrollo, cualquiera fabrica el enlace de baja
  de cualquier contacto.

Ahora, fuera de `Development`, **la aplicación se niega a arrancar** si siguen puestos, si faltan, si
miden menos de 32 caracteres o si falta `Baja:UrlBase` (que por defecto apunta a otro dominio, así que
los enlaces de baja de los correos llevarían a un sitio que no es este). Lo hace antes de escuchar en
ningún puerto y dice cuál y por qué. Un aviso por registro no lo lee nadie el día del despliegue.

```bash
openssl rand -base64 48    # para cada una
```

Las claves de avisos (`Avisos:ClavePublica`, `Avisos:ClavePrivada`, `Avisos:Sujeto`) **se generan solas
al arrancar si no están**, y ahí sí basta un aviso por registro: sin ellas el producto funciona, solo
que sin avisos push. Pero hay que fijarlas: la clave pública va dentro de cada suscripción del
navegador, así que unas claves nuevas invalidan todas las existentes, y un reinicio dejaría a la
plantilla sin avisos hasta que alguien lo notara un viernes por la tarde. Se generan una vez —la
aplicación las imprime al arrancar— y no se tocan más.

## 3. Migraciones y permisos

En `Development` la aplicación migra sola al arrancar. En producción **no**: dos instancias arrancando
a la vez migrarían a la vez.

Las aplica el servicio `migraciones`, que es el **paquete de migraciones** de EF Core: un ejecutable
autónomo que lleva dentro el esquema y nada más. Se construye en la imagen y no necesita el SDK en el
servidor.

```bash
# Lo que hace ese servicio, si hiciera falta a mano:
./migrar --connection "Host=…;Username=<dueño>;Password=…"
```

Y después, **siempre**, los permisos:

```bash
psql -U <dueño> -d matchketing -f scripts/bd/permisos.sql
```

Ese segundo paso no es opcional y es fácil de olvidar: **una tabla nueva nace sin permisos** para
`matchketing_app`, así que el primer módulo que añada una tabla tira la aplicación con «permission
denied» en la primera petición que la toque. El guion recorre los esquemas **de la base, no de una
lista escrita a mano**, y es idempotente.

## 4. Detrás de un proxio: la IP del cliente

Con un proxio delante, `RemoteIpAddress` es la del proxio. Eso convierte tres cosas correctas en tres
cosas falsas, y ninguna avisa:

1. El techo de intentos de acceso reparte por IP: **todo el mundo comparte cubo**.
2. La IP del consentimiento es parte de la prueba de que alguien aceptó. La del proxio es una prueba
   que no prueba nada.
3. Lo mismo con la IP del envío de un formulario.

Se arregla con `Proxy:Confiar=true`, y **solo** con eso: el arreglo obvio —confiar siempre en
`X-Forwarded-For`— es peor que el problema, porque esa cabecera la escribe quien quiera y entonces se
podría elegir la IP que queda en el consentimiento y saltarse el techo de intentos cambiándola en cada
petición. Así que **falla cerrado**: sin declarar el proxio, la cabecera no se mira.

`Proxy:Redes` limita de qué redes se acepta (por defecto, las privadas: el caso de un contenedor con el
proxio al lado). Hay dos pruebas, una por sentido: que una cabecera inventada no cambia la IP sin
proxio declarado, y que sí la cambia con él.

### Cabeceras de seguridad

Van **en la aplicación**, no en el Caddyfile: una protección que vive en la configuración de otro
programa se pierde en la primera mudanza. Son `X-Content-Type-Options`, `Referrer-Policy`,
`X-Frame-Options: DENY`, `Strict-Transport-Security` (solo fuera de desarrollo: un HSTS en `localhost`
se queda pegado en el navegador meses) y una `Content-Security-Policy` con `default-src 'self'`.

La CSP lleva `'unsafe-inline'` y es una concesión de verdad, no un descuido: la aplicación es un solo
fichero con su estilo y su guion dentro, servido como estático, y quitarlo exige un nonce por petición
—o sea, dejar de servirlo como estático—. A cambio, `default-src 'self'` deja fuera cualquier origen
externo, que en una aplicación sin dependencias es la mitad del valor de una CSP.

## 5. Copias de seguridad

```bash
./scripts/copia.sh                                    # copia + verificación
./scripts/restaurar.sh --prueba copias/matchketing-….dump   # restauración de prueba
./scripts/restaurar.sh copias/matchketing-….dump      # ENCIMA de la base de verdad
```

Cuatro decisiones, y todas salieron de hacerlo:

1. **Formato propio de PostgreSQL** (`-Fc`): comprimido, restaurable por tablas, y con un índice que
   `pg_restore --list` puede leer. Eso último es lo que hace posible el punto 2.
2. **La copia se verifica al hacerla**: que pesa algo y que declara más de diez tablas con datos. Un
   fichero que no se puede leer no es una copia, es un fichero.
3. **Se borra lo viejo después de verificar la nueva.** Al revés, un fallo de red se lleva el
   historial.
4. **`--prueba` restaura en una base de usar y tirar**, cuenta las filas y la borra. Una copia que
   nunca se ha restaurado no se sabe si sirve, y probarlo encima de producción no es probarlo. El guion
   de copia avisa si hace más de un mes que no se prueba.

La restauración usa `--no-privileges` a propósito: así la base restaurada no depende de qué roles
existían en el servidor viejo. Y por eso vuelve a ejecutar `permisos.sql` al terminar.

## 6. Sonda de salud

`GET /salud` contesta:

| Respuesta | Qué significa |
| --- | --- |
| `200 {"estado":"vivo","aislamiento":"dos barreras"}` | Todo en su sitio. |
| `503 {"base_datos":"sin conexión"}` | El proceso vive y la base no contesta. **No hay que mandarle tráfico.** |
| `503 {"aislamiento":"una sola barrera…"}` | Se conecta como superusuario: falta la mitad del aislamiento. |

Es la ruta del equilibrador de carga y también la del propio contenedor: el `HEALTHCHECK` de la imagen
ejecuta `dotnet Matchketing.Api.dll --comprobar-salud`, que pregunta a `/salud` y sale con 0 o con 1.
Se hace así para no meter `curl` ni un gestor de paquetes en la imagen final.

## 7. Trabajos en segundo plano

Seis trabajos corren dentro del propio proceso (`Trabajos/`):

| Trabajo | Cada | Qué hace |
| --- | --- | --- |
| Barrido de Match | 24 h | Recalcula el Match de todos los contactos: el Momento decae con el tiempo y el tiempo pasa sin que nadie pulse nada. |
| Rebote de leads | 30 min | Reasigna los leads sin atender pasadas las horas laborables configuradas. |
| Retención de leads | 24 h | Borra los leads que han cumplido su plazo de conservación. |
| Aviso del repaso | 30 min | Solo actúa los viernes entre las 18:00 y las 18:59 (hora de España): manda el aviso push a quien tenga decisiones pendientes. |
| Entrega de webhooks | 1 min | Vacía el buzón de salida. Cada minuto porque una integración se espera «ya»; la pasada es una consulta por índice que devuelve cero filas cuando no hay nada. |
| Envío de correos | 1 min | Vacía el buzón de salida del correo, **volviendo a comprobar el permiso de cada destinatario** justo antes de mandar. Decide también si el correo lleva píxel, según el ajuste de la empresa. |

Van dentro del proceso porque a esta escala montar un planificador aparte añade una pieza que puede
fallar sola. **Con varias instancias hay que ejecutarlos en una sola**: ninguno corrompe nada al
solaparse, pero el de retención borraría en paralelo y el de rebote podría reasignar dos veces el
mismo lead. La forma más simple es una variable de entorno que los active solo en una instancia; si
algún día hace falta más, toca un cerrojo en la base.

El del aviso es la excepción: aguanta solaparse sin consecuencias porque su idempotencia no depende
del planificador sino del dato, `SuscripcionAviso.UltimoAvisoEn`. Aunque corra en cuatro instancias,
no salen cuatro avisos. Está hecho así a propósito, porque el día que haya dos procesos y lleguen dos
avisos es el día en que la gente los apaga.

El rebote va cada media hora y no una vez al día por un motivo concreto: un plazo de cuatro horas
laborables que se comprobara solo de madrugada se convertiría en un plazo de un día.

## 8. Límite de intentos de acceso

`/auth/login` y `/auth/contrasena` admiten 20 intentos cada cinco minutos **por IP de origen**. Detrás
de un proxio hay que poner `Proxy:Confiar=true` —ver la sección 4— o todas las peticiones comparten la
IP del proxio y el techo se agota solo: veinte fallos de cualquiera dejan sin entrar a la empresa
entera.

No hay bloqueo por cuenta, y es deliberado: bloquear una cuenta tras N fallos convierte el límite en
un arma contra su dueño —basta con fallar adrede para dejarle fuera—, y ese ataque es más fácil y más
dañino que el que se pretendía evitar.

## 9. CORS

Solo dos grupos de rutas aceptan cualquier origen, y los dos tienen que hacerlo:

* `/f/…` — el formulario de captación se pega en la web del cliente, que es otro dominio.
* `/b/…` — la página de baja se abre desde el gestor de correo.

El resto de la API es de mismo origen. Hay un test de integración que comprueba que las demás rutas no
emiten `Access-Control-Allow-Origin`.

## 10. Zona horaria

Las horas laborables del rebote se cuentan en hora de España (`Europe/Madrid`), y con ellas el «hoy»
de todo el producto (`HorasLaborables.DiaDeTrabajo`). Si el sistema no trae `tzdata`, `HorasLaborables`
se queda en UTC+1 fijo y en verano la franja va corrida una hora: no se cae, pero cuenta mal.

La imagen de `mcr.microsoft.com/dotnet/aspnet:8.0` **sí la trae** —comprobado, y hay un paso de
integración continua que lo comprueba en cada cambio, porque es la clase de cosa que desaparece al
cambiar de imagen base—. Si algún día se cambia por una `alpine` o una `chiseled`, hay que volver a
mirarlo.
