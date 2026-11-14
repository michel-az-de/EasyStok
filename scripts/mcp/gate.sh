#!/usr/bin/env bash
# gate.sh - unica porta de entrada do MCP baba-diag na VPS (spec docs/plan/mcp-casa-da-baba, #2521).
#
# Instalacao (Felipe, uma vez, como root): ver tools/mcp-baba/README.md.
#   authorized_keys do usuario baba-mcp:
#     command="/opt/baba-mcp/gate.sh",restrict ssh-ed25519 AAAA... pessoa@maquina
#
# Contrato: o cliente manda UM comando de lista fixa em SSH_ORIGINAL_COMMAND.
#   logs <ez-api|ez-worker|ez-web> <since: 5m|2h> <tail: 1..5000>
#   containers
#   deploy_historico <linhas: 1..200>
# Qualquer outra coisa e recusada (exit 64). Nada vem de shell livre: os argumentos
# passam por regex estrita e o docker e chamado com argv fixo, sem eval.
# Roda como baba-mcp (sem grupo docker) e reexecuta a si mesmo por `sudo -n` para o trecho
# que precisa de docker; la valida de novo (defesa em profundidade).
set -euo pipefail
export PATH=/usr/sbin:/usr/bin:/sbin:/bin

HIST="${BABA_DEPLOY_HISTORY:-/home/felipe/build/deploy-history.log}"
DOCKER="${BABA_DOCKER:-/usr/bin/docker}"
GATE_DRY="${GATE_DRY:-}"   # teste: imprime o comando em vez de executar

nega() { echo "gate: recusado: $1" >&2; exit 64; }

valida() {
  local verbo="${1:-}"; shift || true
  case "$verbo" in
    logs)
      [ "$#" -eq 3 ] || nega "logs exige 3 argumentos"
      [[ "$1" =~ ^(ez-api|ez-worker|ez-web)$ ]] || nega "container"
      [[ "$2" =~ ^[0-9]{1,4}[smh]$ ]] || nega "since"
      [[ "$3" =~ ^[0-9]{1,4}$ ]] && [ "$3" -ge 1 ] && [ "$3" -le 5000 ] || nega "tail"
      ;;
    containers) [ "$#" -eq 0 ] || nega "containers nao recebe argumentos" ;;
    deploy_historico)
      [ "$#" -eq 1 ] || nega "deploy_historico exige 1 argumento"
      [[ "$1" =~ ^[0-9]{1,3}$ ]] && [ "$1" -ge 1 ] && [ "$1" -le 200 ] || nega "linhas"
      ;;
    *) nega "verbo desconhecido" ;;
  esac
}

executa() {
  local verbo="$1"; shift
  case "$verbo" in
    logs)
      set -- "$DOCKER" logs --timestamps --since "$2" --tail "$3" "$1" ;;
    containers)
      set -- "$DOCKER" ps -a --filter name=ez- --format '{{.Names}}|{{.Status}}|{{.Image}}'
      ;;
    deploy_historico)
      set -- tail -n "$1" "$HIST" ;;
  esac
  if [ -n "$GATE_DRY" ]; then printf '%s\n' "$*"; exit 0; fi
  "$@" 2>&1
}

# containers: alem do ps, inspeciona reinicios e GIT_SHA (sem imprimir as demais variaveis).
inspeciona() {
  local c
  for c in ez-api ez-worker ez-web; do
    "$DOCKER" inspect -f '{{.Name}}|restarts={{.RestartCount}}|health={{if .State.Health}}{{.State.Health.Status}}{{else}}sem{{end}}|running={{.State.Running}}' "$c" 2>&1 || true
  done
  "$DOCKER" inspect -f '{{range .Config.Env}}{{println .}}{{end}}' ez-api 2>/dev/null | sed -n 's/^GIT_SHA=/api_git_sha=/p' | head -1
}

if [ "${1:-}" = "--root" ]; then
  shift; valida "$@"
  if [ "$1" = "containers" ] && [ -z "$GATE_DRY" ]; then
    "$DOCKER" ps -a --filter name=ez- --format '{{.Names}}|{{.Status}}|{{.Image}}'; inspeciona; exit 0
  fi
  executa "$@"; exit 0
fi

cmd="${SSH_ORIGINAL_COMMAND:-${GATE_CMD:-}}"
[ -n "$cmd" ] || nega "sem comando"
[[ "$cmd" =~ ^[a-z_]+(\ [A-Za-z0-9-]+)*$ ]] || nega "caracteres invalidos"
read -r -a partes <<<"$cmd"
valida "${partes[@]}"
if [ -n "$GATE_DRY" ] || [ "${partes[0]}" = "deploy_historico" ]; then executa "${partes[@]}"; exit 0; fi
exec sudo -n "$0" --root "${partes[@]}"
