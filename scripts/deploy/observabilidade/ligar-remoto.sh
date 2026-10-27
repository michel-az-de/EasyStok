#!/usr/bin/env bash
# Roda NA VPS (copiado por scripts/deploy/observabilidade-deploy.sh). Liga ou desliga o override de observabilidade
# no stack do EasyStok (#2107, F2): mexe só na linha COMPOSE_FILE do .env e no compose.observabilidade.yaml, e
# recria api, web e worker. Se não ficarem saudáveis no prazo, devolve os dois ao estado anterior e sobe de novo.
#
# O override novo chega como compose.observabilidade.yaml.novo e só é promovido aqui, já com a trava, para nunca
# trocar o arquivo debaixo de um vps-deploy.sh em andamento e para o rollback ter a versão anterior.
#
# Saudável = ez-api e ez-web healthy, ez-worker rodando e sem reinício (o mesmo critério do vps-deploy.sh). O
# healthcheck do worker não entra: a imagem em produção pode ser anterior ao ArquivoDeVidaService.
#
# Uso: ligar-remoto.sh <stack_dir> ligar|desligar
# Env: HEALTH_TIMEOUT (180 s), ESPERA entre checagens (5 s), LOCK (/tmp/easystok-deploy.lock, o mesmo do vps-deploy.sh).
# Saídas: 0 ok | 2 uso inválido | 3 outro deploy em andamento | 4 compose inválido (nada recriado)
#         | 6 não ficou saudável (estado anterior restaurado e saudável) | 7 nem o estado anterior ficou saudável
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

# Override: promove o .novo guardando o atual em .prev; volta_override desfaz.
tinha_prev=0
if [ -f "$stack/$ARQ.novo" ]; then
  if [ -f "$stack/$ARQ" ]; then cp -p "$stack/$ARQ" "$stack/$ARQ.prev"; tinha_prev=1; fi
  mv "$stack/$ARQ.novo" "$stack/$ARQ"
fi
volta_override() { if [ "$tinha_prev" = 1 ]; then mv "$stack/$ARQ.prev" "$stack/$ARQ"; fi; }
restaura() { grava "$anterior"; volta_override; }

saudavel() {
  local fim=$(( $(date +%s) + HEALTH_TIMEOUT )) ok c reinicios
  while :; do
    ok=1
    for c in ez-api ez-web; do
      [ "$(docker inspect -f '{{.State.Health.Status}}' "$c" 2>/dev/null)" = healthy ] || ok=0
    done
    [ "$(docker inspect -f '{{.State.Running}}' ez-worker 2>/dev/null)" = true ] || ok=0
    reinicios="$(docker inspect -f '{{.RestartCount}}' ez-worker 2>/dev/null || echo 0)"
    if [ "${reinicios:-0}" -gt 0 ] 2>/dev/null; then
      echo "ERRO: ez-worker reiniciou $reinicios vez(es) (laço de restart)" >&2
      return 1
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
  restaura
  echo "ERRO: compose inválido com $ARQ; estado anterior restaurado e nada foi recriado" >&2
  exit 4
fi

echo "==> Recriando $SERVICOS"
if sobe && saudavel; then
  [ "$tinha_prev" = 1 ] && rm -f "$stack/$ARQ.prev"
  echo "==> OK: api e web healthy, worker rodando ($acao). Worker: $(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}sem healthcheck{{end}}' ez-worker 2>/dev/null || echo '?')"
  exit 0
fi

echo "ERRO: não ficou saudável em ${HEALTH_TIMEOUT}s; restaurando o estado anterior" >&2
docker logs --tail 30 ez-worker >&2 2>&1 || true
restaura
if sobe && saudavel; then
  echo "==> Estado anterior restaurado e saudável" >&2
  exit 6
fi
echo "ROLLBACK NAO FICOU SAUDAVEL: intervenção manual (docker ps; docker logs ez-api)" >&2
exit 7
