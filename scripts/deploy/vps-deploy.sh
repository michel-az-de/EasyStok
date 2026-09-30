#!/usr/bin/env bash
# =============================================================================
# vps-deploy.sh - deploy canonico do EasyStok na VPS (host SSH "hostinger",
# stack /opt/stacks/easystok). Issue #1223.
#
# Roda da maquina de dev (Git Bash). Nao precisa de checkout na VPS: envia o
# `git archive` do SHA pedido e builda la. Passos (no servidor, sob trava):
#   1. flock /tmp/easystok-deploy.lock -> recusa um segundo deploy simultaneo
#   2. extrai a fonte em ~/build/easystok-<sha>
#   3. docker build api/worker/web com GIT_SHA, tag easystok-<svc>:vps-<sha>
#   4. pg_dump -Fc do banco            -> a migration roda no startup da API
#   5. guarda :vps atual como vps-prev-<ts> e promove vps-<sha> -> vps
#   6. compose up -d, espera healthy e confere GIT_SHA no ez-api
#   7. se falhar: volta as imagens para vps-prev-<ts> e sobe de novo.
#      O banco NAO e revertido: o dump do passo 4 fica para restauracao manual.
#
# Uso:
#   scripts/deploy/vps-deploy.sh                 # origin/master
#   scripts/deploy/vps-deploy.sh <sha>           # um commit especifico
#   scripts/deploy/vps-deploy.sh --dry-run [sha] # plano + checagem de sintaxe, sem SSH
#
# Variaveis: VPS_HOST (hostinger), STACK_DIR (/opt/stacks/easystok),
# BUILD_ROOT (/home/felipe/build), BACKUP_DIR (/opt/backups), DB_CONTAINER
# (shared-postgres), DB_NAME (easystock), HEALTH_TIMEOUT (180 s).
# =============================================================================
set -euo pipefail

VPS_HOST="${VPS_HOST:-hostinger}"
STACK_DIR="${STACK_DIR:-/opt/stacks/easystok}"
BUILD_ROOT="${BUILD_ROOT:-/home/felipe/build}"
BACKUP_DIR="${BACKUP_DIR:-/opt/backups}"
DB_CONTAINER="${DB_CONTAINER:-shared-postgres}"
DB_NAME="${DB_NAME:-easystock}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-180}"
SERVICES="api worker web"

DRY_RUN=0
if [ "${1:-}" = "--dry-run" ]; then DRY_RUN=1; shift; fi
REF="${1:-origin/master}"

# Script executado na VPS. Heredoc literal: nada aqui e expandido localmente.
read -r -d '' REMOTE <<'REMOTE_EOF' || true
set -euo pipefail
exec 9>/tmp/easystok-deploy.lock
flock -n 9 || { echo "ERRO: outro deploy em andamento (trava /tmp/easystok-deploy.lock)" >&2; exit 3; }

SRC="$BUILD_ROOT/easystok-$SHA"
echo "==> [1/6] fonte em $SRC"
rm -rf "$SRC"
mkdir -p "$SRC"
tar -x -C "$SRC"

dockerfile() {
  case "$1" in
    api) echo Dockerfile ;;
    worker) echo EasyStock.Worker/Dockerfile ;;
    web) echo Dockerfile.Web ;;
  esac
}

echo "==> [2/6] build: $SERVICES"
for s in $SERVICES; do
  log="$BUILD_ROOT/build-$SHA-$s.log"
  docker build --build-arg GIT_SHA="$SHA_FULL" -f "$SRC/$(dockerfile "$s")" \
    -t "easystok-$s:vps-$SHA" "$SRC" >"$log" 2>&1 \
    || { echo "ERRO: build de $s falhou, ver $log" >&2; exit 4; }
done

echo "==> [3/6] pg_dump de $DB_NAME"
DUMP="$BACKUP_DIR/$DB_NAME-predeploy-$TS.dump"
docker exec "$DB_CONTAINER" pg_dump -U postgres -Fc "$DB_NAME" >"$DUMP"
[ -s "$DUMP" ] || { echo "ERRO: dump vazio; nada foi trocado nos containers" >&2; exit 5; }

echo "==> [4/6] guarda :vps como vps-prev-$TS e promove vps-$SHA"
for s in $SERVICES; do
  if docker image inspect "easystok-$s:vps" >/dev/null 2>&1; then
    docker tag "easystok-$s:vps" "easystok-$s:vps-prev-$TS"
  fi
  docker tag "easystok-$s:vps-$SHA" "easystok-$s:vps"
done

up() { (cd "$STACK_DIR" && docker compose up -d --force-recreate $SERVICES); }

healthy() {
  local fim=$(( $(date +%s) + HEALTH_TIMEOUT )) ok c
  while [ "$(date +%s)" -lt "$fim" ]; do
    ok=1
    for c in ez-api ez-web; do
      [ "$(docker inspect -f '{{.State.Health.Status}}' "$c" 2>/dev/null)" = healthy ] || ok=0
    done
    [ "$(docker inspect -f '{{.State.Running}}' ez-worker 2>/dev/null)" = true ] || ok=0
    [ "$ok" = 1 ] && return 0
    sleep 5
  done
  return 1
}

sha_no_ar() {
  docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' ez-api | sed -n 's/^GIT_SHA=//p' | head -1
}

echo "==> [5/6] compose up"
up
if healthy && [ "$(sha_no_ar)" = "$SHA_FULL" ]; then
  echo "$SHA" >"$BUILD_ROOT/easystok-current"
  echo "==> [6/6] OK: $SHA no ar. Dump pre-deploy: $DUMP"
  exit 0
fi

echo "ERRO: $SHA nao ficou healthy em ${HEALTH_TIMEOUT}s; voltando para vps-prev-$TS" >&2
docker logs --tail 40 ez-api >&2 || true
for s in $SERVICES; do
  if docker image inspect "easystok-$s:vps-prev-$TS" >/dev/null 2>&1; then
    docker tag "easystok-$s:vps-prev-$TS" "easystok-$s:vps"
  fi
done
up
if healthy; then
  echo "ROLLBACK de imagem OK. O banco NAO foi revertido; se a migration quebrou, restaure $DUMP" >&2
else
  echo "ROLLBACK NAO FICOU HEALTHY: intervencao manual. Dump: $DUMP" >&2
fi
exit 6
REMOTE_EOF

git fetch -q origin
SHA_FULL="$(git rev-parse --verify "$REF^{commit}")"
SHA="${SHA_FULL:0:8}"
TS="$(date -u +%Y%m%dT%H%M%SZ)"

echo "==> Deploy de $SHA ($(git log -1 --format=%s "$SHA_FULL" | cut -c1-70)) em $VPS_HOST"
echo "    servicos: $SERVICES | dump: $BACKUP_DIR/$DB_NAME-predeploy-$TS.dump | rollback: vps-prev-$TS"

if [ "$DRY_RUN" = 1 ]; then
  bash -n <<<"$REMOTE" && echo "==> script remoto: sintaxe OK"
  echo "==> --dry-run: nada foi executado."
  exit 0
fi

ENVS="SHA=$SHA SHA_FULL=$SHA_FULL TS=$TS STACK_DIR=$STACK_DIR BUILD_ROOT=$BUILD_ROOT"
ENVS="$ENVS BACKUP_DIR=$BACKUP_DIR DB_CONTAINER=$DB_CONTAINER DB_NAME=$DB_NAME"
ENVS="$ENVS HEALTH_TIMEOUT=$HEALTH_TIMEOUT SERVICES='$SERVICES'"

# stdin do ssh = fonte (tar); o script remoto vai como argumento de bash -c.
git archive --format=tar "$SHA_FULL" \
  | ssh -o BatchMode=yes "$VPS_HOST" "$ENVS bash -c $(printf '%q' "$REMOTE")"
