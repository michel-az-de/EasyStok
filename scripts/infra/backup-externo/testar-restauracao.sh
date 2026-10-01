#!/usr/bin/env bash
# =============================================================================
# testar-restauracao.sh - prova que o backup externo restaura. Issue #1253.
#
# Roda NA VPS (via testar-restauracao.ps1). Nao toca no banco de producao:
#   1. baixa (e decifra) o dump do dia mais recente de $DESTINO/diario/
#   2. sobe um postgres descartavel: docker run --rm, mesma imagem do
#      $DB_CONTAINER, porta aleatoria em 127.0.0.1, sem volume
#   3. pg_restore --list (catalogo legivel) e restore real em restore_teste
#   4. lista as tabelas e conta as linhas estimadas; confere o tar de uploads
#   5. derruba o container (trap), mesmo em falha
# =============================================================================
set -euo pipefail

export PATH="$HOME/bin:$PATH"
export RCLONE_CONFIG="${RCLONE_CONFIG:-$HOME/.config/easystok-backup/rclone.conf}"
DESTINO="${DESTINO:-gdrive-crypt:easystok}"
DB_CONTAINER="${DB_CONTAINER:-shared-postgres}"
PG_IMAGEM="${PG_IMAGEM:-$(docker inspect -f '{{.Config.Image}}' "$DB_CONTAINER" 2>/dev/null || echo postgres:16)}"
NOME="ez-restore-teste-$$"

tmp="$(mktemp -d)"
limpar() { docker rm -f "$NOME" >/dev/null 2>&1 || true; rm -rf "$tmp"; }
trap limpar EXIT

dia="$(rclone lsf --dirs-only "$DESTINO/diario/" | sed 's#/$##' | grep -E '^[0-9]{4}-[0-9]{2}-[0-9]{2}$' | sort -r | head -1)"
[ -n "$dia" ] || { echo "ERRO: nenhum backup em $DESTINO/diario/" >&2; exit 2; }
echo "==> [1/5] baixando diario/$dia"
rclone copy "$DESTINO/diario/$dia" "$tmp"
ls -lh "$tmp"
dump="$(find "$tmp" -maxdepth 1 -type f \( -name '*.dump' -o -name '*.sql.gz' \) | head -1)"
[ -n "$dump" ] || { echo "ERRO: diario/$dia sem dump" >&2; exit 2; }

echo "==> [2/5] postgres descartavel ($PG_IMAGEM)"
docker run -d --rm --name "$NOME" -p 127.0.0.1::5432 \
  -e POSTGRES_HOST_AUTH_METHOD=trust "$PG_IMAGEM" >/dev/null
echo "porta: $(docker port "$NOME" 5432)"
for _ in $(seq 1 60); do docker exec "$NOME" pg_isready -U postgres >/dev/null 2>&1 && break; sleep 1; done
docker exec "$NOME" pg_isready -U postgres >/dev/null || { echo "ERRO: postgres descartavel nao subiu" >&2; exit 3; }
docker cp "$dump" "$NOME:/tmp/$(basename "$dump")"
alvo="/tmp/$(basename "$dump")"
docker exec "$NOME" createdb -U postgres restore_teste

case "$dump" in
  *.dump)
    echo "==> [3/5] pg_restore --list"
    itens="$(docker exec "$NOME" pg_restore --list "$alvo" | grep -vc '^;' || true)"
    echo "itens no catalogo: $itens"
    [ "$itens" -gt 0 ] || { echo "ERRO: catalogo vazio" >&2; exit 4; }
    echo "==> [3/5] restore real em restore_teste"
    docker exec "$NOME" pg_restore -U postgres -d restore_teste --no-owner --no-acl --exit-on-error "$alvo" ;;
  *.sql.gz)
    echo "==> [3/5] restore real (sql.gz) em restore_teste"
    docker exec "$NOME" sh -c "gunzip -c '$alvo' | psql -q -v ON_ERROR_STOP=1 -U postgres -d restore_teste" ;;
esac

echo "==> [4/5] tabelas restauradas"
docker exec "$NOME" psql -U postgres -d restore_teste -Atc "ANALYZE" >/dev/null
docker exec "$NOME" psql -U postgres -d restore_teste -c \
  "SELECT schemaname, relname, n_live_tup AS linhas FROM pg_stat_user_tables ORDER BY n_live_tup DESC, relname"
n="$(docker exec "$NOME" psql -U postgres -d restore_teste -Atc "SELECT count(*) FROM pg_stat_user_tables")"
[ "$n" -gt 0 ] || { echo "ERRO: nenhuma tabela restaurada" >&2; exit 5; }

up="$(find "$tmp" -maxdepth 1 -name 'uploads-*.tar.gz' | head -1)"
if [ -n "$up" ]; then
  echo "uploads: $(tar tzf "$up" | wc -l) entradas legiveis em $(basename "$up")"
else
  echo "AVISO: diario/$dia sem tar de uploads" >&2
fi

echo "==> [5/5] OK: $n tabelas restauradas de diario/$dia. Container $NOME sera removido."
