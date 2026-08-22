# La imagen de la aplicación. Dos etapas: se compila con el SDK y se ejecuta con el runtime, que pesa
# la cuarta parte y no lleva compilador dentro —una imagen de producción con un SDK es una imagen con
# herramientas que no hacen falta y que sí se pueden usar si alguien entra—.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS construccion
WORKDIR /src

# Primero los ficheros de proyecto y luego el resto: así la capa de `restore` se reaprovecha mientras
# no cambie ninguna dependencia, que es casi siempre.
COPY Directory.Build.props global.json Matchketing.sln ./
COPY .config ./.config
COPY src/ ./src/
COPY tests/ ./tests/

RUN dotnet restore src/Matchketing.Api/Matchketing.Api.csproj

# `--no-restore` para que no vuelva a resolver lo que acaba de resolverse.
RUN dotnet publish src/Matchketing.Api/Matchketing.Api.csproj \
    -c Release -o /app/publicado --no-restore

# El paquete de migraciones: un ejecutable que aplica las migraciones **sin SDK y sin el código de la
# aplicación**. Es lo que permite que las migraciones las ejecute un contenedor aparte con el rol dueño
# de la base, y que la aplicación arranque con un rol que no puede crear ni una tabla.
RUN dotnet tool restore \
 && dotnet ef migrations bundle \
    --project src/Matchketing.Persistencia \
    --startup-project src/Matchketing.Api \
    --configuration Release \
    --self-contained -r linux-x64 \
    --output /app/migrar

# ---------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=construccion /app/publicado ./
COPY --from=construccion /app/migrar ./migrar

# **No arranca como root.** La imagen de aspnet trae el usuario `app` (uid 1654) preparado para esto.
# Un proceso que sirve peticiones de internet no necesita poder escribir en su propio código.
USER app

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1
EXPOSE 8080

# La sonda la hace **la propia aplicación**: `--comprobar-salud` pregunta a `/salud` y sale con 0 o 1.
# Así la imagen no lleva `curl` ni un gestor de paquetes dentro, y la construcción no necesita los
# repositorios de Debian. Y «sano» significa de verdad «puede atender»: `/salud` pregunta a la base de
# datos y comprueba que están las dos barreras del aislamiento.
HEALTHCHECK --interval=15s --timeout=8s --start-period=25s --retries=4 \
  CMD ["dotnet", "Matchketing.Api.dll", "--comprobar-salud"]

ENTRYPOINT ["dotnet", "Matchketing.Api.dll"]
