#!/usr/bin/env bash
# Vigia externo da producao (#2107). Roda no GitHub Actions, fora da VPS, a cada 5 min.
# Testa cada alvo de alvos.txt; se algum cair, abre UMA issue com label `uptime` atribuida ao
# responsavel (o app do GitHub avisa no celular). Enquanto a falha for a mesma, fica quieto;
# se mudar, troca o titulo e comenta; quando tudo voltar, comenta e fecha.
#
# Env: ALVOS (arquivo), ALVO_EXTRA ("nome|url|texto", teste de disparo), RESPONSAVEL,
#      TENTATIVAS (2), ESPERA entre tentativas em s (20), TIMEOUT por chamada em s (15).
# Saida: 0 = checou e a issue esta coerente; 2 = ALVO_EXTRA invalido; outro = o vigia quebrou.
set -euo pipefail

ALVOS="${ALVOS:-$(dirname "$0")/alvos.txt}"
RESPONSAVEL="${RESPONSAVEL:-michel-az-de}"
TENTATIVAS="${TENTATIVAS:-2}"
ESPERA="${ESPERA:-20}"
TIMEOUT="${TIMEOUT:-15}"
LABEL=uptime
PREFIXO="[uptime] Fora do ar: "

agora() { TZ=America/Sao_Paulo date '+%d/%m %H:%M'; }

lista=$(tr -d '\r' <"$ALVOS" | grep -v -E '^\s*(#|$)')
if [ -n "${ALVO_EXTRA:-}" ]; then
  [[ "$ALVO_EXTRA" == *"|http"*"|"* ]] || { echo "ALVO_EXTRA invalido, use nome|url|texto: $ALVO_EXTRA" >&2; exit 2; }
  lista="$lista"$'\n'"$ALVO_EXTRA"
fi

corpo_tmp=$(mktemp)
trap 'rm -f "$corpo_tmp"' EXIT

# checa <url> <texto>: imprime vazio se ok, ou o motivo da falha
checa() {
  local url="$1" texto="$2" codigo
  codigo=$(curl -sS -o "$corpo_tmp" -w '%{http_code}' --max-time "$TIMEOUT" "$url" 2>/dev/null) || true
  if [ -z "$codigo" ] || [ "$codigo" = 000 ]; then echo "sem resposta em ${TIMEOUT}s"; return; fi
  if [ "$codigo" != 200 ]; then echo "HTTP $codigo"; return; fi
  if [ -n "$texto" ] && ! grep -q -F -- "$texto" "$corpo_tmp"; then echo "HTTP 200 sem o texto esperado ($texto)"; fi
}

nomes=() detalhes=""
while IFS='|' read -r nome url texto; do
  motivo=""
  for ((t = 1; t <= TENTATIVAS; t++)); do
    : > "$corpo_tmp"
    motivo=$(checa "$url" "$texto")
    [ -z "$motivo" ] && break
    [ "$t" -lt "$TENTATIVAS" ] && sleep "$ESPERA"
  done
  if [ -n "$motivo" ]; then
    nomes+=("$nome")
    detalhes+="| $nome | $url | $motivo |"$'\n'
    echo "FORA  $nome: $motivo"
  else
    echo "ok    $nome"
  fi
done <<<"$lista"

aberta=$(gh issue list --label "$LABEL" --state open --limit 1 --json number --jq '.[0].number // empty')

if [ "${#nomes[@]}" -eq 0 ]; then
  if [ -n "$aberta" ]; then
    gh issue comment "$aberta" --body "Voltou: todos os alvos responderam em $(agora) (horario de Brasilia)."
    gh issue close "$aberta"
  fi
  exit 0
fi

titulo="$PREFIXO$(IFS=,; echo "${nomes[*]}" | sed 's/,/, /g')"
corpo="Detectado em $(agora) (horario de Brasilia), $TENTATIVAS tentativas com ${ESPERA}s de intervalo.

| Alvo | URL | Falha |
|---|---|---|
$detalhes
Primeiro passo: abrir https://api.easystok.online/health e ver qual check caiu.
Esta issue fecha sozinha quando tudo voltar. Run: ${GITHUB_SERVER_URL:-}/${GITHUB_REPOSITORY:-}/actions/runs/${GITHUB_RUN_ID:-}"

if [ -z "$aberta" ]; then
  gh label create "$LABEL" --color B60205 --description "Queda detectada pelo vigia externo (#2107)" >/dev/null 2>&1 || true
  gh issue create --title "$titulo" --body "$corpo" --assignee "$RESPONSAVEL" --label "$LABEL" --label infra --label priority:p0
elif [ "$(gh issue view "$aberta" --json title --jq .title)" != "$titulo" ]; then
  gh issue edit "$aberta" --title "$titulo"
  gh issue comment "$aberta" --body "A falha mudou:
$corpo"
fi
