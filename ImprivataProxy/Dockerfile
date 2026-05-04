# syntax=docker/dockerfile:1.6

# =========================================================================
# Build stage
# =========================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Layer-cache the NuGet restore by copying only the csproj first.
COPY src/ImprivataProxy/ImprivataProxy.csproj src/ImprivataProxy/
RUN dotnet restore src/ImprivataProxy/ImprivataProxy.csproj

# Copy the rest and publish.
COPY src/ src/
RUN dotnet publish src/ImprivataProxy/ImprivataProxy.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# =========================================================================
# Runtime stage
# =========================================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl for healthchecks; non-root user; writable dirs for SQLite / certs / logs.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd -g 1000 app \
    && useradd -u 1000 -g app -s /bin/bash -m app \
    && mkdir -p /app/data /app/certs /app/logs \
    && chown -R app:app /app

COPY --from=build --chown=app:app /app/publish .

USER app

# Listen inside the container on 8080 (non-privileged). Map via -p on the host.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "ImprivataProxy.dll"]
