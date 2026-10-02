#!/usr/bin/env bash
# =============================================================================
# console-deploy.sh - publica o console do operador em https://app.easystok.online
# (HTML estatico servido pelo Caddy, que repassa /api para o ez-api). Issue #1358.
#
# Sempre publica o origin/master: monta a partir de `git archive` (arvore limpa,
# nunca de worktree parada), no modo API (VITE_FONTE_DADOS=api, VITE_API_BASE
# vazio = mesma origem), carimba <meta name="easystok-console-sha"> com o SHA,
# guarda backup do index.html no servidor, copia e confere o SHA servido.
# Avisa quando a API em producao esta noutro SHA (12-go-live.md, passo 4).
#
# Em 2026-10-02 o console no ar era de 01/10 23:06 UTC enquanto a API estava no
# master; a copia manual escondia a discrepancia.
#
# Uso:
#   scripts/deploy/console-deploy.sh              # monta e publica o origin/master
#   scripts/deploy/console-deploy.sh --so-montar  # so monta e confere, sem SSH
#
# Variaveis: VPS_HOST (hostinger), DESTINO (/opt/stacks/shared/docs/easystok-console),
# URL_CONSOLE (https://app.easystok.online), URL_VERSAO_API (https://api.easystok.online/health/version).
# Saidas: 0 publicado e conferido | 1 falha | 9 SHA servido diferente do publicado.
# =============================================================================
set -euo pipefail

VPS_HOST="${VPS_HOST:-hostinger}"
DESTINO="${DESTINO:-/opt/stacks/shared/docs/easystok-console}"
URL_CONSOLE="${URL_CONSOLE:-https://app.easystok.online}"
URL_VERSAO_API="${URL_VERSAO_API:-https://api.easystok.online/health/version}"
SO_MONTAR=0
[ "${1:-}" = "--so-montar" ] && SO_MONTAR=1

REPO="$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
git -C "$REPO" fetch -q origin
SHA="$(git -C "$REPO" rev-parse --verify 'origin/master^{commit}')"
echo "==> Console de ${SHA:0:8} ($(git -C "$REPO" log -1 --format=%s "$SHA" | cut -c1-70))"

API_SHA="$(curl -s -m 15 "$URL_VERSAO_API" | sed -n 's/.*"buildSha":"\([0-9a-f]*\)".*/\1/p' || true)"
if [ -z "$API_SHA" ]; then
  echo "    AVISO: nao li o SHA da API em $URL_VERSAO_API" >&2
elif [ "$API_SHA" != "$SHA" ]; then
  echo "    AVISO: a API em producao esta em ${API_SHA:0:8}, nao em ${SHA:0:8}. Publique a API do mesmo SHA (vps-deploy.sh)." >&2
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
git -C "$REPO" archive "$SHA" EasyStock.Console | tar -x -C "$TMP"
(
  cd "$TMP/EasyStock.Console"
  npm ci --no-audit --no-fund --loglevel=error >/dev/null
  VITE_FONTE_DADOS=api VITE_API_BASE= npm run build >/dev/null
)
DIST="$TMP/EasyStock.Console/dist"
sed -i "0,/<head>/s##<head><meta name=\"easystok-console-sha\" content=\"$SHA\">#" "$DIST/index.html"

for marca in 'easystok-console-sha' 'google/config' 'api/atendimento/conversas'; do
  grep -q "$marca" "$DIST/index.html" || { echo "ERRO: o console montado nao tem '$marca'" >&2; exit 1; }
done
echo "==> Montado: $(ls "$DIST" | tr '\n' ' ')($(wc -c < "$DIST/index.html") bytes), modo API e login Google conferidos"

if [ "$SO_MONTAR" = 1 ]; then
  echo "==> --so-montar: nada foi publicado."
  exit 0
fi

TS="$(date -u +%Y%m%dT%H%M%SZ)"
ssh -o BatchMode=yes "$VPS_HOST" "cp $DESTINO/index.html $DESTINO/index.html.antes-$TS"
scp -q "$DIST"/* "$VPS_HOST:$DESTINO/"

NO_AR="$(curl -s -m 15 "$URL_CONSOLE/" | sed -n 's/.*easystok-console-sha" content="\([0-9a-f]*\)".*/\1/p' | head -1)"
if [ "$NO_AR" != "$SHA" ]; then
  echo "ERRO: servido '${NO_AR:0:8}', esperado '${SHA:0:8}'. Backup: $DESTINO/index.html.antes-$TS" >&2
  exit 9
fi
echo "==> OK: console ${SHA:0:8} no ar. Backup do anterior: $DESTINO/index.html.antes-$TS"
