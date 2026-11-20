#!/usr/bin/env bash
# =============================================================================
# instalar.sh - instala o espelho do repositorio na VPS (#2958). Idempotente.
# Roda NA VPS como felipe, a partir da pasta com espelho.sh e systemd/.
# Nao precisa de segredo: o repositorio e publico. Independe do Drive (E3).
# Uso: bash instalar.sh [--sem-timer]
# =============================================================================
set -euo pipefail
SRC="$(cd "$(dirname "$0")" && pwd)"
BASE="$HOME/git"; UNIT_DIR="$HOME/.config/systemd/user"
SEM_TIMER=0; [ "${1:-}" = "--sem-timer" ] && SEM_TIMER=1

echo "==> espelho.sh e units"
mkdir -p "$BASE" "$UNIT_DIR"
install -m 755 "$SRC/espelho.sh" "$BASE/espelho.sh"
install -m 644 "$SRC/systemd/easystok-espelho.service" "$SRC/systemd/easystok-espelho.timer" "$UNIT_DIR/"

echo "==> primeira sincronizacao"
bash "$BASE/espelho.sh"
cat "$BASE/estado-espelho"

if [ "$SEM_TIMER" = 1 ]; then echo "==> timer nao ligado (--sem-timer)"; exit 0; fi
echo "==> timer"
systemctl --user daemon-reload
systemctl --user enable --now easystok-espelho.timer
systemctl --user list-timers easystok-espelho.timer --no-pager
if [ "$(loginctl show-user "$USER" -p Linger --value 2>/dev/null)" != yes ]; then
  echo "AVISO: linger desligado; o timer so roda com sessao aberta. Rode: sudo loginctl enable-linger $USER"
fi
