#!/usr/bin/env bash
# =============================================================================
# azure-deploy.sh - deploy canonico do EasyStok na VM Azure (easystok-vm).
#
# Garante que o deploy SEMPRE sobe o estado novo do projeto (issue #478). As
# regressoes "que parecem cache" vinham de scripts que buildavam o checkout
# velho da VM sem dar git pull, e da falta de qualquer verificacao do que subiu.
#
# Este script e a UNICA forma suportada de deployar na VM. Ele:
#   1. reset --hard origin/master   -> nunca builda checkout velho
#   2. build --no-cache             -> zero cache de layer do Docker
#   3. up -d --force-recreate       -> recria o container mesmo se a imagem nao mudou
#   4. verifica /health.commit == HEAD -> falha ruidosamente se nao refletiu
#
# Uso (na VM):
#   ./scripts/deploy/azure-deploy.sh            # api web admin (default)
#   ./scripts/deploy/azure-deploy.sh web        # so o web
#   ./scripts/deploy/azure-deploy.sh api web    # subconjunto
#
# Roda tanto via SSH (usuario azureuser) quanto via `az vm run-command` (root):
# o git executa sempre como dono do checkout e o docker usa sudo so se preciso.
# =============================================================================
set -euo pipefail

REPO_DIR="${REPO_DIR:-/home/azureuser/easystok}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.azure.yml}"
SERVICES="${*:-api web admin}"

cd "$REPO_DIR"

# git sempre como dono do checkout: evita "dubious ownership" e nao converte o
# owner dos arquivos para root quando o script roda via az vm run-command.
OWNER="$(stat -c '%U' "$REPO_DIR")"
git_() {
  if [ "$(id -un)" = "$OWNER" ]; then git "$@"; else sudo -u "$OWNER" git "$@"; fi
}
# docker sem sudo se ja houver acesso (root ou grupo docker); senao com sudo.
if docker info >/dev/null 2>&1; then DOCKER=(docker); else DOCKER=(sudo docker); fi
dc() { "${DOCKER[@]}" compose -f "$COMPOSE_FILE" "$@"; }

echo "==> [1/5] Atualizando codigo (fetch + reset --hard origin/master + clean)"
git_ fetch origin --quiet
git_ reset --hard origin/master
git_ clean -fdq -e .env
GIT_SHA="$(git_ rev-parse HEAD)"
export GIT_SHA                       # consumido como build-arg pelo docker-compose.azure.yml
echo "    alvo: $(git_ rev-parse --short HEAD)  $(git_ log -1 --format='%s')"

echo "==> [2/5] Build --no-cache: $SERVICES"
# shellcheck disable=SC2086  # word splitting intencional (lista de servicos)
dc build --no-cache $SERVICES

echo "==> [3/5] up -d --force-recreate: $SERVICES"
# shellcheck disable=SC2086
dc up -d --force-recreate $SERVICES

echo "==> [4/5] Estado dos containers"
dc ps

# Verificacao pos-deploy. Apos #572 os 3 containers (web/api/admin) carimbam GIT_SHA
# no env; o deploy so e "sucesso" se o commit no ar == HEAD. Dois sinais:
#   (a) env GIT_SHA de cada container == HEAD (uniforme, todos os servicos);
#   (b) prova end-to-end de que o app SERVE o commit (web /health.commit, api
#       /health/version.buildSha) — pega container 'no env certo mas processo velho'.
echo "==> [5/5] Verificando que o commit no ar == $(git_ rev-parse --short HEAD)"
VERIFY_FAIL=0

for svc in $SERVICES; do
  case "$svc" in
    api)   cname=easystok-api ;;
    web)   cname=easystok-web ;;
    admin) cname=easystok-admin ;;
    *)     continue ;;
  esac
  CSHA="$("${DOCKER[@]}" inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$cname" 2>/dev/null | sed -n 's/^GIT_SHA=//p' | head -1)"
  if [ "$CSHA" = "$GIT_SHA" ]; then
    echo "    OK    $cname env GIT_SHA confere"
  else
    echo "    FALHA $cname env GIT_SHA='${CSHA:-<ausente>}' != HEAD"
    VERIFY_FAIL=1
  fi
done

case " $SERVICES " in
  *" web "*)
    WEBIP="$("${DOCKER[@]}" inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' easystok-web 2>/dev/null || true)"
    SERVED=""
    for _ in $(seq 1 20); do
      BODY="$(curl -s -m 5 "http://${WEBIP}:8081/health" 2>/dev/null || true)"
      SERVED="$(printf '%s' "$BODY" | grep -o '"commit":"[^"]*"' | cut -d'"' -f4 || true)"
      [ -n "$SERVED" ] && [ "$SERVED" != "unknown" ] && break
      sleep 3
    done
    if [ "$SERVED" = "$GIT_SHA" ]; then echo "    OK    web /health.commit == HEAD"
    else echo "    FALHA web /health.commit='${SERVED:-<vazio>}' != HEAD"; VERIFY_FAIL=1; fi
    ;;
esac

case " $SERVICES " in
  *" api "*)
    APIIP="$("${DOCKER[@]}" inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' easystok-api 2>/dev/null || true)"
    SERVED=""
    for _ in $(seq 1 20); do
      BODY="$(curl -s -m 5 "http://${APIIP}:8080/health/version" 2>/dev/null || true)"
      SERVED="$(printf '%s' "$BODY" | grep -o '"buildSha":"[^"]*"' | cut -d'"' -f4 || true)"
      [ -n "$SERVED" ] && [ "$SERVED" != "unknown" ] && [ "$SERVED" != "master" ] && break
      sleep 3
    done
    if [ "$SERVED" = "$GIT_SHA" ]; then echo "    OK    api /health/version.buildSha == HEAD"
    else echo "    FALHA api /health/version.buildSha='${SERVED:-<vazio>}' != HEAD"; VERIFY_FAIL=1; fi
    ;;
esac

if [ "$VERIFY_FAIL" -ne 0 ]; then
  echo "    O deploy NAO refletiu em algum container/endpoint. Investigar antes de confiar."
  exit 1
fi

echo "==> DEPLOY OK - VM em $(git_ rev-parse --short HEAD)"
