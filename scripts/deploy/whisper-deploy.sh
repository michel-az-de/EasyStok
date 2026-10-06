#!/usr/bin/env bash
# =============================================================================
# whisper-deploy.sh - sobe a stack de transcrição (faster-whisper) na VPS. ADR-0058.
# Roda da máquina de dev (Git Bash): copia scripts/deploy/whisper/compose.yaml para
# /opt/stacks/whisper, sobe o ez-whisper na rede "edge" (a mesma do ez-api) e espera
# o servidor responder. O primeiro pedido de transcrição baixa o modelo (~500 MB).
# Uso: scripts/deploy/whisper-deploy.sh
# =============================================================================
set -euo pipefail
VPS_HOST="${VPS_HOST:-hostinger}"
DESTINO="${DESTINO:-/opt/stacks/whisper}"
RAIZ="$(cd "$(dirname "$0")/../.." && pwd)"

echo "==> Copiando compose para $VPS_HOST:$DESTINO"
ssh "$VPS_HOST" "mkdir -p '$DESTINO'"
scp -q "$RAIZ/scripts/deploy/whisper/compose.yaml" "$VPS_HOST:$DESTINO/compose.yaml"

echo "==> Subindo ez-whisper"
ssh "$VPS_HOST" "cd '$DESTINO' && docker compose pull -q && docker compose up -d"

echo "==> Esperando o servidor responder"
for _ in $(seq 1 60); do
  if ssh "$VPS_HOST" "docker run --rm --network edge curlimages/curl:8.10.1 -sf -m 5 http://ez-whisper:8000/health" >/dev/null 2>&1; then
    echo "==> OK: ez-whisper responde na rede edge"
    exit 0
  fi
  sleep 5
done
echo "ERRO: ez-whisper não respondeu em 5 min" >&2
ssh "$VPS_HOST" "cd '$DESTINO' && docker compose logs --tail 30" >&2
exit 1
