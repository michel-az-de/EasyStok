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
    psql_ro) [ "$#" -eq 0 ] || nega "psql_ro recebe o SQL por stdin, sem argumentos" ;;
    *) nega "verbo desconhecido" ;;
  esac
}

# psql_ro: SQL via stdin, so leitura. A barreira real e o papel baba_ro no banco (apenas GRANT SELECT em
# tabelas de negocio, sem BYPASSRLS, sem escrita); aqui ficam so os limites de forma.
#   - sem barra invertida (meta-comandos do psql, como \!, executam shell dentro do container)
#   - ate 20 KB; transacao READ ONLY, timeout de 15 s e escopo de empresa fixo vindos do arquivo de config
# Config (root:root 600): PG_CONTAINER, PG_DB, PG_USER, PGPASSWORD, EMPRESA_ID.
psql_ro() {
  local envf="${BABA_RO_ENV:-/opt/baba-mcp/baba_ro.env}" sql
  sql="$(head -c 20001)"
  [ "${#sql}" -ge 1 ] && [ "${#sql}" -le 20000 ] || nega "sql vazio ou maior que 20000 bytes"
  case "$sql" in *\\*) nega "barra invertida no sql" ;; esac
  case "$sql" in *'/*'*|*'--'*) nega "comentarios nao sao permitidos" ;; esac
  # uma unica instrucao: ';' so no fim (um ';' no meio permitiria COMMIT/SET e sairia do READ ONLY e do escopo de empresa)
  sql="${sql%"${sql##*[![:space:];]}"}"
  case "$sql" in *\;*) nega "apenas uma instrucao" ;; esac
  # nomes que trocam escopo ou executam SQL montado em texto; compara sem espacos, aspas e comentarios
  local norm
  norm="$(printf '%s' "$sql" | sed -e 's#/\*[^*]*\*/##g' | tr -d '[:space:]"' | tr '[:upper:]' '[:lower:]')"
  case "$norm" in *set_config*|*app.empresa_id*|*app.bypass_rls*|*pg_sleep*|*dblink*|*lo_export*|*lo_import*|*lo_create*|*query_to_xml*|*cursor_to_xml*|*table_to_xml*|*schema_to_xml*|*database_to_xml*|*ts_stat*|*pg_read_*|*pg_ls_*) nega "funcao ou parametro nao permitido" ;; esac
  # so SELECT/WITH/VALUES/TABLE/EXPLAIN, sem escape unicode nem DO, e so funcoes da lista (mesma lista do cliente)
  [[ "$norm" =~ ^\(*(select|with|values|table|explain) ]] || nega "so consultas"
  [[ "$norm" =~ u\& ]] || [[ "$norm" =~ uescape ]] && nega "escape unicode"
  local nome lista=" $(tr '\n' ' ' < "${BABA_FUNCOES:-/opt/baba-mcp/psql_ro_funcoes.txt}") "
  while read -r nome; do
    [ -n "$nome" ] || continue
    [[ "$lista" == *" $nome "* ]] || nega "funcao nao permitida: $nome"
  done < <(printf '%s' "$sql" | tr '
' ' ' | tr -d '"' | tr '[:upper:]' '[:lower:]' | grep -oE '[a-z_][a-z0-9_]*[[:space:]]*\(' | sed -E 's/[[:space:]]*\($//')
  if [ -n "$GATE_DRY" ]; then echo "psql_ro ok (${#sql} bytes)"; exit 0; fi
  [ -r "$envf" ] || nega "sem configuracao do banco"
  # shellcheck disable=SC1090
  . "$envf"
  [[ "${EMPRESA_ID:-}" =~ ^[0-9a-fA-F-]{36}$ ]] || nega "EMPRESA_ID invalido"
  [[ "${PG_CONTAINER:-}" =~ ^[A-Za-z0-9_.-]+$ && "${PG_DB:-}" =~ ^[A-Za-z0-9_]+$ && "${PG_USER:-}" =~ ^[A-Za-z0-9_]+$ ]] || nega "configuracao invalida"
  export PGPASSWORD
  printf 'BEGIN READ ONLY;
SET LOCAL statement_timeout = 15000;
SET LOCAL app.empresa_id = %s;
%s
;
COMMIT;
' "'$EMPRESA_ID'" "$sql"     | timeout 25 "$DOCKER" exec -i -e PGPASSWORD "$PG_CONTAINER" psql -X -q -v ON_ERROR_STOP=1 --csv -U "$PG_USER" -d "$PG_DB" 2>&1
}

executa() {
  local verbo="$1"; shift
  case "$verbo" in
    psql_ro) psql_ro; exit 0 ;;
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
  # como root nada vem do ambiente: caminhos fixos, sem modo de teste
  DOCKER=/usr/bin/docker; HIST=/home/felipe/build/deploy-history.log; GATE_DRY=; BABA_RO_ENV=/opt/baba-mcp/baba_ro.env; BABA_FUNCOES=/opt/baba-mcp/psql_ro_funcoes.txt
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
if [ -n "$GATE_DRY" ] || [ -n "${GATE_LOCAL:-}" ] || [ "${partes[0]}" = "deploy_historico" ]; then executa "${partes[@]}"; exit 0; fi
exec sudo -n "$0" --root "${partes[@]}"
