#!/bin/bash
#
# Crea el rol con el que se conecta la aplicación. Se ejecuta **una sola vez**, cuando el contenedor de
# PostgreSQL inicializa un volumen vacío (`/docker-entrypoint-initdb.d`).
#
# El rol no es superusuario y no es dueño de nada, y eso no es una precaución genérica: **es la mitad
# del aislamiento entre empresas**. Las políticas de seguridad por fila que llevan todas las tablas no
# se aplican a un superusuario, así que desplegar con `postgres` deja el producto con una sola barrera
# —el filtro de EF Core— en lugar de dos, y sin que nada falle ni avise.
set -euo pipefail

: "${MK_CLAVE_APP:?hace falta MK_CLAVE_APP para crear el rol de la aplicación}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
     -v clave="$MK_CLAVE_APP" <<'SQL'
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'matchketing_app') THEN
        CREATE ROLE matchketing_app LOGIN;
    END IF;
END
$$;

-- Explícito aunque sea el valor por defecto: quien lea esto tiene que ver que el rol **no** se salta
-- la seguridad por fila y **no** puede crear bases ni roles.
ALTER ROLE matchketing_app NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS NOINHERIT;
SQL

# La contraseña va en una sentencia aparte y parametrizada: interpolarla en el bloque anterior la
# dejaría escrita en los registros de PostgreSQL.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
     -v clave="$MK_CLAVE_APP" \
     -c "ALTER ROLE matchketing_app PASSWORD :'clave'"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<SQL
GRANT CONNECT ON DATABASE "$POSTGRES_DB" TO matchketing_app;

-- Nada de crear objetos en el esquema público. Las tablas las crea el dueño desde una migración.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE ALL ON SCHEMA public FROM matchketing_app;
GRANT USAGE ON SCHEMA public TO matchketing_app;
SQL

echo "rol matchketing_app creado (sin superusuario, sin BYPASSRLS)"
