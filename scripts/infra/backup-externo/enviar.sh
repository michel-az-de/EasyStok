#!/usr/bin/env bash
# =============================================================================
# enviar.sh - backup externo do EasyStok no Google Drive, cifrado no cliente
# (remote rclone `gdrive-crypt`, tipo crypt sobre `gdrive`). Issue #1253 (#1180).
#
# Roda NA VPS (cron do usuario as 03:45, ou enviar-agora.ps1). Passos, sob trava:
#   1. escolhe o dump mais recente de $DUMP_DIRS (recusa se tiver mais de
#      $DUMP_MAX_IDADE_H horas: backup velho nao pode passar por backup do dia)
#   2. empacota o volume $UPLOADS_VOLUME num tar.gz (container alpine, :ro)
#   3. envia os dois para $DESTINO/diario/AAAA-MM-DD/ e confere com `rclone cryptcheck`
#   4. no domingo copia o dia para $DESTINO/semanal/AAAA-MM-DD/
#   5. retencao: mantem os $MANTER_DIARIOS diarios e $MANTER_SEMANAIS semanais
#      mais novos e apaga o resto (so pastas com nome AAAA-MM-DD)
# Log em $LOG. Qualquer falha termina com exit != 0.
#
# Segredos: so o arquivo $RCLONE_CONFIG (criado pelo Felipe, chmod 600). Este
# script nunca imprime o conteudo dele.
#
# Variaveis: RCLONE_CONFIG (~/.config/easystok-backup/rclone.conf), DESTINO
# (gdrive-crypt:easystok), DUMP_DIRS ("/opt/backups $HOME/backups"), DUMP_PADRAO
# (*easystock*), DUMP_MAX_IDADE_H (26), UPLOADS_VOLUME (easystok_uploads-data),
# MANTER_DIARIOS (7), MANTER_SEMANAIS (4), LOG (~/logs/backup-externo.log),
# HOJE (AAAA-MM-DD, so para teste).
# =============================================================================
set -euo pipefail

export PATH="$HOME/bin:$PATH"
export RCLONE_CONFIG="${RCLONE_CONFIG:-$HOME/.config/easystok-backup/rclone.conf}"
DESTINO="${DESTINO:-gdrive-crypt:easystok}"
DUMP_DIRS="${DUMP_DIRS:-/opt/backups $HOME/backups}"
DUMP_PADRAO="${DUMP_PADRAO:-*easystock*}"
DUMP_MAX_IDADE_H="${DUMP_MAX_IDADE_H:-26}"
UPLOADS_VOLUME="${UPLOADS_VOLUME:-easystok_uploads-data}"
MANTER_DIARIOS="${MANTER_DIARIOS:-7}"
MANTER_SEMANAIS="${MANTER_SEMANAIS:-4}"
LOG="${LOG:-$HOME/logs/backup-externo.log}"

# --- funcoes puras (testadas em retencao.test.sh) ----------------------------

# Le nomes de pasta no stdin e imprime os que passam dos $1 mais novos.
# Ignora o que nao for AAAA-MM-DD: nunca apaga pasta que nao criou.
excedentes() {
  local manter="$1"
  grep -E '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' | sort -r | tail -n +"$((manter + 1))" || true
}

# 0 se a data AAAA-MM-DD cai num domingo.
e_domingo() {
  [ "$(date -u -d "$1" +%u)" = 7 ]
}

# --- execucao ----------------------------------------------------------------

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }

dump_mais_recente() {
  local d
  for d in $DUMP_DIRS; do
    [ -d "$d" ] && find "$d" -maxdepth 1 -type f -name "$DUMP_PADRAO" \
      \( -name '*.dump' -o -name '*.sql.gz' \) -printf '%T@ %p\n' 2>/dev/null
  done | sort -rn | head -1 | cut -d' ' -f2-
}

pastas() {
  rclone lsf --dirs-only "$DESTINO/$1/" 2>/dev/null | sed 's#/$##' || true
}

expurgar() {
  local tipo="$1" manter="$2" p
  for p in $(pastas "$tipo" | excedentes "$manter"); do
    log "retencao: apagando $tipo/$p"
    rclone purge "$DESTINO/$tipo/$p"
  done
}

main() {
  mkdir -p "$(dirname "$LOG")"
  exec > >(tee -a "$LOG") 2>&1
  exec 8>/tmp/easystok-backup-externo.lock
  flock -n 8 || { log "ERRO: outro envio em andamento"; exit 3; }

  local hoje tmp dump idade_h
  hoje="${HOJE:-$(date +%F)}"
  log "==> backup externo $hoje para $DESTINO"

  command -v rclone >/dev/null || { log "ERRO: rclone ausente (rode instalar)"; exit 2; }
  [ -r "$RCLONE_CONFIG" ] || { log "ERRO: $RCLONE_CONFIG ausente"; exit 2; }
  rclone listremotes | grep -qx 'gdrive-crypt:' || { log "ERRO: remote gdrive-crypt ausente"; exit 2; }

  dump="$(dump_mais_recente)"
  [ -n "$dump" ] && [ -s "$dump" ] || { log "ERRO: nenhum dump '$DUMP_PADRAO' em $DUMP_DIRS"; exit 4; }
  idade_h=$(( ($(date +%s) - $(stat -c %Y "$dump")) / 3600 ))
  [ "$idade_h" -le "$DUMP_MAX_IDADE_H" ] || { log "ERRO: dump mais recente tem ${idade_h}h: $dump"; exit 4; }
  log "dump: $dump (${idade_h}h, $(du -h "$dump" | cut -f1))"

  tmp="$(mktemp -d)"
  # Expande agora: no EXIT a variavel local ja saiu de escopo (set -u).
  trap "rm -rf '$tmp'" EXIT
  cp "$dump" "$tmp/"
  docker run --rm -v "$UPLOADS_VOLUME":/d:ro -v "$tmp":/out alpine \
    tar czf /out/uploads-"$hoje".tar.gz -C /d . \
    || { log "ERRO: tar do volume $UPLOADS_VOLUME falhou"; exit 5; }
  log "uploads: $(du -h "$tmp/uploads-$hoje.tar.gz" | cut -f1)"

  rclone copy "$tmp" "$DESTINO/diario/$hoje" --stats-one-line --stats 0
  rclone cryptcheck "$tmp" "$DESTINO/diario/$hoje" --one-way \
    || { log "ERRO: conferencia do envio falhou"; exit 6; }
  log "enviado e conferido: diario/$hoje"

  if e_domingo "$hoje"; then
    rclone copy "$DESTINO/diario/$hoje" "$DESTINO/semanal/$hoje"
    log "domingo: copiado para semanal/$hoje"
  fi

  expurgar diario "$MANTER_DIARIOS"
  expurgar semanal "$MANTER_SEMANAIS"
  log "==> OK"
}

[ "${BACKUP_EXTERNO_SO_FUNCOES:-0}" = 1 ] || main "$@"
