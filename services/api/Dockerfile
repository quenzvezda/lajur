# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/api/src/Lajur.Domain/Lajur.Domain.csproj services/api/src/Lajur.Domain/
COPY services/api/src/Lajur.Application/Lajur.Application.csproj services/api/src/Lajur.Application/
COPY services/api/src/Lajur.Infrastructure/Lajur.Infrastructure.csproj services/api/src/Lajur.Infrastructure/
COPY services/api/src/Lajur.Api/Lajur.Api.csproj services/api/src/Lajur.Api/

RUN dotnet restore services/api/src/Lajur.Api/Lajur.Api.csproj

COPY services/api/ services/api/

RUN dotnet publish services/api/src/Lajur.Api/Lajur.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /srv/lajur/data /srv/lajur/backups /srv/lajur/templates /srv/lajur/auth /srv/lajur/keys \
    && chown -R "${APP_UID}:${APP_UID}" /srv/lajur

WORKDIR /app
COPY --from=build --chown=${APP_UID}:${APP_UID} /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER ${APP_UID}

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "Lajur.Api.dll"]
