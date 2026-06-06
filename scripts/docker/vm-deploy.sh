#!/usr/bin/env bash
# vm-deploy.sh — deploy idempotente do EasyStok na VM Azure (easystok-vm).
#
# Resolve a dor "correcoes nao refletem": o deploy da VM era manual
# (git pull + docker compose up --build, em /home/azureuser/easystok) e
# esquecido, deixando a VM servindo um commit antigo enquanto o master avancava.
# Medido em 2026-06-06: VM em 17a62f82 vs master a frente.
#
# Este script torna o deploy 1 comando idempotente e carimba o GIT_SHA real no
# build, para que /health prove a versao no ar (GIT_SHA == origin/master).
#
#   Manual:     bash scripts/docker/vm-deploy.sh [--force]
#   Auto (cron): */5 * * * * bash /home/azureuser/easystok/scripts/docker/vm-deploy.sh
#                so rebuilda quando origin/master avanca; --force rebuilda sempre.
set -euo pipefail

REPO="${EASYSTOK_REPO:-/home/azureuser/easystok}"
COMPOSE="$REPO/docker-compose.azure.yml"
BRANCH="${EASYSTOK_BRANCH:-master}"
FORCE="${1:-}"

cd "$REPO"
git config --global --add safe.directory "$REPO" 2>/dev/null || true
git fetch origin "$BRANCH" --quiet

LOCAL="$(git rev-parse HEAD)"
REMOTE="$(git rev-parse "origin/$BRANCH")"

if [ "$LOCAL" = "$REMOTE" ] && [ "$FORCE" != "--force" ]; then
  echo "[vm-deploy] ja em ${LOCAL:0:8} (origin/$BRANCH). Nada a fazer."
  exit 0
fi

echo "[vm-deploy] atualizando ${LOCAL:0:8} -> ${REMOTE:0:8} ..."
git pull --ff-only origin "$BRANCH"
SHA="$(git rev-parse HEAD)"

echo "[vm-deploy] rebuildando stack (GIT_SHA=${SHA:0:8}) ..."
GIT_SHA="$SHA" docker compose -f "$COMPOSE" up -d --build

sleep 8
WEB_SHA="$(docker exec easystok-web printenv GIT_SHA 2>/dev/null || echo '?')"
echo "[vm-deploy] OK. HEAD=${SHA:0:8}  container_web GIT_SHA=${WEB_SHA:0:8}"
[ "$WEB_SHA" = "$SHA" ] || echo "[vm-deploy] AVISO: GIT_SHA do container difere do HEAD (rebuild pode ter usado cache)."
