#!/bin/bash
#
# Copia de seguridad de la base de datos.
#
#   ./scripts/copia.sh                    # con la configuración del .env
#   ./scripts/copia.sh /ruta/de/destino
#
# Tres decisiones dentro:
#
# 1. **Formato propio de PostgreSQL** (`-Fc`), no un `.sql` de texto. Va comprimido, permite restaurar
#    una sola tabla y `pg_restore --list` puede leer su índice, que es lo que hace posible el punto 3.
# 2. **La copia se verifica al hacerla.** Un fichero que no se puede leer no es una copia, es un
#    fichero; y eso se descubre el día que hace falta o se descubre hoy.
# 3. **Se borra lo viejo, pero solo si la nueva vale.** El orden importa: primero verificar, después
#    limpiar. Al revés, un fallo de red se lleva por delante el historial entero.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
[ -f .env ] && set -a && . ./.env && set +a

DESTINO="${1:-copias}"
DIAS="${MK_COPIAS_DIAS:-14}"
BASE="${MK_BD_NOMBRE:-matchketing}"
DUENO="${MK_BD_DUENO:-postgres}"
SERVICIO="${MK_BD_SERVICIO:-bd}"

mkdir -p "$DESTINO"
# La marca de tiempo va en UTC: un servidor que cambia de hora en marzo no puede dejar dos copias con
# el mismo nombre ni una hora sin copia.
NOMBRE="$DESTINO/matchketing-$(date -u +%Y%m%dT%H%M%SZ).dump"

echo "Copiando $BASE → $NOMBRE"

# Por defecto se ejecuta **dentro del contenedor de la base**: así no hace falta publicar el puerto ni
# tener `pg_dump` en el servidor, y la versión de las herramientas es exactamente la del servidor.
#
# Con `MK_BD_DIRECTO=1` se habla con un PostgreSQL que no está en Docker. Existe porque hay
# instalaciones así y porque es el único modo que se puede probar donde no hay Docker; el camino de
# producción es el otro.
export PGPASSWORD="${MK_BD_CLAVE_DUENO:?falta MK_BD_CLAVE_DUENO}"

# **Una copia necesita saltarse la seguridad por fila, y hay que decirlo.**
#
# Todas las tablas llevan `FORCE ROW LEVEL SECURITY`, que aplica las políticas **también al dueño**. Eso
# es lo que se quiere para los datos —si alguien despliega con el rol dueño, el aislamiento sigue
# puesto— y tiene una consecuencia que no se ve venir: `pg_dump` lee con `COPY`, y `COPY` sobre una
# tabla con la política puesta y sin empresa activa devuelve
# «query would be affected by row-level security policy». O sea: **sin esto, no hay copias**, y se
# descubre el día que hacen falta.
#
# La salida no es quitar `FORCE`: es que el rol que copia pueda saltarse las políticas. Dos formas, de
# menos a más privilegio: `ALTER ROLE <rol> BYPASSRLS` —lo justo— o un superusuario. En el
# `docker compose` de producción el dueño es el usuario inicial de PostgreSQL, que ya es superusuario,
# así que esto pasa solo; en una instalación a mano hay que darlo.
puede() {
  if [ "${MK_BD_DIRECTO:-0}" = "1" ]; then
    psql -h "${MK_BD_ANFITRION:-localhost}" -p "${MK_BD_PUERTO:-5432}" -U "$DUENO" -d "$BASE" -X -q -t -A \
      -c "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"
  else
    docker compose -f docker-compose.produccion.yml exec -T -e PGPASSWORD="$PGPASSWORD" "$SERVICIO" \
      psql -U "$DUENO" -d "$BASE" -X -q -t -A \
      -c "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"
  fi
}

if [ "$(puede | tr -d '[:space:]')" != "t" ]; then
  echo "✗ el rol «$DUENO» no puede saltarse las políticas por fila, así que no puede copiar la base." >&2
  echo "  Arréglalo con:  ALTER ROLE $DUENO BYPASSRLS;   (o usa un superusuario para las copias)" >&2
  exit 1
fi

if [ "${MK_BD_DIRECTO:-0}" = "1" ]; then
  pg_dump -h "${MK_BD_ANFITRION:-localhost}" -p "${MK_BD_PUERTO:-5432}" -U "$DUENO" -d "$BASE" -Fc > "$NOMBRE"
else
  docker compose -f docker-compose.produccion.yml exec -T \
    -e PGPASSWORD="$PGPASSWORD" "$SERVICIO" pg_dump -U "$DUENO" -d "$BASE" -Fc > "$NOMBRE"
fi

# 1. ¿Tiene contenido?
TAMANO=$(stat -c %s "$NOMBRE")
[ "$TAMANO" -gt 4096 ] || { echo "✗ la copia pesa $TAMANO bytes: algo ha ido mal" >&2; rm -f "$NOMBRE"; exit 1; }

# 2. ¿Se puede leer su índice? Y de paso, ¿lleva dentro las tablas que tiene que llevar?
TABLAS=$(pg_restore --list "$NOMBRE" 2>/dev/null | grep -c "TABLE DATA" || true)
[ "$TABLAS" -gt 10 ] || {
  echo "✗ la copia solo declara $TABLAS tablas con datos: no es una copia completa" >&2
  rm -f "$NOMBRE"; exit 1; }

echo "✓ copia verificada: $(numfmt --to=iec "$TAMANO" 2>/dev/null || echo "$TAMANO B"), $TABLAS tablas con datos"

# 3. Y ahora, y solo ahora, se limpia lo viejo.
BORRADAS=$(find "$DESTINO" -name 'matchketing-*.dump' -type f -mtime "+$DIAS" -print -delete | wc -l)
[ "$BORRADAS" -gt 0 ] && echo "  ($BORRADAS copias de más de $DIAS días retiradas)"

# El recordatorio que ninguna copia da: una copia que nunca se ha restaurado no se sabe si sirve.
ULTIMA_PRUEBA="$DESTINO/.ultima-restauracion-de-prueba"
if [ ! -f "$ULTIMA_PRUEBA" ] || [ "$(find "$ULTIMA_PRUEBA" -mtime +30 | wc -l)" -gt 0 ]; then
  echo "⚠ hace más de un mes que no se prueba una restauración: ./scripts/restaurar.sh --prueba $NOMBRE"
fi
