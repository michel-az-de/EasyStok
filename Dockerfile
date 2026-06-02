# syntax=docker/dockerfile:1.7
# ─── Stage 1: build + publish ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copiar arquivos de projeto (camada cacheada — so invalida quando .csproj muda)
COPY EasyStock.Domain/EasyStock.Domain.csproj                 EasyStock.Domain/
COPY EasyStock.Application/EasyStock.Application.csproj       EasyStock.Application/
COPY EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj   EasyStock.Infra.Postgre/
COPY EasyStock.Infra.Async/EasyStock.Infra.Async.csproj       EasyStock.Infra.Async/
COPY EasyStock.Infra.Notifications/EasyStock.Infra.Notifications.csproj EasyStock.Infra.Notifications/
COPY EasyStock.Contracts/EasyStock.Contracts.csproj           EasyStock.Contracts/
COPY EasyStock.Infra.Integrations/EasyStock.Infra.Integrations.csproj EasyStock.Infra.Integrations/
COPY EasyStock.Api/EasyStock.Api.csproj                       EasyStock.Api/

# Restore com cache mount BuildKit (persiste pacotes NuGet entre builds)
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget \
    dotnet restore EasyStock.Api/EasyStock.Api.csproj

# Copiar codigo fonte (separado dos .csproj pra cache mais granular)
COPY EasyStock.Domain/             EasyStock.Domain/
COPY EasyStock.Application/        EasyStock.Application/
COPY EasyStock.Infra.Postgre/      EasyStock.Infra.Postgre/
COPY EasyStock.Infra.Async/        EasyStock.Infra.Async/
COPY EasyStock.Infra.Notifications/ EasyStock.Infra.Notifications/
COPY EasyStock.Infra.Integrations/ EasyStock.Infra.Integrations/
COPY EasyStock.Contracts/          EasyStock.Contracts/
COPY EasyStock.Api/                EasyStock.Api/

# SW2: carimba CACHE_VERSION no sw.js ANTES do publish (assim os .gz/.br
# precompressados saem consistentes; sed pos-publish deixaria os comprimidos
# stale). O CI do Render roda scripts/rewrite-cache-version.ps1 antes do build
# (cdb-<sha>); o deploy da VM (docker compose up --build) NAO roda esse passo,
# entao o sw.js ia com o literal de dev e o activate do SW nunca purgava caches.
# Aqui: se ainda esta no literal (nao cdb-<12hex>), carimba cdb-<hash de
# conteudo do PWA>. Exclui sw.js do hash (auto-referencia); mudanca no proprio
# sw.js o browser ja detecta por byte-diff do Service Worker.
RUN if [ -f EasyStock.Api/wwwroot/pwa/sw.js ]; then \
      if grep -qE "const CACHE_VERSION = 'cdb-[0-9a-f]{12}'" EasyStock.Api/wwwroot/pwa/sw.js; then \
        echo "[sw2] CACHE_VERSION ja carimbado (CI rewrite) — mantem"; \
      else \
        V="cdb-$(find EasyStock.Api/wwwroot/pwa -type f ! -name 'sw.js' -print0 | sort -z | xargs -0 cat | sha256sum | cut -c1-12)"; \
        sed -i "s/const CACHE_VERSION = '[^']*'/const CACHE_VERSION = '$V'/" EasyStock.Api/wwwroot/pwa/sw.js; \
        echo "[sw2] CACHE_VERSION carimbado: $V"; \
      fi; \
    fi

WORKDIR /src/EasyStock.Api
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget \
    dotnet publish -c Release -o /app/publish --no-restore

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
