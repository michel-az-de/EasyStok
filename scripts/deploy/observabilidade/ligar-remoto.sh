#!/usr/bin/env bash
# Roda NA VPS (copiado por scripts/deploy/observabilidade-deploy.sh). Liga ou desliga o override de observabilidade
# no stack do EasyStok (#2107, F2): mexe só na linha COMPOSE_FILE do .env e recria api, web e worker. Se não ficarem
# saudáveis no prazo, devolve o COMPOSE_FILE anterior e sobe de novo.
#
# Uso: ligar-remoto.sh <stack_dir> ligar|desligar
# Env: HEALTH_TIMEOUT (180 s), ESPERA entre checagens (5 s), LOCK (/tmp/easystok-deploy.lock, o mesmo do vps-deploy.sh).
# Saídas: 0 ok | 2 uso inválido | 3 outro deploy em andamento | 4 compose inválido (nada recriado)
#         | 6 não ficou saudável (COMPOSE_FILE anterior restaurado)
set -euo pipefail

stack="${1:-}"; acao="${2:-}"
[ -d "$stack" ] && { [ "$acao" = ligar ] || [ "$acao" = desligar ]; } || { echo "uso: $0 <stack_dir> ligar|desligar" >&2; exit 2; }
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-180}"
ESPERA="${ESPERA:-5}"
LOCK="${LOCK:-/tmp/easystok-deploy.lock}"
ARQ=compose.observabilidade.yaml
SERVICOS="api web worker"
env="$stack/.env"

exec 9>"$LOCK"
flock -n 9 || { echo "ERRO: outro deploy do EasyStok em andamento ($LOCK)" >&2; exit 3; }

anterior="$(sed -n 's/^COMPOSE_FILE=//p' "$env" | tail -n1)"
if [ -z "$anterior" ]; then
  # Sem COMPOSE_FILE o docker compose junta compose.yaml e compose.override.yaml sozinho; mantém o mesmo conjunto.
  base=compose.yaml
  [ -f "$stack/compose.override.yaml" ] && base="$base:compose.override.yaml"
else
  base="$anterior"
fi

sem_obs="$(printf '%s' "$base" | tr ':' '\n' | grep -v -x "$ARQ" | paste -sd: -)"
if [ "$acao" = ligar ]; then novo="$sem_obs:$ARQ"; else novo="$sem_obs"; fi

grava() {
  local valor="$1" tmp
  tmp="$(mktemp "$stack/.env.XXXXXX")"
  if [ -z "$valor" ]; then
    grep -v '^COMPOSE_FILE=' "$env" >"$tmp" || true
  elif grep -q '^COMPOSE_FILE=' "$env"; then
    sed "s|^COMPOSE_FILE=.*|COMPOSE_FILE=$valor|" "$env" >"$tmp"
  else
    cat "$env" >"$tmp"; echo "COMPOSE_FILE=$valor" >>"$tmp"
  fi
  chmod 600 "$tmp"; mv "$tmp" "$env"
}

saudavel() {
  local fim=$(( $(date +%s) + HEALTH_TIMEOUT )) ok c
  while :; do
    ok=1
    for c in ez-api ez-web; do
      [ "$(docker inspect -f '{{.State.Health.Status}}' "$c" 2>/dev/null)" = healthy ] || ok=0
    done
    if [ "$acao" = ligar ]; then
      [ "$(docker inspect -f '{{.State.Health.Status}}' ez-worker 2>/dev/null)" = healthy ] || ok=0
    else
      [ "$(docker inspect -f '{{.State.Running}}' ez-worker 2>/dev/null)" = true ] || ok=0
    fi
    [ "$ok" = 1 ] && return 0
    [ "$(date +%s)" -ge "$fim" ] && return 1
    sleep "$ESPERA"
  done
}

sobe() { (cd "$stack" && docker compose up -d $SERVICOS); }

echo "==> COMPOSE_FILE: ${anterior:-(padrão)} -> $novo"
grava "$novo"
if ! (cd "$stack" && docker compose config -q); then
  grava "$anterior"
  echo "ERRO: compose inválido com $ARQ; COMPOSE_FILE restaurado e nada foi recriado" >&2
  exit 4
fi

echo "==> Recriando $SERVICOS"
sobe
if saudavel; then
  echo "==> OK: api, web e worker saudáveis ($acao)"
  exit 0
fi

echo "ERRO: não ficou saudável em ${HEALTH_TIMEOUT}s; restaurando COMPOSE_FILE anterior" >&2
docker logs --tail 30 ez-worker >&2 2>&1 || true
grava "$anterior"
sobe
exit 6
