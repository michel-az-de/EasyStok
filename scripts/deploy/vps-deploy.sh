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
# Guarda (issue #1336, incidente 2026-10-01): antes do git archive faz git fetch
# e RECUSA o SHA que nao for o origin/master atual nem descendente dele, porque
# publicar um commit antigo desfaz em producao tudo o que entrou depois. Voltar
# de proposito a uma versao anterior so com --rollback, que exige motivo e
# confirmacao. Tambem recusa rodar a partir de uma copia deste script diferente
# da do origin/master (worktree antigo). Cada execucao grava uma linha em
# $BUILD_ROOT/deploy-history.log na VPS: data, modo, sha, rc, quem, motivo.
#
# Uso:
#   scripts/deploy/vps-deploy.sh                 # origin/master
#   scripts/deploy/vps-deploy.sh <sha>           # origin/master ou descendente dele
#   scripts/deploy/vps-deploy.sh --rollback <sha> --motivo "<por que>"
#                                                # commit anterior do master; pede
#                                                # para digitar o sha (ou ROLLBACK_CONFIRMA=<sha8>)
#   scripts/deploy/vps-deploy.sh --dry-run [...] # guarda + plano + sintaxe, sem SSH
#
# Saidas: 0 ok | 2 uso invalido | 3 outro deploy em andamento | 4 build falhou
# | 5 dump vazio | 6 nao ficou healthy (imagem revertida) | 7 SHA recusado pela
# guarda | 8 rollback sem motivo ou sem confirmacao.
#
# Variaveis: VPS_HOST (hostinger), STACK_DIR (/opt/stacks/easystok),
# BUILD_ROOT (/home/felipe/build), BACKUP_DIR (/home/felipe/backups; /opt/backups e do root), DB_CONTAINER
# (shared-postgres), DB_NAME (easystock), HEALTH_TIMEOUT (180 s).
# =============================================================================
set -euo pipefail

VPS_HOST="${VPS_HOST:-hostinger}"
STACK_DIR="${STACK_DIR:-/opt/stacks/easystok}"
BUILD_ROOT="${BUILD_ROOT:-/home/felipe/build}"
BACKUP_DIR="${BACKUP_DIR:-/home/felipe/backups}"
DB_CONTAINER="${DB_CONTAINER:-shared-postgres}"
DB_NAME="${DB_NAME:-easystock}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-180}"
SERVICES="api worker web"

uso() { echo "ERRO: $*" >&2; sed -n '/^# Uso:/,/^# Saidas:/p' "$0" | sed '$d; s/^# \{0,1\}//' >&2; exit 2; }

DRY_RUN=0 ROLLBACK=0 MOTIVO="" REF=""
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=1 ;;
    --rollback) [ -n "${2:-}" ] || uso "--rollback exige um sha"; ROLLBACK=1; REF="$2"; shift ;;
    --motivo) [ -n "${2:-}" ] || uso "--motivo exige um texto"; MOTIVO="$2"; shift ;;
    -*) uso "opcao desconhecida: $1" ;;
    *) [ -z "$REF" ] || uso "mais de um sha informado"; REF="$1" ;;
  esac
  shift
done
REF="${REF:-origin/master}"
[ "$ROLLBACK" = 1 ] || [ -z "$MOTIVO" ] || uso "--motivo so vale com --rollback"

# Script executado na VPS. Heredoc literal: nada aqui e expandido localmente.
read -r -d '' REMOTE <<'REMOTE_EOF' || true
set -euo pipefail
registra() {
  local rc=$?
  printf '%s\t%s\t%s\trc=%s\t%s\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$MODO" "$SHA" "$rc" "$QUEM" "$MOTIVO" \
    >>"$BUILD_ROOT/deploy-history.log" 2>/dev/null || true
}
trap registra EXIT
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
mkdir -p "$BACKUP_DIR"
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

recusa() { echo "RECUSADO: $1" >&2; shift; [ $# = 0 ] || printf '%s\n' "$@" >&2; exit 7; }

# --- guarda (#1336): o SHA tem de ser o origin/master atual ou descendente dele
git fetch -q origin || recusa "git fetch origin falhou; sem o origin/master atual nao da para conferir o SHA."
MASTER="$(git rev-parse --verify 'origin/master^{commit}')"
SHA_FULL="$(git rev-parse --verify -q "$REF^{commit}")" || recusa "'$REF' nao e um commit conhecido (rode git fetch?)."
SHA="${SHA_FULL:0:8}"
ATRAS="$(git rev-list --count "$SHA_FULL..$MASTER")"

if [ "$ROLLBACK" = 0 ]; then
  if ! git merge-base --is-ancestor "$MASTER" "$SHA_FULL"; then
    if git merge-base --is-ancestor "$SHA_FULL" "$MASTER"; then
      onde="esta $ATRAS commit(s) atras do origin/master (${MASTER:0:8}); publicar desfaz em producao:"
    else
      onde="nao descende do origin/master (${MASTER:0:8}); ficariam de fora $ATRAS commit(s) do master, entre eles:"
    fi
    recusa "$SHA $onde" \
      "$(git log --format='  %h %s' -10 "$SHA_FULL..$MASTER")" \
      "Para publicar o master atual: scripts/deploy/vps-deploy.sh" \
      "Para voltar de proposito a uma versao anterior: --rollback <sha> --motivo \"<por que>\""
  fi
  MODO=deploy
  FRENTE="$(git rev-list --count "$MASTER..$SHA_FULL")"
  guarda="$SHA e o origin/master atual"
  [ "$FRENTE" = 0 ] || guarda="$SHA descende do origin/master (${MASTER:0:8}), $FRENTE commit(s) a frente"
else
  [ -n "$MOTIVO" ] || { echo "ERRO: --rollback exige --motivo \"<por que>\"" >&2; exit 8; }
  git merge-base --is-ancestor "$SHA_FULL" "$MASTER" \
    || recusa "rollback so para commit que ja esteve no origin/master; $SHA nao esta na historia dele."
  MODO=rollback
  guarda="ROLLBACK para $SHA, $ATRAS commit(s) atras do origin/master (${MASTER:0:8}). Motivo: $MOTIVO"
fi

# Copia antiga deste script (worktree parado) nao tem as guardas novas: so roda a do master.
ESPERADO="$(git rev-parse -q --verify 'origin/master:scripts/deploy/vps-deploy.sh' || true)"
if [ -n "$ESPERADO" ] && [ "$(git hash-object --path=scripts/deploy/vps-deploy.sh "$0")" != "$ESPERADO" ]; then
  msg="esta copia do vps-deploy.sh ($0) difere da do origin/master: script desatualizado ou alterado. Rode o do master atualizado."
  if [ "$DRY_RUN" = 1 ]; then echo "AVISO: $msg" >&2; else recusa "$msg"; fi
fi

TS="$(date -u +%Y%m%dT%H%M%SZ)"
QUEM="$(git config user.name || true) <$(git config user.email || true)> em ${USER:-${USERNAME:-?}}@$(hostname 2>/dev/null || echo '?')"
QUEM="${QUEM//[$'\t\n\r']/ }"
MOTIVO="${MOTIVO//[$'\t\n\r']/ }"

echo "==> Deploy de $SHA ($(git log -1 --format=%s "$SHA_FULL" | cut -c1-70)) em $VPS_HOST"
echo "    guarda: OK, $guarda"
echo "    servicos: $SERVICES | dump: $BACKUP_DIR/$DB_NAME-predeploy-$TS.dump | rollback: vps-prev-$TS"
echo "    historico: $BUILD_ROOT/deploy-history.log ($MODO, $QUEM)"

if [ "$ROLLBACK" = 1 ] && [ "$DRY_RUN" = 0 ]; then
  git log --format='    desfaz %h %s' -10 "$SHA_FULL..$MASTER"
  if [ -n "${ROLLBACK_CONFIRMA:-}" ]; then resp="$ROLLBACK_CONFIRMA"
  elif [ -t 0 ]; then read -r -p "Digite $SHA para confirmar o rollback: " resp
  else resp=""; fi
  [ "$resp" = "$SHA" ] || { echo "ERRO: rollback nao confirmado; digite exatamente $SHA (ou ROLLBACK_CONFIRMA=$SHA)." >&2; exit 8; }
fi

if [ "$DRY_RUN" = 1 ]; then
  bash -n <<<"$REMOTE" && echo "==> script remoto: sintaxe OK"
  echo "==> --dry-run: nada foi executado."
  exit 0
fi

ENVS="SHA=$SHA SHA_FULL=$SHA_FULL TS=$TS STACK_DIR=$STACK_DIR BUILD_ROOT=$BUILD_ROOT"
ENVS="$ENVS BACKUP_DIR=$BACKUP_DIR DB_CONTAINER=$DB_CONTAINER DB_NAME=$DB_NAME"
ENVS="$ENVS HEALTH_TIMEOUT=$HEALTH_TIMEOUT SERVICES='$SERVICES'"
ENVS="$ENVS MODO=$MODO QUEM=$(printf '%q' "$QUEM") MOTIVO=$(printf '%q' "$MOTIVO")"

# stdin do ssh = fonte (tar); o script remoto vai como argumento de bash -c.
git archive --format=tar "$SHA_FULL" \
  | ssh -o BatchMode=yes "$VPS_HOST" "$ENVS bash -c $(printf '%q' "$REMOTE")"
