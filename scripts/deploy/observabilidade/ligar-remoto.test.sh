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
cat > "$W/bin/docker" <<'EOF'
#!/usr/bin/env bash
echo "docker $*" >> "$FAKE/docker.log"
case "$1 ${2:-}" in
  "compose config") [ -z "${FAKE_CONFIG_FALHA:-}" ] ;;
  "compose up")     echo "up COMPOSE_FILE=$(sed -n 's/^COMPOSE_FILE=//p' .env)" >> "$FAKE/docker.log" ;;
  inspect*)
    case "$3" in
      *Health*)  echo "${FAKE_SAUDE:-healthy}" ;;
      *Running*) echo true ;;
    esac ;;
  logs*) : ;;
esac
EOF
printf '#!/usr/bin/env bash\nexit 0\n' > "$W/bin/flock"
chmod +x "$W/bin/docker" "$W/bin/flock"

# roda <conteudo do .env> <acao> [env extra...]: imprime rc, saida, log do docker e o .env final
roda() {
  local conteudo="$1" acao="$2"; shift 2
  local s; s="$(mktemp -d)"
  printf '%s\n' "$conteudo" > "$s/.env"
  [ -n "${COM_OVERRIDE:-}" ] && touch "$s/compose.override.yaml"
  rm -f "$W/docker.log"
  local rc out
  out=$(env PATH="$W/bin:$PATH" FAKE="$W" LOCK="$W/lock" HEALTH_TIMEOUT=0 ESPERA=0 "$@" bash "$SCRIPT" "$s" "$acao" 2>&1) && rc=0 || rc=$?
  echo "rc=$rc"; echo "$out"; echo "--- docker"; cat "$W/docker.log" 2>/dev/null || true; echo "--- env"; cat "$s/.env"
}

PROD=$'EZ_DB_PASSWORD=x\nCOMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml'

r=$(roda "$PROD" ligar)
confere "ligar: exit 0"                      "rc=0" "$r"
confere "ligar: acrescenta no fim"           "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml:compose.observabilidade.yaml$" "$r"
confere "ligar: valida o compose antes"      "docker compose config -q" "$r"
confere "ligar: recria api web worker"       "docker compose up -d api web worker" "$r"
confere "ligar: mantem as outras chaves"     "^EZ_DB_PASSWORD=x$" "$r"

r=$(roda $'COMPOSE_FILE=compose.yaml:compose.observabilidade.yaml' ligar)
confere "ligar de novo: nao duplica"         "^COMPOSE_FILE=compose.yaml:compose.observabilidade.yaml$" "$r"

r=$(roda "$PROD" ligar FAKE_SAUDE=unhealthy)
confere "nao saudavel: exit 6"               "rc=6" "$r"
confere "nao saudavel: restaura"             "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml$" "$r"
confere "nao saudavel: sobe de novo sem obs" "up COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml$" "$r"

r=$(roda "$PROD" ligar FAKE_CONFIG_FALHA=1)
confere "compose invalido: exit 4"           "rc=4" "$r"
confere "compose invalido: restaura"         "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml$" "$r"
nao_tem  "compose invalido: nao recria"      "compose up" "$r"

r=$(roda $'COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.observabilidade.yaml:compose.wppconnect.yaml' desligar)
confere "desligar: tira so o override"       "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.wppconnect.yaml$" "$r"

r=$(COM_OVERRIDE=1 roda 'EZ_DB_PASSWORD=x' ligar)
confere "sem COMPOSE_FILE: preserva o padrao" "^COMPOSE_FILE=compose.yaml:compose.override.yaml:compose.observabilidade.yaml$" "$r"

r=$(roda 'EZ_DB_PASSWORD=x' ligar FAKE_SAUDE=unhealthy)
confere "sem COMPOSE_FILE e falha: exit 6"   "rc=6" "$r"
nao_tem  "sem COMPOSE_FILE e falha: tira a linha" "^COMPOSE_FILE=" "$r"

r=$(roda "$PROD" outra)
confere "acao invalida: exit 2"              "rc=2" "$r"

echo
[ "$falhas" -eq 0 ] && echo "TODOS OS TESTES PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
