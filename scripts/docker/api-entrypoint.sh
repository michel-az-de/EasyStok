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

if [ -z "$ConnectionStrings__DefaultConnection" ]; then
  echo "[entrypoint] ERRO: ConnectionStrings__DefaultConnection nao definida -- abortando."
  exit 1
fi

# Modo migrate-only (release_command do deploy): aplica migrations e encerra,
# sem subir o servidor. Se falhar, o deploy e abortado (versao antiga continua).
if [ "$1" = "--migrate-only" ]; then
  echo "[entrypoint] >>> Modo migrate-only: aplicando migrations e encerrando..."
  exec dotnet EasyStock.Api.dll --migrate-only
fi

echo "[entrypoint] >>> Iniciando API (migrations rodam no startup do app)..."
exec dotnet EasyStock.Api.dll
