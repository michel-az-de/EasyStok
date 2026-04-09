# ─── Stage 1: build ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copiar arquivos de projeto e restaurar (camada cacheada)
COPY EasyStock.Domain/EasyStock.Domain.csproj                 EasyStock.Domain/
COPY EasyStock.Application/EasyStock.Application.csproj       EasyStock.Application/
COPY EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj   EasyStock.Infra.Postgre/
COPY EasyStock.Infra.MongoDb/EasyStock.Infra.MongoDb.csproj   EasyStock.Infra.MongoDb/
COPY EasyStock.Infra.Async/EasyStock.Infra.Async.csproj       EasyStock.Infra.Async/
COPY EasyStock.Api/EasyStock.Api.csproj                       EasyStock.Api/

RUN dotnet restore EasyStock.Api/EasyStock.Api.csproj

# Copiar tudo e publicar
COPY . .
WORKDIR /src/EasyStock.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# ─── Stage 2: runtime ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Criar usuario nao-root
RUN addgroup --system --gid 1001 appgroup \
 && adduser  --system --uid 1001 --ingroup appgroup appuser

COPY --from=build /app/publish .

# Volume para armazenamento local de arquivos
RUN mkdir -p /app/uploaded-files && chown -R appuser:appgroup /app
USER appuser

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "EasyStock.Api.dll"]
