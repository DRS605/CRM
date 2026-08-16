#!/bin/bash
# Igual que dn.sh pero ejecuta un comando de shell completo dentro del contenedor del SDK.
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
  -e PATH="/tmp/.dotnet/tools:/usr/share/dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin" \
  mcr.microsoft.com/dotnet/sdk:8.0 bash -lc "$1"
