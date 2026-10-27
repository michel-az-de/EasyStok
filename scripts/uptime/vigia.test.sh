#!/usr/bin/env bash
# Testes do vigia.sh (#2107), sem rede e sem GitHub: `curl` e `gh` falsos no PATH.
# Uso: bash scripts/uptime/vigia.test.sh
set -euo pipefail
cd "$(dirname "$0")"
SCRIPT="$(pwd)/vigia.sh"

falhas=0
confere()  { if grep -q -- "$2" <<<"$3"; then echo "ok   $1"; else echo "FAIL $1: esperava '$2'"; echo "$3" | sed 's/^/     /'; falhas=$((falhas+1)); fi; }
nao_tem()  { if grep -q -- "$2" <<<"$3"; then echo "FAIL $1: nao esperava '$2'"; echo "$3" | sed 's/^/     /'; falhas=$((falhas+1)); else echo "ok   $1"; fi; }

W="$(mktemp -d)"
mkdir -p "$W/bin"

# curl falso: responde pela tabela $W/respostas ("url codigo corpo"). Se a URL tiver
# duas linhas, a primeira chamada usa a primeira e as seguintes usam a segunda.
cat > "$W/bin/curl" <<'EOF'
#!/usr/bin/env bash
out="" url=""
while [ $# -gt 0 ]; do
  case "$1" in
    -o) out="$2"; shift 2 ;;
    -w|--max-time) shift 2 ;;
    -*) shift ;;
    *) url="$1"; shift ;;
  esac
done
n=$(grep -c -F -- "$url " "$FAKE/chamadas" 2>/dev/null || true)
echo "$url " >> "$FAKE/chamadas"
linhas=$(grep -F -- "$url " "$FAKE/respostas" || true)
total=$(printf '%s\n' "$linhas" | grep -c . || true)
[ "$total" -eq 0 ] && { printf '000'; exit 28; }
idx=$(( n + 1 )); [ "$idx" -gt "$total" ] && idx=$total
linha=$(printf '%s\n' "$linhas" | sed -n "${idx}p")
codigo=$(cut -d' ' -f2 <<<"$linha"); corpo=$(cut -d' ' -f3- <<<"$linha")
[ -n "$out" ] && printf '%s' "$corpo" > "$out"
printf '%s' "$codigo"
[ "$codigo" = 000 ] && exit 28
exit 0
EOF

# gh falso: issue aberta em $W/aberta (numero), titulo em $W/titulo, chamadas em $W/gh.log.
cat > "$W/bin/gh" <<'EOF'
#!/usr/bin/env bash
echo "gh $*" >> "$FAKE/gh.log"
case "$1 $2" in
  "issue list")    cat "$FAKE/aberta" 2>/dev/null || true ;;
  "issue view")    cat "$FAKE/titulo" ;;
  "issue create")  echo 77 > "$FAKE/aberta"; echo "https://github.com/x/y/issues/77" ;;
  "issue close")   rm -f "$FAKE/aberta" ;;
  *) : ;;
esac
EOF
chmod +x "$W/bin/curl" "$W/bin/gh"

cat > "$W/alvos.txt" <<'EOF'
# comentario ignorado
EZ API|https://api.test/health/ready|
EZ Console|https://app.test/|easystok-console-sha
EOF

# roda <respostas> [issue aberta] [titulo] [env extra...]: imprime rc, saida e o log do gh
roda() {
  local respostas="$1" aberta="${2:-}" titulo="${3:-}"; shift 3 || shift $#
  rm -f "$W/chamadas" "$W/gh.log" "$W/aberta" "$W/titulo"
  printf '%s\n' "$respostas" > "$W/respostas"
  [ -n "$aberta" ] && echo "$aberta" > "$W/aberta"
  [ -n "$titulo" ] && echo "$titulo" > "$W/titulo"
  local rc out
  out=$(env PATH="$W/bin:$PATH" FAKE="$W" ALVOS="$W/alvos.txt" ESPERA=0 "$@" bash "$SCRIPT" 2>&1) && rc=0 || rc=$?
  echo "rc=$rc"; echo "$out"; echo "--- gh"; cat "$W/gh.log" 2>/dev/null || true
}

OK_API='https://api.test/health/ready 200 {"status":"Healthy"}'
OK_CON='https://app.test/ 200 <meta name="easystok-console-sha">'

r=$(roda "$OK_API
$OK_CON" "" "")
confere "tudo no ar: exit 0"                 "rc=0" "$r"
nao_tem  "tudo no ar: nao abre issue"        "issue create" "$r"
nao_tem  "tudo no ar: nao comenta"           "issue comment" "$r"

r=$(roda "https://api.test/health/ready 503 {\"status\":\"Unhealthy\"}
$OK_CON" "" "")
confere "API 503: exit 0"                    "rc=0" "$r"
confere "API 503: abre issue"                "issue create" "$r"
confere "API 503: titulo com o alvo"         "Fora do ar: EZ API" "$r"
confere "API 503: atribui ao responsavel"    "--assignee michel-az-de" "$r"
confere "API 503: label uptime"              "--label uptime" "$r"
confere "API 503: corpo diz o codigo"        "HTTP 503" "$r"
nao_tem  "API 503: console nao entra"        "Fora do ar: EZ API, EZ Console" "$r"

r=$(roda "https://api.test/health/ready 503 x
$OK_API
$OK_CON" "" "")
nao_tem  "falha so na 1a tentativa: nao abre issue" "issue create" "$r"

r=$(roda "$OK_API
https://app.test/ 200 <html>pagina de erro</html>" "" "")
confere "console sem o texto: abre issue"    "Fora do ar: EZ Console" "$r"
confere "console sem o texto: diz o motivo"  "sem o texto esperado" "$r"

r=$(roda "$OK_CON" "" "")
confere "API sem resposta: abre issue"       "Fora do ar: EZ API" "$r"
confere "API sem resposta: diz o motivo"     "sem resposta" "$r"

r=$(roda "https://api.test/health/ready 503 x
$OK_CON" 41 "[uptime] Fora do ar: EZ API")
nao_tem  "mesma falha com issue aberta: nao abre outra" "issue create" "$r"
nao_tem  "mesma falha com issue aberta: nao comenta"    "issue comment" "$r"

r=$(roda "https://api.test/health/ready 503 x
https://app.test/ 502 x" 41 "[uptime] Fora do ar: EZ API")
confere "falha mudou: troca o titulo"        "issue edit 41 --title \[uptime\] Fora do ar: EZ API, EZ Console" "$r"
confere "falha mudou: comenta"               "issue comment 41" "$r"

r=$(roda "$OK_API
$OK_CON" 41 "[uptime] Fora do ar: EZ API")
confere "voltou: comenta"                    "issue comment 41" "$r"
confere "voltou: fecha"                      "issue close 41" "$r"

r=$(roda "$OK_API
$OK_CON" "" "" ALVO_EXTRA='TESTE disparo|https://api.test/nao-existe|')
confere "alvo extra: entra na checagem"      "Fora do ar: TESTE disparo" "$r"

r=$(roda "$OK_API
$OK_CON" "" "" ALVO_EXTRA='lixo sem barra')
confere "alvo extra invalido: exit 2"        "rc=2" "$r"

echo
[ "$falhas" -eq 0 ] && echo "TODOS OS TESTES PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
