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
#
# F0.1 (2026-06-25): snapshot pre-deploy OBRIGATORIO antes do `up --build`.
#   A API auto-aplica migrations no boot (RunMigrationsOnStartup=true +
#   StartupMigrationsAndSeed.RunAsync). Sem snapshot previo, uma migration
#   destrutiva empurrada para master corromperia o banco de copia unica
#   IN-PLACE, sem humano e sem volta. O hook tira pg_dump + tar dos 3 volumes
#   nao-pg ANTES do build; se o dump falhar, ABORTA o deploy (nao deploya
#   sobre banco que nem da pra dumpar).
#
# Self-pull-safe: o corpo vive em main(), chamado no fim. Bash le a funcao
# inteira ANTES de executar, entao o `git pull` (que reescreve ESTE arquivo)
# nao troca o corpo em execucao no meio do deploy. O hook novo so passa a valer
# no PROXIMO deploy — comportamento determinístico, sem meia-versao.
set -euo pipefail

REPO="${EASYSTOK_REPO:-/home/azureuser/easystok}"
COMPOSE="$REPO/docker-compose.azure.yml"
BRANCH="${EASYSTOK_BRANCH:-master}"
BACKUP_ROOT="${EASYSTOK_BACKUP_ROOT:-/var/backups/easystok}"
PG_CONTAINER="${EASYSTOK_PG_CONTAINER:-easystok-postgres}"
RETENTION_DAYS="${EASYSTOK_BACKUP_RETENTION_DAYS:-7}"

# Snapshot pre-deploy: pg_dump (custom -Fc -Z9) + tar dos volumes nao-pg.
# Os 3 volumes sao parte da baseline, nao opcional: uploads-data (arquivos
# servidos por /files) e os 2 dataprotection-keys (sem eles, restore invalida
# os cookies de auth assinados => logout geral). Falha do pg_dump aborta (set -e).
snapshot_predeploy() {
  local ts dest v
  ts="$(date -u +%Y%m%d-%H%M%S)"
  dest="$BACKUP_ROOT/predeploy-$ts"
  mkdir -p "$dest"
  echo "[vm-deploy] snapshot pre-deploy -> $dest"
  # pg_dump com expansao DENTRO do container (le POSTGRES_USER/DB do env do container).
  if ! docker exec "$PG_CONTAINER" sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -Z9' > "$dest/pg.dump"; then
    echo "[vm-deploy] ERRO: pg_dump falhou — abortando deploy (banco sem snapshot)."
    exit 3
  fi
  for v in easystok_uploads-data easystok_dataprotection-admin-keys easystok_dataprotection-web-keys; do
    docker run --rm -v "$v":/data alpine tar -C /data -cf - . > "$dest/$v.tar"
    zstd -19 --rm "$dest/$v.tar"
  done
  sha256sum "$dest"/* > "$dest/SHA256SUMS"
  echo "[vm-deploy] snapshot OK ($(du -sh "$dest" | cut -f1))"
  # Rotacao: remove snapshots predeploy-* mais velhos que RETENTION_DAYS.
  # O `-name 'predeploy-*'` protege snapshots manuais/baseline (outro prefixo).
  find "$BACKUP_ROOT" -maxdepth 1 -type d -name 'predeploy-*' -mtime "+$RETENTION_DAYS" -exec rm -rf {} + 2>/dev/null || true
}

main() {
  local force="${1:-}"

  cd "$REPO"
  git config --global --add safe.directory "$REPO" 2>/dev/null || true
  git fetch origin "$BRANCH" --quiet

  local local_sha remote_sha
  local_sha="$(git rev-parse HEAD)"
  remote_sha="$(git rev-parse "origin/$BRANCH")"

  if [ "$local_sha" = "$remote_sha" ] && [ "$force" != "--force" ]; then
    echo "[vm-deploy] ja em ${local_sha:0:8} (origin/$BRANCH). Nada a fazer."
    exit 0
  fi

  echo "[vm-deploy] atualizando ${local_sha:0:8} -> ${remote_sha:0:8} ..."
  git pull --ff-only origin "$BRANCH"
  local sha
  sha="$(git rev-parse HEAD)"

  # F0.1: snapshot ANTES do build (a API migra no boot — banco de copia unica).
  snapshot_predeploy

  echo "[vm-deploy] rebuildando stack (GIT_SHA=${sha:0:8}) ..."
  GIT_SHA="$sha" docker compose -f "$COMPOSE" up -d --build

  sleep 8
  local web_sha
  web_sha="$(docker exec easystok-web printenv GIT_SHA 2>/dev/null || echo '?')"
  echo "[vm-deploy] OK. HEAD=${sha:0:8}  container_web GIT_SHA=${web_sha:0:8}"
  [ "$web_sha" = "$sha" ] || echo "[vm-deploy] AVISO: GIT_SHA do container difere do HEAD (rebuild pode ter usado cache)."
}

main "$@"
