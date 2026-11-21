#!/usr/bin/env bash
# =============================================================================
# backup.sh - backup do EasyStok no Google Drive com restic (E3, #2977, ADR-0068).
#
# Roda NA VPS como felipe. O timer systemd de usuario easystok-backup.timer chama
# `backup.sh` as 03:00 de Brasilia; o vps-deploy.sh chama `backup.sh pre-deploy <dump>`
# em segundo plano depois do dump de cada deploy.
#
# Uso: backup.sh                    snapshot diario (tag diario)
#      backup.sh pre-deploy <dump>  snapshot do dump do deploy (tag pre-deploy, 10 ultimos)
#
# O snapshot diario leva, em $BASE/stage:
#   db/       pg_dump -Fc do banco e manifesto de contagens tirados do MESMO snapshot MVCC,
#             globals sem senha (role easystock NOBYPASSRLS e grants) e o initdb/30-easystok.sql
#   config/   .env (com EZ_CRYPTO_KEK e as KEKs anteriores), compose e wppconnect
#   volumes/  tar de cada volume (uploads, DataProtection, wppconnect)
#   git/      bundle do espelho da #2958 e o JSON de issues e PRs (ESPELHO_META), quando existirem
# Depois apaga o stage: dado em claro nao fica no disco entre execucoes.
#
# Retencao: forget por tag, SEM --prune. Quem apaga dado do Drive e a estacao (puxar.ps1).
# VPS e estacao usam o mesmo login OAuth (ezdrive.conf): um token roubado daqui apaga tudo
# no Drive. Risco aceito pelo dono, ver docs/dev/backup-drive.md.
#
# Saidas: 0 ok; 2 configuracao; 3 outro backup em andamento; 4 banco; 5 arquivos;
#         6 restic backup; 7 forget.
#
# Env (padrao): BASE (~/backup-restic), RESTIC (~/bin/restic), RCLONE (~/bin/rclone),
#   CONF_DIR (~/.config/easystok-backup), RESTIC_REPOSITORY (rclone:ezdrive:easystok-restic),
#   DB_CONTAINER (shared-postgres), DB_NAME (easystock), STACK_DIR (/opt/stacks/easystok),
#   SHARED_DIR (/opt/stacks/shared), VOLUMES, CONFIG_ARQUIVOS, ESPELHO_GIT (~/git/EasyStok.git),
#   ESPELHO_META (~/git/EasyStok-meta), HOST_TAG
#   (easystok-vps), IMAGEM_TAR (alpine:latest), ROTULOS_TABELAS (vazio), DIA_DO_MES (hoje).
# =============================================================================
set -euo pipefail

BASE="${BASE:-$HOME/backup-restic}"
RESTIC="${RESTIC:-$HOME/bin/restic}"
RCLONE="${RCLONE:-$HOME/bin/rclone}"
CONF_DIR="${CONF_DIR:-$HOME/.config/easystok-backup}"
export RESTIC_REPOSITORY="${RESTIC_REPOSITORY:-rclone:ezdrive:easystok-restic}"
export RESTIC_PASSWORD_FILE="${RESTIC_PASSWORD_FILE:-$CONF_DIR/restic.senha}"
export RCLONE_CONFIG="${RCLONE_CONFIG:-$CONF_DIR/ezdrive.conf}"
DB_CONTAINER="${DB_CONTAINER:-shared-postgres}"
DB_NAME="${DB_NAME:-easystock}"
STACK_DIR="${STACK_DIR:-/opt/stacks/easystok}"
SHARED_DIR="${SHARED_DIR:-/opt/stacks/shared}"
VOLUMES="${VOLUMES:-easystok_uploads-data easystok_dataprotection-api-keys easystok_dataprotection-web-keys easystok_dataprotection-admin-keys easystok-wppconnect-tokens easystok-wppconnect-user-data}"
# Lista fechada: as copias .bak e .pre-* dos .env tem a mesma KEK e segredos vencidos.
CONFIG_ARQUIVOS="${CONFIG_ARQUIVOS:-$STACK_DIR/.env $STACK_DIR/compose.yaml $STACK_DIR/compose.override.yaml $STACK_DIR/compose.wppconnect.yaml $STACK_DIR/compose.observabilidade.yaml $STACK_DIR/wppconnect/runtime.env $STACK_DIR/wppconnect/configurar.cjs $SHARED_DIR/compose.yaml $SHARED_DIR/Caddyfile}"
CONFIG_OBRIGATORIOS="${CONFIG_OBRIGATORIOS:-$STACK_DIR/.env $STACK_DIR/compose.yaml}"
INITDB="${INITDB:-$SHARED_DIR/initdb/30-easystok.sql}"
ESPELHO_GIT="${ESPELHO_GIT:-$HOME/git/EasyStok.git}"
ESPELHO_META="${ESPELHO_META:-$HOME/git/EasyStok-meta}"
HOST_TAG="${HOST_TAG:-easystok-vps}"
IMAGEM_TAR="${IMAGEM_TAR:-alpine:latest}"
ROTULOS_TABELAS="${ROTULOS_TABELAS:-}"
DIA_DO_MES="${DIA_DO_MES:-$(TZ=America/Sao_Paulo date +%d)}"
STAGE="$BASE/stage"

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }
falha() { local rc="$1"; shift; log "ERRO: $*"; grava_estado "$rc" "$*"; exit "$rc"; }
grava_estado() { local ESTADO="$BASE/estado-$MODO"; printf 'data=%s\nmodo=%s\nrc=%s\nmotivo=%s\n' "$(date -u +%FT%TZ)" "$MODO" "$1" "${2:-}" >"$ESTADO.tmp" && mv "$ESTADO.tmp" "$ESTADO"; }
r() { "$RESTIC" -o rclone.program="$RCLONE" --cache-dir "$BASE/cache" --no-progress "$@"; }

# Apaga so o conteudo do stage, que e nosso; o caminho e fixo para o restic achar o pai.
limpa_stage() { [ -d "$STAGE" ] && find "$STAGE" -mindepth 1 -delete; return 0; }

preflight() {
  [ -x "$RESTIC" ] || falha 2 "restic ausente em $RESTIC (rode instalar.ps1)"
  [ -x "$RCLONE" ] || falha 2 "rclone ausente em $RCLONE"
  [ -r "$RESTIC_PASSWORD_FILE" ] || falha 2 "senha do restic ausente em $RESTIC_PASSWORD_FILE"
  [ -r "$RCLONE_CONFIG" ] || falha 2 "ezdrive.conf ausente em $RCLONE_CONFIG"
}

# pg_dump e contagens no mesmo snapshot exportado: o manifesto descreve exatamente o dump,
# e a restauracao mensal compara contra ele, nao contra a producao que ja mudou.
dump_banco() {
  local d="$STAGE/db" tmp="/tmp/ezbk-$$"
  mkdir -p "$d"
  docker exec -i -e EZ_DB="$DB_NAME" -e EZ_OUT="$tmp" "$DB_CONTAINER" \
    psql -U postgres -d "$DB_NAME" -X -q -v ON_ERROR_STOP=1 <<'SQL' || falha 4 "pg_dump ou manifesto falhou"
BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT pg_export_snapshot() AS snap \gset
\setenv EZ_SNAP :snap
\! pg_dump -U postgres -Fc --snapshot="$EZ_SNAP" -f "$EZ_OUT.dump" "$EZ_DB"
\if :SHELL_ERROR
  SELECT 'pg_dump falhou'::int;
\endif
\pset tuples_only on
\pset format unaligned
\o /tmp/ezbk-manifesto
SELECT c.relname || '|' || (xpath('/row/n/text()', query_to_xml(
         format('SELECT count(*) AS n FROM %I.%I', n.nspname, c.relname), false, true, '')))[1]::text
  FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
 WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
 ORDER BY c.relname;
\o
COMMIT;
\! mv /tmp/ezbk-manifesto "$EZ_OUT.manifesto"
SQL
  docker exec "$DB_CONTAINER" cat "$tmp.dump" >"$d/$DB_NAME.dump" \
    && docker exec "$DB_CONTAINER" cat "$tmp.manifesto" >"$d/manifesto.txt"
  local rc=$?
  docker exec "$DB_CONTAINER" rm -f "$tmp.dump" "$tmp.manifesto" || true
  [ "$rc" = 0 ] && [ -s "$d/$DB_NAME.dump" ] && [ -s "$d/manifesto.txt" ] || falha 4 "dump ou manifesto vazio"
  docker exec "$DB_CONTAINER" pg_dumpall -U postgres --globals-only --no-role-passwords >"$d/globals.sql" \
    || falha 4 "pg_dumpall --globals-only falhou"
  [ -r "$INITDB" ] && cp "$INITDB" "$d/" || log "aviso: $INITDB ausente"
  {
    echo "data=$(date -u +%FT%TZ)"
    echo "banco=$DB_NAME"
    echo "postgres=$(docker exec "$DB_CONTAINER" postgres --version)"
    echo "sha_no_ar=$(cat "$HOME/build/easystok-current" 2>/dev/null || echo desconhecido)"
    echo "tabelas=$(wc -l <"$d/manifesto.txt")"
  } >"$d/info.txt"
  log "banco: $(du -h "$d/$DB_NAME.dump" | cut -f1), $(wc -l <"$d/manifesto.txt") tabelas no manifesto"
}

coleta_config() {
  local f destino
  for f in $CONFIG_OBRIGATORIOS; do [ -r "$f" ] || falha 5 "arquivo obrigatorio ausente: $f"; done
  for f in $CONFIG_ARQUIVOS; do
    if [ -r "$f" ]; then
      destino="$STAGE/config${f}"
      mkdir -p "$(dirname "$destino")" && cp -p "$f" "$destino" || falha 5 "copia de $f falhou"
    else
      log "aviso: $f ausente"
    fi
  done
  log "config: $(find "$STAGE/config" -type f | wc -l) arquivos"
}

# tar sem compressao: o restic deduplica entre dias; gzip mudaria todos os blocos.
coleta_volumes() {
  local v
  mkdir -p "$STAGE/volumes"
  for v in $VOLUMES; do
    docker run --rm --network none -v "$v":/d:ro "$IMAGEM_TAR" \
      tar -cf - --exclude='*/Cache' --exclude='*/Code Cache' --exclude='*/GPUCache' -C /d . \
      >"$STAGE/volumes/$v.tar" || falha 5 "tar do volume $v falhou"
    log "volume $v: $(du -h "$STAGE/volumes/$v.tar" | cut -f1)"
  done
}

bundle_git() {
  [ -n "$ESPELHO_GIT" ] || return 0
  if [ -d "$ESPELHO_GIT" ]; then
    mkdir -p "$STAGE/git"
    git -C "$ESPELHO_GIT" bundle create "$STAGE/git/easystok.bundle" --all 2>&1 || falha 5 "git bundle falhou"
    [ ! -d "$ESPELHO_META" ] || cp -r "$ESPELHO_META" "$STAGE/git/meta" || falha 5 "copia de $ESPELHO_META falhou"
  else
    log "aviso: espelho $ESPELHO_GIT ausente (#2958)"
  fi
}

# Retencao por tag; --group-by host,tags porque o caminho do dump pre-deploy muda a cada deploy.
esquece() {
  local tag="$1"; shift
  r forget --host "$HOST_TAG" --tag "$tag" --group-by host,tags "$@" || falha 7 "forget da tag $tag falhou"
}

# Rotulos (ADR-0012): 5 anos so das tabelas de rotulo, por minimizacao (LGPD). Sem tabela
# configurada, a retencao longa fica dormente e o log diz isso.
rotulos_mensal() {
  [ "$DIA_DO_MES" = 01 ] || return 0
  if [ -z "$ROTULOS_TABELAS" ]; then log "rotulos: nenhuma tabela configurada; retencao de 5 anos dormente"; return 0; fi
  local t args=()
  for t in $ROTULOS_TABELAS; do args+=(-t "$t"); done
  r backup --host "$HOST_TAG" --tag rotulos --stdin-filename "$DB_NAME-rotulos.dump" \
    --stdin-from-command -- docker exec "$DB_CONTAINER" pg_dump -U postgres -Fc "${args[@]}" "$DB_NAME" \
    || falha 6 "snapshot de rotulos falhou"
  esquece rotulos --keep-within 5y
}

diario() {
  exec 9>"$BASE/run.lock"
  flock -n 9 || { log "outro backup em andamento; saindo"; exit 3; }
  log "==> backup diario para $RESTIC_REPOSITORY"
  preflight
  trap 'limpa_stage' EXIT
  mkdir -p "$STAGE" && chmod 700 "$STAGE" && limpa_stage
  dump_banco
  coleta_config
  coleta_volumes
  bundle_git
  r backup --host "$HOST_TAG" --tag diario "$STAGE" || falha 6 "restic backup falhou"
  r unlock || true   # so remove lock velho, de execucao morta no meio (aceite 5)
  esquece diario --keep-daily 7 --keep-weekly 4 --keep-monthly 12
  rotulos_mensal
  grava_estado 0
  log "==> OK"
}

pre_deploy() {
  local dump="${1:-}"
  [ -s "$dump" ] || { echo "uso: backup.sh pre-deploy <dump>" >&2; exit 2; }
  exec 9>"$BASE/run.lock"
  flock -w 900 9 || { log "pre-deploy: trava ocupada por 15 min; dump fica so na VPS"; exit 3; }
  log "==> pre-deploy: $dump"
  preflight
  r backup --host "$HOST_TAG" --tag pre-deploy "$dump" || falha 6 "restic backup do pre-deploy falhou"
  esquece pre-deploy --keep-last 10
  log "==> OK pre-deploy"
}

main() {
  MODO="${1:-diario}"
  mkdir -p "$BASE"
  case "$MODO" in
    diario) diario ;;
    pre-deploy) pre_deploy "${2:-}" ;;
    *) echo "uso: backup.sh [diario | pre-deploy <dump>]" >&2; exit 2 ;;
  esac
}

MODO=diario
[ "${BACKUP_SO_FUNCOES:-0}" = 1 ] || main "$@"
