#!/bin/bash
# Ejecuta el SDK de .NET 8 vía Docker: en este entorno no hay SDK instalado y la salida a
# internet pasa por el proxy del agente, así que hay que pasarle el proxy y el CA.
exec docker run --rm --network host \
  -u "$(id -u):$(id -g)" \
  -v /home/user/ERP/matchketing:/src \
  -v /root/.ccr/ca-bundle.crt:/ca/ca-bundle.crt:ro \
  -v /home/user/ERP/matchketing/.nuget:/tmp/.nuget \
  -w /src \
  -e HOME=/tmp -e DOTNET_CLI_HOME=/tmp -e XDG_DATA_HOME=/tmp \
  -e NUGET_PACKAGES=/tmp/.nuget/packages \
  -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e DOTNET_NOLOGO=1 \
  -e HTTP_PROXY="$HTTP_PROXY" -e HTTPS_PROXY="$HTTPS_PROXY" -e NO_PROXY="$NO_PROXY" \
  -e http_proxy="$HTTP_PROXY" -e https_proxy="$HTTPS_PROXY" -e no_proxy="$NO_PROXY" \
  -e SSL_CERT_FILE=/ca/ca-bundle.crt \
  mcr.microsoft.com/dotnet/sdk:8.0 dotnet "$@"
