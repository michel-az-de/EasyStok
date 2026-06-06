#!/bin/sh
# Entrypoint do container EasyStock.Api.
# Loga estado das env vars e inicia a API. Migrations sao aplicadas pelo
# proprio app no startup (Program.cs) com logging detalhado e MigrationsFailFast
# opcional pra abortar startup se algo falhar.
set -e

echo ""
echo "================================================================================"
echo "  EasyStok API - container startup"
echo "  ASPNETCORE_ENVIRONMENT = ${ASPNETCORE_ENVIRONMENT:-(unset)}"
echo "  Database__Provider     = ${Database__Provider:-(unset)}"
echo "  RunMigrationsOnStartup = ${RunMigrationsOnStartup:-(unset)}"
echo "  MigrationsFailFast     = ${MigrationsFailFast:-(unset)}"
echo "================================================================================"

# Garante que o diretorio de uploads (volume montado) seja gravavel pelo usuario nao-root.
# Roda como root aqui; o processo .NET e iniciado via gosu como appuser mais abaixo. No Fly
# o volume monta root-owned; na VM (named volume) o chown e no-op (ja appuser).
UPLOAD_DIR="/app/uploaded-files"
mkdir -p "$UPLOAD_DIR"
chown -R appuser:appgroup "$UPLOAD_DIR" 2>/dev/null || true

if [ -z "$ConnectionStrings__DefaultConnection" ]; then
  echo "[entrypoint] ERRO: ConnectionStrings__DefaultConnection nao definida -- abortando."
  exit 1
fi

# Modo migrate-only (release_command do deploy): aplica migrations e encerra,
# sem subir o servidor. Se falhar, o deploy e abortado (versao antiga continua).
if [ "$1" = "--migrate-only" ]; then
  echo "[entrypoint] >>> Modo migrate-only: aplicando migrations e encerrando..."
  exec gosu appuser dotnet EasyStock.Api.dll --migrate-only
fi

echo "[entrypoint] >>> Iniciando API (migrations rodam no startup do app)..."
exec gosu appuser dotnet EasyStock.Api.dll
