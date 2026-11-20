#!/usr/bin/env bash
# =============================================================================
# instalar.sh - instala o backup restic na VPS (E3, #2977, ADR-0068). Idempotente.
# Roda NA VPS como felipe, a partir da pasta com backup.sh e systemd/ (o instalar.ps1
# copia para ~/backup-restic/src e chama este script).
#
# Nunca le nem imprime segredo: so confere que a senha e o rclone.conf existem, com
# permissao 600 e as chaves esperadas. Sem eles, para antes de ligar o timer.
# Uso: bash instalar.sh [--sem-timer]
# =============================================================================
set -euo pipefail
SRC="$(cd "$(dirname "$0")" && pwd)"
BASE="$HOME/backup-restic"; BIN="$HOME/bin"; CONF_DIR="$HOME/.config/easystok-backup"
UNIT_DIR="$HOME/.config/systemd/user"
RESTIC_VERSAO=0.19.1
RESTIC_SHA256=f415415624dcc452f2a02b8c33641791a8c6d6d3b65bbb3543fcf9a25151585c  # restic_0.19.1_linux_amd64.bz2
SEM_TIMER=0; [ "${1:-}" = "--sem-timer" ] && SEM_TIMER=1

passo() { echo "==> $*"; }
erro() { echo "ERRO: $*" >&2; exit 2; }

passo "backup.sh e units"
mkdir -p "$BASE" "$BIN" "$UNIT_DIR"; chmod 700 "$BASE"
install -m 700 "$SRC/backup.sh" "$BASE/backup.sh"
install -m 644 "$SRC/systemd/easystok-backup.service" "$SRC/systemd/easystok-backup.timer" "$UNIT_DIR/"

passo "restic $RESTIC_VERSAO"
if "$BIN/restic" version 2>/dev/null | grep -q "restic $RESTIC_VERSAO "; then
  echo "    ja instalado"
else
  tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
  curl -fsSL -o "$tmp/restic.bz2" \
    "https://github.com/restic/restic/releases/download/v$RESTIC_VERSAO/restic_${RESTIC_VERSAO}_linux_amd64.bz2"
  echo "$RESTIC_SHA256  $tmp/restic.bz2" | sha256sum -c --quiet - || erro "SHA256 do restic nao confere"
  bunzip2 "$tmp/restic.bz2"; install -m 755 "$tmp/restic" "$BIN/restic"
  "$BIN/restic" version
fi
[ -x "$BIN/rclone" ] || erro "rclone ausente em $BIN (pre-requisito da #1253)"

passo "configuracao em $CONF_DIR (sem ler segredo)"
falta=0
modo() { stat -c %a "$1" 2>/dev/null; }
[ -d "$CONF_DIR" ] && [ "$(modo "$CONF_DIR")" = 700 ] || { echo "    falta $CONF_DIR com modo 700"; falta=1; }
[ -s "$CONF_DIR/restic.senha" ] && [ "$(modo "$CONF_DIR/restic.senha")" = 600 ] \
  || { echo "    falta restic.senha (modo 600): docs/dev/backup-drive.md, passo da senha"; falta=1; }
if [ -s "$CONF_DIR/rclone.conf" ] && [ "$(modo "$CONF_DIR/rclone.conf")" = 600 ]; then
  for chave in '^\[ezdrive\]' '^type = drive' '^scope = drive$' '^service_account_file = ' '^team_drive = '; do
    grep -q "$chave" "$CONF_DIR/rclone.conf" || { echo "    rclone.conf sem '$chave'"; falta=1; }
  done
  sa="$(sed -n 's/^service_account_file = //p' "$CONF_DIR/rclone.conf")"
  [ -s "$sa" ] && [ "$(modo "$sa")" = 600 ] || { echo "    chave da conta de servico ausente ou sem modo 600"; falta=1; }
else
  echo "    falta rclone.conf (modo 600): docs/dev/backup-drive.md, passo do rclone"; falta=1
fi
[ "$falta" = 0 ] || erro "configuracao incompleta; nada foi ligado"

passo "repositorio"
export RESTIC_REPOSITORY="rclone:ezdrive:easystok-restic" RESTIC_PASSWORD_FILE="$CONF_DIR/restic.senha" RCLONE_CONFIG="$CONF_DIR/rclone.conf"
set +e; "$BIN/restic" -o rclone.program="$BIN/rclone" cat config >/dev/null 2>&1; rc=$?; set -e
case "$rc" in
  0)  echo "    ja existe" ;;
  10) "$BIN/restic" -o rclone.program="$BIN/rclone" init ;;
  12) erro "senha nao abre o repositorio existente" ;;
  *)  erro "restic cat config saiu com $rc" ;;
esac

if [ "$SEM_TIMER" = 1 ]; then passo "timer nao ligado (--sem-timer)"; exit 0; fi
passo "timer"
systemctl --user daemon-reload
systemctl --user enable --now easystok-backup.timer
systemctl --user list-timers easystok-backup.timer --no-pager
if [ "$(loginctl show-user "$USER" -p Linger --value 2>/dev/null)" != yes ]; then
  echo "AVISO: linger desligado; o timer so roda com sessao aberta. Rode: sudo loginctl enable-linger $USER"
fi
if crontab -l 2>/dev/null | grep -q 'backup-externo/enviar.sh'; then
  echo "AVISO: o cron antigo da #1253 (03:45) segue ativo; o corte e o passo 'Corte' do runbook."
fi
