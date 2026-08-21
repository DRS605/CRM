#!/bin/bash
#
# Comprueba que el aislamiento entre empresas tiene **las dos barreras**, no una.
#
# El filtro global de EF Core lo prueban los tests de integración, que corren como superusuario y
# por tanto con la RLS de PostgreSQL efectivamente desactivada: si pasan, el filtro basta por sí solo.
# Este script prueba la otra mitad, la que ningún test de C# puede probar —porque para probarla hay
# que conectarse con otro rol— y que es la que se cae en silencio en producción si alguien despliega
# con el usuario `postgres`.
#
# Uso:
#   ./scripts/comprobar-aislamiento.sh [base] [usuario-admin] [contrasena-admin] [host] [puerto]
#
# Por defecto, la base de los tests de integración en un PostgreSQL local. Hay que ejecutarlo
# **después** de que existan las tablas y con algún dato dentro.
set -euo pipefail

BASE="${1:-matchketing_test}"
ADMIN="${2:-postgres}"
CLAVE_ADMIN="${3:-postgres}"
HOST="${4:-localhost}"
PUERTO="${5:-5432}"

ROL="mk_comprobacion_aislamiento"
CLAVE_ROL="comprobacion-$RANDOM$RANDOM"

adm() { PGPASSWORD="$CLAVE_ADMIN" psql -U "$ADMIN" -h "$HOST" -p "$PUERTO" -d "$BASE" -X -q -t -A "$@"; }
app() { PGPASSWORD="$CLAVE_ROL" psql -U "$ROL" -h "$HOST" -p "$PUERTO" -d "$BASE" -X -q -t -A "$@"; }

fallo() { echo "✗ $1" >&2; exit 1; }

limpiar() {
  adm -c "REVOKE ALL ON DATABASE \"$BASE\" FROM $ROL" >/dev/null 2>&1 || true
  adm -c "DROP OWNED BY $ROL" >/dev/null 2>&1 || true
  adm -c "DROP ROLE IF EXISTS $ROL" >/dev/null 2>&1 || true
}
trap limpiar EXIT

echo "Comprobando el aislamiento en $BASE…"
limpiar

adm -c "CREATE ROLE $ROL LOGIN PASSWORD '$CLAVE_ROL'" >/dev/null
adm -c "GRANT CONNECT ON DATABASE \"$BASE\" TO $ROL" >/dev/null
adm <<SQL >/dev/null
DO \$\$
DECLARE esquema text;
BEGIN
    -- Los esquemas se sacan de la base, **no de una lista escrita aquí**. La lista se quedaba obsoleta
    -- con cada módulo nuevo: al añadir avisos, webhooks y correo, sus tablas no recibían permisos y el
    -- guion moría con «permission denied» en vez de comprobarlas. Una lista que hay que acordarse de
    -- ampliar es una lista que falla abierto, y en una comprobación de aislamiento eso es lo peor que
    -- puede pasar: parecería que pasa cuando no ha mirado nada.
    FOR esquema IN
        SELECT nspname FROM pg_namespace
        WHERE nspname NOT LIKE 'pg\\_%'
          AND nspname NOT IN ('information_schema', 'public')
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO $ROL', esquema);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO $ROL', esquema);
    END LOOP;
END \$\$;
SQL

# 0. El rol tiene que ser un rol normal. Si es superusuario, todo lo demás pasaría por el motivo
#    equivocado y la comprobación no valdría nada.
[ "$(app -c "SELECT usesuper FROM pg_user WHERE usename = current_user")" = "f" ] \
  || fallo "el rol de comprobación es superusuario; la RLS no se le aplica y la prueba no vale"

[ "$(adm -c "SELECT to_regclass('contactos.contacto') IS NOT NULL")" = "t" ] \
  || fallo "en $BASE no están las tablas; aplica las migraciones o ejecuta antes 'dotnet test'"

# 1. Sin empresa activa, ni una fila. Es el «falla cerrado»: si esto devuelve datos, cualquier
#    petición sin token o con un token roto vería la base entera.
for tabla in contactos.contacto embudo.oportunidad tareas.tarea auditoria.registro cumplimiento.consentimiento avisos.suscripcion webhooks.suscripcion webhooks.entrega correo.plantilla correo.mensaje automatizacion.regla automatizacion.ejecucion identidad.invitacion campania.segmento campania.campania campania.envio; do
  visibles=$(app -c "SET app.empresa_actual = ''; SELECT count(*) FROM $tabla")
  [ "$visibles" = "0" ] || fallo "sin empresa activa se ven $visibles filas de $tabla (deberían ser 0)"
done
echo "✓ sin empresa activa no se ve ninguna fila"

# 2. Con una empresa activa se ven las suyas… y solo las suyas. Se compara contra el recuento real,
#    que solo puede sacar el administrador porque a él la RLS no le aplica.
EMPRESA=$(adm -c "SELECT empresa_id FROM contactos.contacto GROUP BY empresa_id ORDER BY count(*) DESC LIMIT 1")
[ -n "$EMPRESA" ] || fallo "no hay ningún contacto en $BASE; ejecuta antes 'dotnet test'"

SUYOS=$(adm -c "SELECT count(*) FROM contactos.contacto WHERE empresa_id = '$EMPRESA'")
TOTAL=$(adm -c "SELECT count(*) FROM contactos.contacto")
VISTOS=$(app -c "SELECT set_config('app.empresa_actual', '$EMPRESA', false); SELECT count(*) FROM contactos.contacto" | tail -1)

[ "$VISTOS" = "$SUYOS" ] || fallo "con la empresa activa se ven $VISTOS contactos y debería ver $SUYOS"
[ "$TOTAL" -gt "$SUYOS" ] || fallo "solo hay una empresa con contactos en $BASE: la prueba no demuestra nada"
echo "✓ con empresa activa se ven sus $SUYOS contactos y no los $((TOTAL - SUYOS)) de las demás"

# 3. La auditoría no se puede tocar. El disparador deja pasar al propietario de la tabla a propósito
#    (migraciones y borrado de empresa), así que esto solo se puede comprobar con otro rol.
#
#    Ojo con la empresa que se elige: el disparador es BEFORE ... FOR EACH ROW, así que con una empresa
#    **sin líneas de auditoría** la RLS no deja ver ninguna fila, el UPDATE afecta a cero y el
#    disparador nunca salta. La comprobación diría «se ha podido modificar» sin que sea verdad. Por eso
#    aquí se coge la empresa con más apuntes, y se exige que tenga alguno.
EMPRESA_AUD=$(adm -c "SELECT empresa_id FROM auditoria.registro GROUP BY empresa_id ORDER BY count(*) DESC LIMIT 1")
[ -n "$EMPRESA_AUD" ] || fallo "no hay ninguna línea en auditoria.registro; ejecuta antes 'dotnet test'"

APUNTES=$(app -c "SELECT set_config('app.empresa_actual', '$EMPRESA_AUD', false); SELECT count(*) FROM auditoria.registro" | tail -1)
[ "$APUNTES" -gt 0 ] || fallo "el rol no ve los apuntes de su empresa: la comprobación siguiente no valdría"

app -c "SET app.empresa_actual = '$EMPRESA_AUD'; UPDATE auditoria.registro SET detalle = 'retocado'" 2>/dev/null \
  && fallo "se han podido modificar los $APUNTES apuntes visibles de auditoria.registro"
app -c "SET app.empresa_actual = '$EMPRESA_AUD'; DELETE FROM auditoria.registro" 2>/dev/null \
  && fallo "se han podido borrar los $APUNTES apuntes visibles de auditoria.registro"
echo "✓ el registro de auditoría rechaza UPDATE y DELETE sobre sus $APUNTES apuntes"

# 4. Y sí acepta INSERT: una tabla append-only que no admite añadir no sirve de nada.
app -c "SET app.empresa_actual = '$EMPRESA_AUD';
        INSERT INTO auditoria.registro (id, empresa_id, actor_id, entidad, entidad_id, accion, detalle, en)
        VALUES (gen_random_uuid(), '$EMPRESA_AUD', NULL, 'prueba', NULL, 'prueba.aislamiento', NULL, now())" >/dev/null \
  || fallo "el registro de auditoría no acepta INSERT"
adm -c "DELETE FROM auditoria.registro WHERE accion = 'prueba.aislamiento'" >/dev/null
echo "✓ el registro de auditoría sí acepta INSERT"

echo "Aislamiento correcto: las dos barreras están puestas."
