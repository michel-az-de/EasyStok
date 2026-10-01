#!/usr/bin/env bash
# =============================================================================
# instalar.sh - instala o backup externo na VPS, sem sudo. Issue #1253 (#1180).
#
# Roda NA VPS (via instalar.ps1, que embute enviar.sh e testar-restauracao.sh
# em ENVIAR_B64/TESTAR_B64). Idempotente:
#   1. rclone em ~/bin (download oficial + SHA256SUMS), se faltar
#   2. copia os scripts para $APP_DIR, cria ~/logs
#   3. valida $RCLONE_CONFIG: existe, chmod 600, remote `gdrive` (drive) e
#      `gdrive-crypt` (crypt sobre gdrive:). Nao imprime segredo nenhum.
#   4. testa acesso: `rclone lsf gdrive-crypt:` (so lista)
#   5. cron do usuario: 45 3 * * * (depois do backup local das 03:15)
# Sem o rclone.conf, faz 1 e 2 e para com exit 2 e a instrucao.
# =============================================================================
set -euo pipefail

export PATH="$HOME/bin:$PATH"
APP_DIR="${APP_DIR:-$HOME/backup-externo}"
RCLONE_CONFIG="${RCLONE_CONFIG:-$HOME/.config/easystok-backup/rclone.conf}"
export RCLONE_CONFIG
CRON_HORA="${CRON_HORA:-45 3 * * *}"
MARCA="# easystok-backup-externo"

echo "==> [1/5] rclone"
if ! command -v rclone >/dev/null; then
  arch="$(uname -m)"; case "$arch" in x86_64) arch=amd64 ;; aarch64) arch=arm64 ;; esac
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  zip="rclone-current-linux-$arch.zip"
  curl -fsSL -o "$tmp/$zip" "https://downloads.rclone.org/$zip"
  # SHA256SUMS da versao corrente (o zip "current" nao tem soma propria).
  ver="$(curl -fsSL https://downloads.rclone.org/version.txt | sed 's/^rclone //')"
  curl -fsSL -o "$tmp/SHA256SUMS" "https://downloads.rclone.org/$ver/SHA256SUMS"
  if command -v unzip >/dev/null; then unzip -q "$tmp/$zip" -d "$tmp"; else python3 -m zipfile -e "$tmp/$zip" "$tmp"; fi
  bin="$(find "$tmp" -type f -name rclone | head -1)"
  real_zip="rclone-$ver-linux-$arch.zip"
  esperado="$(grep " $real_zip\$" "$tmp/SHA256SUMS" | cut -d' ' -f1)"
  obtido="$(sha256sum "$tmp/$zip" | cut -d' ' -f1)"
  [ -n "$esperado" ] && [ "$esperado" = "$obtido" ] || { echo "ERRO: SHA256 do rclone nao confere" >&2; exit 3; }
  mkdir -p "$HOME/bin"; install -m 755 "$bin" "$HOME/bin/rclone"
fi
rclone version | head -1

echo "==> [2/5] scripts em $APP_DIR"
mkdir -p "$APP_DIR" "$HOME/logs" "$(dirname "$RCLONE_CONFIG")"
chmod 700 "$(dirname "$RCLONE_CONFIG")"
[ -n "${ENVIAR_B64:-}" ] && echo "$ENVIAR_B64" | base64 -d >"$APP_DIR/enviar.sh"
[ -n "${TESTAR_B64:-}" ] && echo "$TESTAR_B64" | base64 -d >"$APP_DIR/testar-restauracao.sh"
[ -f "$APP_DIR/enviar.sh" ] || { echo "ERRO: $APP_DIR/enviar.sh ausente (use instalar.ps1)" >&2; exit 2; }
chmod 750 "$APP_DIR"/*.sh

echo "==> [3/5] $RCLONE_CONFIG"
if [ ! -s "$RCLONE_CONFIG" ]; then
  echo "PENDENTE: crie $RCLONE_CONFIG (docs/dev/backup-externo.md, passo A) e rode instalar de novo." >&2
  exit 2
fi
chmod 600 "$RCLONE_CONFIG"
# rclone config no Windows grava CRLF; sem isto o awk abaixo nao casa a secao.
sed -i 's/\r$//' "$RCLONE_CONFIG"
# Le so as chaves 'type' e 'remote' de cada secao; nunca token nem senha.
campo() { awk -v s="[$1]" -v k="$2" '$0==s{f=1;next} /^\[/{f=0} f && $1==k {sub(/^[^=]*=[ ]*/,""); print; exit}' "$RCLONE_CONFIG"; }
[ "$(campo gdrive type)" = drive ] || { echo "ERRO: secao [gdrive] com type = drive ausente" >&2; exit 2; }
[ "$(campo gdrive-crypt type)" = crypt ] || { echo "ERRO: secao [gdrive-crypt] com type = crypt ausente" >&2; exit 2; }
case "$(campo gdrive-crypt remote)" in gdrive:*) ;; *) echo "ERRO: gdrive-crypt deve apontar para gdrive:<pasta>" >&2; exit 2 ;; esac
echo "remotes gdrive (drive) e gdrive-crypt (crypt sobre gdrive:) OK"

echo "==> [4/5] acesso ao Drive"
rclone mkdir gdrive-crypt:easystok
rclone lsf gdrive-crypt:easystok >/dev/null && echo "gdrive-crypt:easystok acessivel"

echo "==> [5/5] cron do usuario: $CRON_HORA"
linha="$CRON_HORA $APP_DIR/enviar.sh >/dev/null 2>&1 $MARCA"
{ crontab -l 2>/dev/null | grep -vF "$MARCA" || true; echo "$linha"; } | crontab -
crontab -l | grep -F "$MARCA"
echo "==> OK. Log: ~/logs/backup-externo.log"
