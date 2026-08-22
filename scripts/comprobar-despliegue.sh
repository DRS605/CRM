#!/bin/bash
#
# Comprueba un despliegue **desde fuera**, como lo ve un navegador.
#
#   ./scripts/comprobar-despliegue.sh https://crm.tuempresa.es
#   ./scripts/comprobar-despliegue.sh                          # http://localhost:8080 por defecto
#
# Son comprobaciones humildes y todas ellas de cosas que fallan **en silencio**: un despliegue con una
# barrera de aislamiento menos, sin cabeceras de seguridad o con la documentación de la API abierta
# funciona igual de bien y no se nota desde dentro.
set -euo pipefail

BASE="${1:-http://localhost:8080}"
fallo() { echo "✗ $1" >&2; exit 1; }

echo "Comprobando $BASE…"

# 1. La sonda. No vale con un 200: tiene que decir que están las dos barreras del aislamiento.
SALUD=$(curl -fsS "$BASE/salud") || fallo "la sonda de salud no contesta 200 (mira los registros)"
echo "$SALUD" | grep -q '"aislamiento":"dos barreras"' \
  || fallo "la sonda dice: $SALUD"
echo "✓ sonda de salud: viva, y con las dos barreras del aislamiento"

# 2. Las cabeceras de seguridad, en la página.
CABECERAS=$(curl -fsSI "$BASE/")
for cabecera in "x-content-type-options: nosniff" "x-frame-options: DENY" "content-security-policy:"; do
  echo "$CABECERAS" | tr 'A-Z' 'a-z' | grep -q "$(echo "$cabecera" | tr 'A-Z' 'a-z')" \
    || fallo "falta la cabecera «$cabecera»"
done
echo "✓ cabeceras de seguridad puestas"

# 3. La documentación de la API **no** puede estar abierta: es el mapa de todos los endpoints.
CODIGO=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/swagger/index.html")
[ "$CODIGO" = "404" ] || fallo "/swagger contesta $CODIGO: la documentación de la API está expuesta"
echo "✓ la documentación de la API no está expuesta"

# 4. La aplicación se sirve entera: la página, su guion de servicio y las letras.
for ruta in / /sw.js /manifiesto.webmanifest; do
  curl -fsS -o /dev/null "$BASE$ruta" || fallo "$ruta no se sirve"
done
LETRA=$(curl -fsS "$BASE/" | grep -o "/tipos/[a-z-]*\.woff2" | head -1)
[ -n "$LETRA" ] || fallo "la página no declara ninguna letra"
curl -fsS -o /dev/null "$BASE$LETRA" || fallo "$LETRA está declarada y no se sirve"
echo "✓ la aplicación, el trabajador de servicio, el manifiesto y las letras se sirven"

# 5. Si es HTTPS, que el HTTP lleve allí. Un formulario de captación pegado con `http://` seguiría
#    funcionando y mandaría los datos de una persona en claro.
if [ "${BASE#https://}" != "$BASE" ]; then
  SIN_TLS="http://${BASE#https://}"
  REDIRIGE=$(curl -s -o /dev/null -w '%{http_code}' "$SIN_TLS/salud")
  case "$REDIRIGE" in
    30*) echo "✓ HTTP redirige a HTTPS ($REDIRIGE)" ;;
    *) fallo "http:// contesta $REDIRIGE en vez de redirigir a https://" ;;
  esac
fi

echo "Despliegue correcto."
