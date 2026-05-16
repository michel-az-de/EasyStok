# ─── Stage 1: build + publish ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copiar arquivos de projeto e restaurar (camada cacheada)
COPY EasyStock.Domain/EasyStock.Domain.csproj                 EasyStock.Domain/
COPY EasyStock.Application/EasyStock.Application.csproj       EasyStock.Application/
COPY EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj   EasyStock.Infra.Postgre/
COPY EasyStock.Infra.MongoDb/EasyStock.Infra.MongoDb.csproj   EasyStock.Infra.MongoDb/
COPY EasyStock.Infra.Sqlite/EasyStock.Infra.Sqlite.csproj     EasyStock.Infra.Sqlite/
COPY EasyStock.Infra.Async/EasyStock.Infra.Async.csproj       EasyStock.Infra.Async/
COPY EasyStock.Infra.Notifications/EasyStock.Infra.Notifications.csproj EasyStock.Infra.Notifications/
COPY EasyStock.Contracts/EasyStock.Contracts.csproj           EasyStock.Contracts/
COPY EasyStock.Infra.Integrations/EasyStock.Infra.Integrations.csproj EasyStock.Infra.Integrations/
COPY EasyStock.Api/EasyStock.Api.csproj                       EasyStock.Api/

RUN dotnet restore EasyStock.Api/EasyStock.Api.csproj

# Copiar tudo e publicar
COPY . .
WORKDIR /src/EasyStock.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# ─── Stage 2: runtime ────────────────────────────────────────────────────────
# Migrations sao aplicadas pelo proprio app no startup (Program.cs com logging
# gritante + MigrationsFailFast). Sem bundle EF separado pra encurtar build.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Criar usuario nao-root
RUN addgroup --system --gid 1001 appgroup \
 && adduser  --system --uid 1001 --ingroup appgroup appuser

COPY --from=build /app/publish .
COPY scripts/docker/api-entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

# Volume para armazenamento local de arquivos
RUN mkdir -p /app/uploaded-files && chown -R appuser:appgroup /app
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# start-period generoso pra migrations rodarem antes do health check trancar.
HEALTHCHECK --interval=30s --timeout=10s --start-period=120s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["/app/entrypoint.sh"]
