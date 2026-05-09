#!/bin/sh
# Entrypoint do container EasyStock.Api.
# Aplica EF migrations via bundle (gerado em build) ANTES de iniciar a API.
# Falha do bundle = exit != 0 = container reinicia e o host (Render/Azure/k8s)
# mostra os logs. Subir com schema parcial mascara erros como "Credenciais
# invalidas" no front, entao fail-fast aqui e' melhor que continuar mancando.
set -e

echo ""
echo "================================================================================"
echo "  EasyStok API - container startup"
echo "  ASPNETCORE_ENVIRONMENT = ${ASPNETCORE_ENVIRONMENT:-(unset)}"
echo "  Database__Provider     = ${Database__Provider:-(unset)}"
echo "  RunMigrationsOnStartup = ${RunMigrationsOnStartup:-(unset)}"
echo "================================================================================"

if [ -z "$ConnectionStrings__DefaultConnection" ]; then
  echo "[entrypoint] ERRO: ConnectionStrings__DefaultConnection nao definida -- abortando."
  exit 1
fi

# Permite pular o bundle (uso raro: rollback emergencial em que migration nova quebra
# e voce precisa subir o app antigo sem aplicar a migration nova). Default = aplica.
if [ "${SKIP_EF_BUNDLE:-false}" = "true" ]; then
  echo "[entrypoint] SKIP_EF_BUNDLE=true -- pulando aplicacao de migrations no startup."
else
  echo ""
  echo "[entrypoint] >>> Aplicando EF migrations via bundle..."
  echo "[entrypoint] >>> Bundle: /app/efbundle"
  date -u +"[entrypoint] >>> Inicio: %Y-%m-%dT%H:%M:%SZ"

  # --verbose imprime cada migration aplicada e o SQL executado.
  # Connection passada inline pra nao depender de interpolacao de env vars no bundle.
  if /app/efbundle --connection "$ConnectionStrings__DefaultConnection" --verbose; then
    date -u +"[entrypoint] >>> Sucesso: %Y-%m-%dT%H:%M:%SZ"
    echo "[entrypoint] >>> Migrations aplicadas com sucesso."
  else
    exit_code=$?
    date -u +"[entrypoint] >>> Falha: %Y-%m-%dT%H:%M:%SZ"
    echo "[entrypoint] !!! BUNDLE EF FALHOU (exit=$exit_code). Container vai reiniciar."
    echo "[entrypoint] !!! Verifique credenciais de conexao e estado do banco antes do proximo deploy."
    exit $exit_code
  fi
fi

echo ""
echo "[entrypoint] >>> Iniciando API..."
exec dotnet EasyStock.Api.dll
