#!/bin/bash
# Ejecuta el SDK de .NET 8 vía Docker, para entornos sin SDK instalado.
# La raíz del repositorio se deduce del propio script, así que funciona desde cualquier ruta.
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CA_ARGS=()
[ -f /root/.ccr/ca-bundle.crt ] && CA_ARGS=(-v /root/.ccr/ca-bundle.crt:/ca/ca-bundle.crt:ro -e SSL_CERT_FILE=/ca/ca-bundle.crt)

exec docker run --rm --network host \
  -u "$(id -u):$(id -g)" \
  -v "$RAIZ":/src -v "$RAIZ/.nuget":/tmp/.nuget -w /src \
  "${CA_ARGS[@]}" \
  -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e XDG_DATA_HOME=/tmp \
  -e NUGET_PACKAGES=/tmp/.nuget/packages \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  -e HTTP_PROXY="$HTTP_PROXY" -e HTTPS_PROXY="$HTTPS_PROXY" -e NO_PROXY="$NO_PROXY" \
  -e http_proxy="$HTTP_PROXY" -e https_proxy="$HTTPS_PROXY" -e no_proxy="$NO_PROXY" \
  mcr.microsoft.com/dotnet/sdk:8.0 dotnet "$@"
