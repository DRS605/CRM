#!/bin/bash
#
# Restaura una copia de seguridad.
#
#   ./scripts/restaurar.sh --prueba copias/matchketing-….dump   # a una base de usar y tirar
#   ./scripts/restaurar.sh copias/matchketing-….dump            # ENCIMA de la base de verdad
#
# El modo `--prueba` existe porque una copia que nunca se ha restaurado no se sabe si sirve, y probarlo
# encima de la base de producción no es probarlo: es jugárselo. Con `--prueba` se restaura en una base
# nueva, se cuentan las filas y se borra.
#
# El modo normal **pide teclear el nombre de la base**. Es la única operación de este repositorio que
# destruye datos sin vuelta, y un «¿seguro? [s/n]» se contesta sin leerlo.
set -euo pipefail

cd "$(dirname "${BASH_SOURCE[0]}")/.."
[ -f .env ] && set -a && . ./.env && set +a

PRUEBA=0
[ "${1:-}" = "--prueba" ] && { PRUEBA=1; shift; }

COPIA="${1:?uso: ./scripts/restaurar.sh [--prueba] <fichero.dump>}"
[ -f "$COPIA" ] || { echo "✗ no existe $COPIA" >&2; exit 1; }

BASE="${MK_BD_NOMBRE:-matchketing}"
DUENO="${MK_BD_DUENO:-postgres}"
SERVICIO="${MK_BD_SERVICIO:-bd}"
DESTINO="$BASE"
[ "$PRUEBA" = 1 ] && DESTINO="${BASE}_prueba_restauracion"

export PGPASSWORD="${MK_BD_CLAVE_DUENO:?falta MK_BD_CLAVE_DUENO}"

# Igual que en `copia.sh`: por defecto dentro del contenedor, y con `MK_BD_DIRECTO=1` contra un
# PostgreSQL que no está en Docker.
bd() {
  if [ "${MK_BD_DIRECTO:-0}" = "1" ]; then
    local orden="$1"; shift
    "$orden" -h "${MK_BD_ANFITRION:-localhost}" -p "${MK_BD_PUERTO:-5432}" "$@"
  else
    docker compose -f docker-compose.produccion.yml exec -T -e PGPASSWORD="$PGPASSWORD" "$SERVICIO" "$@"
  fi
}

if [ "$PRUEBA" = 0 ]; then
  echo "Esto BORRA la base «$DESTINO» y la deja como estaba en $COPIA. No hay vuelta."
  read -r -p "Escribe el nombre de la base para confirmarlo: " CONFIRMA
  [ "$CONFIRMA" = "$DESTINO" ] || { echo "✗ no coincide; no se ha tocado nada" >&2; exit 1; }
fi

# Restaurar también necesita saltarse las políticas: `pg_restore` inserta como dueño y las tablas
# llevan `FORCE ROW LEVEL SECURITY`, así que cada fila pasaría por el `WITH CHECK` sin empresa activa y
# sería rechazada. Mismo remedio que en `copia.sh`.
# Y crear la base: una restauración empieza por dejar la base como nueva.
if [ "$(bd psql -U "$DUENO" -d postgres -X -q -t -A \
        -c "SELECT (rolsuper OR rolbypassrls) AND (rolsuper OR rolcreatedb) FROM pg_roles WHERE rolname = current_user" \
        | tr -d '[:space:]')" != "t" ]; then
  echo "✗ el rol «$DUENO» no puede restaurar: le falta saltarse las políticas por fila, crear bases, o las dos." >&2
  echo "  Arréglalo con:  ALTER ROLE $DUENO BYPASSRLS CREATEDB;" >&2
  exit 1
fi

echo "Restaurando $COPIA → $DESTINO"

# `--if-exists` para que no proteste si no había nada, y la conexión va a `postgres` porque no se puede
# borrar la base a la que estás conectado.
bd psql -U "$DUENO" -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS \"$DESTINO\" WITH (FORCE)"
bd psql -U "$DUENO" -d postgres -v ON_ERROR_STOP=1 -c "CREATE DATABASE \"$DESTINO\" OWNER \"$DUENO\""

bd pg_restore -U "$DUENO" -d "$DESTINO" --no-owner --no-privileges < "$COPIA"

# **Los permisos del rol de la aplicación no vienen en la copia**, y es a propósito: se restaura con
# `--no-privileges` para que la base restaurada no dependa de qué roles existían en el servidor viejo.
# Así que se vuelven a poner, que es el mismo guion del despliegue.
bd psql -U "$DUENO" -d "$DESTINO" -v ON_ERROR_STOP=1 -f - < scripts/bd/permisos.sql

CONTACTOS=$(bd psql -U "$DUENO" -d "$DESTINO" -X -q -t -A -c "SELECT count(*) FROM contactos.contacto")
EMPRESAS=$(bd psql -U "$DUENO" -d "$DESTINO" -X -q -t -A -c "SELECT count(*) FROM organizacion.empresa")
echo "✓ restaurada: $EMPRESAS empresas y $CONTACTOS contactos"

if [ "$PRUEBA" = 1 ]; then
  bd psql -U "$DUENO" -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE \"$DESTINO\" WITH (FORCE)" >/dev/null
  mkdir -p "$(dirname "$COPIA")"
  touch "$(dirname "$COPIA")/.ultima-restauracion-de-prueba"
  echo "✓ base de prueba retirada. La copia sirve."
fi
