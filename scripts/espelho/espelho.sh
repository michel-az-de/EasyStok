#!/usr/bin/env bash
# =============================================================================
# espelho.sh - espelho bare do repositorio do EasyStok na VPS (#2958).
#
# Roda NA VPS como felipe, pelo timer de usuario easystok-espelho.timer (a cada 15 min).
#   1. flock: uma execucao por vez
#   2. git fetch --prune so de refs/heads e refs/tags. Nao e `clone --mirror`: ele traria as
#      ~1300 refs/pull/* do GitHub, que nao sao historia do projeto e incham o bundle
#   3. uma vez por dia: exporta issues e PRs (JSON da API publica) para $META_DIR
#   4. grava $ESTADO (data, rc, sha do master) para o deploy e para o vigia
# O backup.sh (E3) leva o bundle deste espelho e o $META_DIR no snapshot diario.
# O vps-deploy.sh --origem espelho usa este espelho quando o GitHub esta fora.
#
# O repositorio e publico: clonar por HTTPS dispensa chave. Nada aqui escreve no GitHub.
#
# Saidas: 0 ok | 3 outra execucao em andamento | 4 clone ou fetch falhou | 5 master ausente
# Env (padrao): ORIGEM_URL (https://github.com/michel-az-de/EasyStok.git),
#   ESPELHO_GIT (~/git/EasyStok.git), META_DIR (~/git/EasyStok-meta), META_API
#   (https://api.github.com/repos/michel-az-de/EasyStok), META_HORAS (20; 0 desliga),
#   ESTADO (~/git/estado-espelho)
# =============================================================================
set -euo pipefail
ORIGEM_URL="${ORIGEM_URL:-https://github.com/michel-az-de/EasyStok.git}"
ESPELHO_GIT="${ESPELHO_GIT:-$HOME/git/EasyStok.git}"
META_DIR="${META_DIR:-$HOME/git/EasyStok-meta}"
META_API="${META_API:-https://api.github.com/repos/michel-az-de/EasyStok}"
META_HORAS="${META_HORAS:-20}"
ESTADO="${ESTADO:-$HOME/git/estado-espelho}"

log() { echo "$(date '+%Y-%m-%d %H:%M:%S') $*"; }
grava() { printf 'data=%s\nrc=%s\nmaster=%s\nmotivo=%s\n' "$(date -u +%FT%TZ)" "$1" "${2:-}" "${3:-}" >"$ESTADO.tmp" && mv "$ESTADO.tmp" "$ESTADO"; }
falha() { log "ERRO: $2"; grava "$1" "" "$2"; exit "$1"; }

mkdir -p "$(dirname "$ESPELHO_GIT")" "$(dirname "$ESTADO")"
exec 9>"$(dirname "$ESTADO")/.espelho.lock"
flock -n 9 || { log "outra execucao em andamento"; exit 3; }

if [ ! -d "$ESPELHO_GIT" ]; then
  git init -q --bare "$ESPELHO_GIT"
  git -C "$ESPELHO_GIT" remote add origin "$ORIGEM_URL"
  git -C "$ESPELHO_GIT" config remote.origin.fetch '+refs/heads/*:refs/heads/*'
  git -C "$ESPELHO_GIT" config --add remote.origin.fetch '+refs/tags/*:refs/tags/*'
fi
git -C "$ESPELHO_GIT" fetch -q --prune origin 2>&1 || falha 4 "fetch de $ORIGEM_URL falhou"
MASTER="$(git -C "$ESPELHO_GIT" rev-parse -q --verify 'refs/heads/master^{commit}')" || falha 5 "espelho sem refs/heads/master"
log "espelho em ${MASTER:0:8}"

# Issues e PRs: o bundle guarda o codigo, nao a conversa. Uma vez por dia, porque a API sem
# token permite 60 chamadas por hora e cada pagina traz 100 itens.
exporta_meta() {
  local tmp pagina=1 n url arq
  tmp="$(mktemp -d)"
  url="$META_API/issues?state=all&per_page=100"
  while [ -n "$url" ]; do   # cursor do Link rel="next": o GitHub recusa page= acima de 1000 itens
    arq="$tmp/issues-$(printf %03d "$pagina").json"
    curl -fsS -H 'Accept: application/vnd.github+json' -D "$tmp/cabecalho" "$url" -o "$arq" \
      || { rm -rf "$tmp"; mkdir -p "$META_DIR"; touch "$META_DIR"   # tenta de novo amanha: a API sem token da 60/h
           log "aviso: exportacao de issues falhou na pagina $pagina (mantida a anterior)"; return 0; }
    n="$(grep -c '"number":' "$arq" || true)"
    [ "$n" -gt 0 ] || { rm -f "$arq"; break; }
    url="$(tr -d '\r' < "$tmp/cabecalho" | sed -n 's/^[Ll]ink:.*<\([^>]*\)>; rel="next".*/\1/p')"
    pagina=$((pagina + 1))
  done
  rm -f "$tmp/cabecalho"
  rm -rf "$META_DIR"; mv "$tmp" "$META_DIR"
  log "issues e PRs exportados em $(ls "$META_DIR" | wc -l) pagina(s)"
}
if [ "$META_HORAS" != 0 ] && ! find "$META_DIR" -maxdepth 0 -mmin "-$((META_HORAS * 60))" 2>/dev/null | grep -q .; then
  exporta_meta
fi

grava 0 "$MASTER" ""
