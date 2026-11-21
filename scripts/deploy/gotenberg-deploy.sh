#!/usr/bin/env bash
# =============================================================================
# gotenberg-deploy.sh - sobe o Gotenberg (render HTML -> PNG do impresso Pix) na VPS. #3017.
# Roda da máquina de dev (Git Bash): copia scripts/deploy/gotenberg/compose.yaml para /opt/stacks/gotenberg,
# sobe o ez-gotenberg na rede "edge" (a mesma do ez-api) e espera o healthcheck do container ficar healthy.
# Stack separada do vps-deploy.sh: se o Gotenberg falhar, a API e o rollback não caem.
# Uso: scripts/deploy/gotenberg-deploy.sh [--dry-run]
# Saídas: 0 ok | 2 uso inválido | 4 não ficou healthy
# =============================================================================
set -euo pipefail
VPS_HOST="${VPS_HOST:-hostinger}"
DESTINO="${DESTINO:-/opt/stacks/gotenberg}"
RAIZ="$(cd "$(dirname "$0")/../.." && pwd)"
COMPOSE="$RAIZ/scripts/deploy/gotenberg/compose.yaml"

DRY_RUN=0
case "${1:-}" in
  "") ;;
  --dry-run) DRY_RUN=1 ;;
  *) echo "ERRO: argumento desconhecido: $1 (uso: gotenberg-deploy.sh [--dry-run])" >&2; exit 2 ;;
esac
[ -f "$COMPOSE" ] || { echo "ERRO: $COMPOSE não existe" >&2; exit 2; }

if [ "$DRY_RUN" = 1 ]; then
  echo "==> --dry-run: nada foi executado. Plano:"
  echo "    scp $COMPOSE -> $VPS_HOST:$DESTINO/compose.yaml"
  echo "    ssh $VPS_HOST: cd $DESTINO && docker compose pull -q && docker compose up -d"
  echo "    esperar ez-gotenberg healthy (até 2 min)"
  exit 0
fi

echo "==> Copiando compose para $VPS_HOST:$DESTINO"
ssh -o BatchMode=yes "$VPS_HOST" "mkdir -p '$DESTINO'"
scp -q "$COMPOSE" "$VPS_HOST:$DESTINO/compose.yaml"

echo "==> Subindo ez-gotenberg (imagem fixada no compose)"
ssh -o BatchMode=yes "$VPS_HOST" "cd '$DESTINO' && docker compose pull -q && docker compose up -d"

echo "==> Esperando ficar healthy"
for _ in $(seq 1 24); do
  estado="$(ssh -o BatchMode=yes "$VPS_HOST" "docker inspect -f '{{.State.Health.Status}}' ez-gotenberg" 2>/dev/null || true)"
  if [ "$estado" = "healthy" ]; then
    echo "==> OK: ez-gotenberg healthy em http://gotenberg:3000 (rede edge)"
    exit 0
  fi
  sleep 5
done
echo "ERRO: ez-gotenberg não ficou healthy em 2 min (estado: ${estado:-desconhecido})" >&2
ssh -o BatchMode=yes "$VPS_HOST" "cd '$DESTINO' && docker compose logs --tail 30" >&2 || true
exit 4
