#!/usr/bin/env bash
# =============================================================================
# espelho.sh - espelho bare do repositorio do EasyStok na VPS (#2958).
#
# Roda NA VPS como felipe, pelo timer de usuario easystok-espelho.timer (a cada 15 min).
#   1. flock: uma execucao por vez
#   2. sem espelho: git clone --mirror; com espelho: git remote update --prune
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

if [ -d "$ESPELHO_GIT" ]; then
  git -C "$ESPELHO_GIT" remote update --prune >/dev/null 2>&1 || falha 4 "fetch de $ORIGEM_URL falhou"
else
  git clone -q --mirror "$ORIGEM_URL" "$ESPELHO_GIT" || falha 4 "clone de $ORIGEM_URL falhou"
fi
MASTER="$(git -C "$ESPELHO_GIT" rev-parse -q --verify 'refs/heads/master^{commit}')" || falha 5 "espelho sem refs/heads/master"
log "espelho em ${MASTER:0:8}"

# Issues e PRs: o bundle guarda o codigo, nao a conversa. Uma vez por dia, porque a API sem
# token permite 60 chamadas por hora e cada pagina traz 100 itens.
exporta_meta() {
  local tmp pagina=1 n
  tmp="$(mktemp -d)"
  while :; do
    curl -fsS -H 'Accept: application/vnd.github+json' \
      "$META_API/issues?state=all&per_page=100&page=$pagina" -o "$tmp/issues-$(printf %03d "$pagina").json" \
      || { rm -rf "$tmp"; log "aviso: exportacao de issues falhou na pagina $pagina (mantida a anterior)"; return 0; }
    n="$(grep -c '"number":' "$tmp/issues-$(printf %03d "$pagina").json" || true)"
    [ "$n" -gt 0 ] || { rm -f "$tmp/issues-$(printf %03d "$pagina").json"; break; }
    pagina=$((pagina + 1))
  done
  rm -rf "$META_DIR"; mv "$tmp" "$META_DIR"
  log "issues e PRs exportados em $((pagina - 1)) pagina(s)"
}
if [ "$META_HORAS" != 0 ] && ! find "$META_DIR" -maxdepth 0 -mmin "-$((META_HORAS * 60))" 2>/dev/null | grep -q .; then
  exporta_meta
fi

grava 0 "$MASTER" ""
