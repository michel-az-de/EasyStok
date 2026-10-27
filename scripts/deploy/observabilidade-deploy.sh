#!/usr/bin/env bash
# =============================================================================
# observabilidade-deploy.sh - OpenObserve + collector OTLP na VPS (#2107, F2). Roda da máquina de dev (Git Bash).
#
# Uso:
#   scripts/deploy/observabilidade-deploy.sh subir     # copia a stack, gera segredos (se faltarem), sobe ez-openobserve e ez-otel
#   scripts/deploy/observabilidade-deploy.sh ligar     # liga o EasyStok: override no COMPOSE_FILE e recria api, web e worker
#   scripts/deploy/observabilidade-deploy.sh desligar  # tira o override e recria api, web e worker sem telemetria
#   scripts/deploy/observabilidade-deploy.sh status    # containers e quanto chegou na última hora
#   scripts/deploy/observabilidade-deploy.sh acesso    # como abrir a UI e onde está a senha
#
# Segredos ficam só na VPS: /opt/stacks/observabilidade/.env (chmod 600). Nenhum valor é impresso.
# O ligar e o desligar usam a mesma trava do vps-deploy.sh e voltam atrás sozinhos se api, web ou worker não ficarem
# saudáveis (ver scripts/deploy/observabilidade/ligar-remoto.sh).
#
# Saídas: 0 ok | 2 uso inválido | 3 deploy em andamento | 4 OpenObserve não respondeu ou compose inválido
#         | 6 EasyStok não ficou saudável (restaurado)
# =============================================================================
set -euo pipefail
VPS_HOST="${VPS_HOST:-hostinger}"
DESTINO="${DESTINO:-/opt/stacks/observabilidade}"
STACK_API="${STACK_API:-/opt/stacks/easystok}"
PORTA="${PORTA:-5080}"
DIR="$(cd "$(dirname "$0")/observabilidade" && pwd)"
ACAO="${1:-}"

uso() { echo "ERRO: $*" >&2; sed -n '/^# Uso:/,/^# Segredos/p' "$0" | sed '$d; s/^# \{0,1\}//' >&2; exit 2; }
remoto() { ssh -o BatchMode=yes "$VPS_HOST" "$@"; }

case "$ACAO" in
  subir)
    echo "==> Preparando $VPS_HOST:$DESTINO"
    remoto "mkdir -p '$DESTINO'"
    scp -q "$DIR/compose.yaml" "$DIR/otel-collector.yaml" "$VPS_HOST:$DESTINO/"

    echo "==> Segredos (gera só o que faltar; nada é impresso)"
    remoto bash -s -- "$DESTINO" <<'REMOTO'
set -euo pipefail
env="$1/.env"
umask 077; touch "$env"; chmod 600 "$env"
tem() { grep -q "^$1=" "$env"; }
tem ZO_ROOT_USER_EMAIL || echo "ZO_ROOT_USER_EMAIL=observabilidade@easystok.online" >>"$env"
# O OpenObserve 1.1 recusa senha sem maiúscula, minúscula, dígito e símbolo (derruba o container no boot).
tem ZO_ROOT_USER_PASSWORD || echo "ZO_ROOT_USER_PASSWORD=Ez-$(openssl rand -hex 16)Q7" >>"$env"
. "$env"
tem OO_AUTH || echo "OO_AUTH=$(printf '%s:%s' "$ZO_ROOT_USER_EMAIL" "$ZO_ROOT_USER_PASSWORD" | base64 -w0)" >>"$env"
echo "    .env com $(grep -c '=' "$env") chaves (600)"
REMOTO

    echo "==> Subindo ez-openobserve e ez-otel (imagens fixadas no compose)"
    remoto "cd '$DESTINO' && docker compose pull -q && docker compose up -d"

    echo "==> Esperando o OpenObserve responder"
    for _ in $(seq 1 40); do
      [ "$(remoto "curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$PORTA/healthz" || true)" = 200 ] && { "$0" status; exit 0; }
      sleep 3
    done
    echo "ERRO: ez-openobserve não respondeu" >&2
    remoto "cd '$DESTINO' && docker compose logs --tail 30" >&2 || true
    exit 4
    ;;

  ligar|desligar)
    if [ "$ACAO" = ligar ]; then
      [ "$(remoto "curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:$PORTA/healthz" || true)" = 200 ] \
        || { echo "ERRO: OpenObserve fora do ar; rode 'subir' antes de ligar" >&2; exit 4; }
      scp -q "$DIR/compose.easystok.yaml" "$VPS_HOST:$STACK_API/compose.observabilidade.yaml"
    fi
    scp -q "$DIR/ligar-remoto.sh" "$VPS_HOST:/tmp/ez-obs-ligar.sh"
    remoto "bash /tmp/ez-obs-ligar.sh '$STACK_API' $ACAO"
    ;;

  status)
    remoto "docker ps -a --filter name=ez- --format '{{.Names}}\t{{.Status}}' | grep -E 'openobserve|otel|ez-api|ez-web|ez-worker' || true"
    remoto bash -s -- "$DESTINO" "$PORTA" <<'REMOTO'
set -euo pipefail
. "$1/.env"
agora=$(( $(date +%s) * 1000000 )); ini=$(( agora - 3600000000 ))
for tipo in logs traces; do
  n=$(curl -s -m 10 -u "$ZO_ROOT_USER_EMAIL:$ZO_ROOT_USER_PASSWORD" -H 'Content-Type: application/json' \
    "http://127.0.0.1:$2/api/default/_search?type=$tipo" \
    -d "{\"query\":{\"sql\":\"SELECT count(*) AS n FROM \\\"easystok\\\"\",\"start_time\":$ini,\"end_time\":$agora}}" \
    | sed -n 's/.*"n":\([0-9]*\).*/\1/p')
  echo "    $tipo na última hora: ${n:-0}"
done
REMOTO
    ;;

  acesso)
    echo "1. Túnel:  ssh -L $PORTA:127.0.0.1:$PORTA $VPS_HOST"
    echo "2. Abra:   http://localhost:$PORTA"
    echo "3. Login:  ssh $VPS_HOST \"grep ZO_ROOT_USER $DESTINO/.env\"   (rode você; a senha não passa por aqui)"
    ;;

  *) uso "ação desconhecida: ${ACAO:-(vazia)}" ;;
esac
