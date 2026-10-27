#!/usr/bin/env bash
# Testes do ligar-remoto.sh (#2107), sem VPS: `docker` e `flock` falsos no PATH, stack num diretório temporário.
# Uso: bash scripts/deploy/observabilidade/ligar-remoto.test.sh
set -euo pipefail
cd "$(dirname "$0")"
SCRIPT="$(pwd)/ligar-remoto.sh"

falhas=0
confere() { if grep -q -- "$2" <<<"$3"; then echo "ok   $1"; else echo "FAIL $1: esperava '$2'"; echo "$3" | sed 's/^/     /'; falhas=$((falhas+1)); fi; }
nao_tem() { if grep -q -- "$2" <<<"$3"; then echo "FAIL $1: nao esperava '$2'"; echo "$3" | sed 's/^/     /'; falhas=$((falhas+1)); else echo "ok   $1"; fi; }

W="$(mktemp -d)"
mkdir -p "$W/bin"
# docker falso. Antes do 2o `compose up` (o do rollback) a saude e FAKE_SAUDE; depois, FAKE_SAUDE_2.
# FAKE_UP_FALHA faz o 1o `compose up` sair com erro.
cat > "$W/bin/docker" <<'FIM'
#!/usr/bin/env bash
echo "docker $*" >> "$FAKE/docker.log"
ups=$(grep -c "^up " "$FAKE/docker.log" 2>/dev/null || true)
case "$1 ${2:-}" in
  "compose config") [ -z "${FAKE_CONFIG_FALHA:-}" ] ;;
  "compose up")
    echo "up COMPOSE_FILE=$(sed -n 's/^COMPOSE_FILE=//p' .env) override=$(cat compose.observabilidade.yaml 2>/dev/null)" >> "$FAKE/docker.log"
    if [ "$ups" -eq 0 ] && [ -n "${FAKE_UP_FALHA:-}" ]; then exit 1; fi ;;
  inspect*)
    saude="${FAKE_SAUDE:-healthy}"; [ "$ups" -ge 2 ] && saude="${FAKE_SAUDE_2:-healthy}"
    case "$3" in
      *Health*)
        if [ "$4" = ez-worker ]; then echo "${FAKE_SAUDE_WORKER:-$saude}"; else echo "$saude"; fi ;;
      *Running*)      echo true ;;
      *RestartCount*)
        # O container recriado no rollback comeca com RestartCount zerado.
        if [ "$ups" -ge 2 ]; then echo 0; else echo "${FAKE_REINICIOS:-0}"; fi ;;
    esac ;;
  logs*) : ;;
esac
FIM
printf '#!/usr/bin/env bash\nexit 0\n' > "$W/bin/flock"
chmod +x "$W/bin/docker" "$W/bin/flock"

# roda <conteudo do .env> <acao> [env extra...]: imprime rc, saida, log do docker, o .env e o override finais.
# ARQ_ATUAL e ARQ_NOVO criam compose.observabilidade.yaml e .novo com esse conteudo.
roda() {
  local conteudo="$1" acao="$2"; shift 2
  local s; s="$(mktemp -d)"
  printf '%s\n' "$conteudo" > "$s/.env"
  [ -n "${COM_OVERRIDE:-}" ] && touch "$s/compose.override.yaml"
  [ -n "${ARQ_ATUAL:-}" ] && printf '%s' "$ARQ_ATUAL" > "$s/compose.observabilidade.yaml"
  [ -n "${ARQ_NOVO:-}" ] && printf '%s' "$ARQ_NOVO" > "$s/compose.observabilidade.yaml.novo"
  rm -f "$W/docker.log"
  local rc out
  out=$(env PATH="$W/bin:$PATH" FAKE="$W" LOCK="$W/lock" HEALTH_TIMEOUT=0 ESPERA=0 "$@" bash "$SCRIPT" "$s" "$acao" 2>&1) && rc=0 || rc=$?
  echo "rc=$rc"; echo "$out"; echo "--- docker"; cat "$W/docker.log" 2>/dev/null || true; echo "--- env"; cat "$s/.env"
  echo "--- override: $(cat "$s/compose.observabilidade.yaml" 2>/dev/null) | novo restante: $(find "$s" -name 'compose.observabilidade.yaml.novo' | wc -l)"
}

PROD=$'EZ_DB_PASSWORD=x\nCOMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml'

r=$(ARQ_NOVO=novo roda "$PROD" ligar)
confere "ligar: exit 0"                      "rc=0" "$r"
confere "ligar: acrescenta no fim"           "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml:compose.observabilidade.yaml$" "$r"
confere "ligar: valida o compose antes"      "docker compose config -q" "$r"
confere "ligar: recria api web worker"       "docker compose up -d api web worker" "$r"
confere "ligar: mantem as outras chaves"     "^EZ_DB_PASSWORD=x$" "$r"
confere "ligar: promove o override novo"     "override: novo | novo restante: 0" "$r"

r=$(roda $'COMPOSE_FILE=compose.yaml:compose.observabilidade.yaml' ligar)
confere "ligar de novo: nao duplica"         "^COMPOSE_FILE=compose.yaml:compose.observabilidade.yaml$" "$r"

r=$(roda "$PROD" ligar FAKE_SAUDE=unhealthy)
confere "nao saudavel: exit 6"               "rc=6" "$r"
confere "nao saudavel: restaura"             "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml$" "$r"
confere "nao saudavel: sobe de novo sem obs" "up COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml override" "$r"

r=$(roda "$PROD" ligar FAKE_CONFIG_FALHA=1)
confere "compose invalido: exit 4"           "rc=4" "$r"
confere "compose invalido: restaura"         "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml$" "$r"
nao_tem  "compose invalido: nao recria"      "compose up" "$r"

r=$(ARQ_ATUAL=velho ARQ_NOVO=novo roda $'COMPOSE_FILE=compose.yaml:compose.observabilidade.yaml' ligar FAKE_CONFIG_FALHA=1)
confere "compose invalido: devolve o override velho" "override: velho " "$r"

r=$(roda $'COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.observabilidade.yaml:compose.wppconnect.yaml' desligar)
confere "desligar: tira so o override"       "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml$" "$r"

r=$(COM_OVERRIDE=1 roda 'EZ_DB_PASSWORD=x' ligar)
confere "sem COMPOSE_FILE: preserva o padrao" "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.observabilidade.yaml$" "$r"

r=$(roda 'EZ_DB_PASSWORD=x' ligar FAKE_SAUDE=unhealthy)
confere "sem COMPOSE_FILE e falha: exit 6"   "rc=6" "$r"
nao_tem  "sem COMPOSE_FILE e falha: tira a linha" "^COMPOSE_FILE=" "$r"

r=$(roda "$PROD" outra)
confere "acao invalida: exit 2"              "rc=2" "$r"

# Revisao da #2116
r=$(roda "$PROD" ligar FAKE_UP_FALHA=1)
confere "up falha: exit 6 (nao morre no set -e)" "rc=6" "$r"
confere "up falha: restaura"                 "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml$" "$r"

r=$(ARQ_ATUAL=velho ARQ_NOVO=novo roda $'COMPOSE_FILE=compose.yaml:compose.observabilidade.yaml' ligar FAKE_SAUDE=unhealthy)
confere "override novo ruim: exit 6"         "rc=6" "$r"
confere "override novo ruim: volta o arquivo" "override: velho " "$r"
confere "override novo ruim: rollback sobe o velho" "override=velho$" "$r"

r=$(roda "$PROD" ligar FAKE_SAUDE=unhealthy FAKE_SAUDE_2=unhealthy)
confere "rollback tambem ruim: exit 7"       "rc=7" "$r"
confere "rollback tambem ruim: avisa"        "ROLLBACK NAO FICOU SAUDAVEL" "$r"

r=$(roda "$PROD" ligar FAKE_REINICIOS=2)
confere "worker reiniciando: exit 6"         "rc=6" "$r"

r=$(roda "$PROD" ligar FAKE_SAUDE_WORKER=unhealthy)
confere "worker de imagem antiga (sem arquivo de vida): liga" "rc=0" "$r"

echo
[ "$falhas" -eq 0 ] && echo "TODOS OS TESTES PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
