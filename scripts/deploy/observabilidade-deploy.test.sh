#!/usr/bin/env bash
# Teste do `subir` do observabilidade-deploy.sh (#2440), sem VPS: `ssh` falso que roda o `bash -s` local, `scp` e `curl`
# falsos. Cobre o token de ingestão do collector: marca "já existe" no .env não basta, o token tem de ser aceito.
# Roda à mão (não está no CI): bash scripts/deploy/observabilidade-deploy.test.sh [script para a prova Red]
set -euo pipefail

SCRIPT="${1:-$(cd "$(dirname "$0")" && pwd)/observabilidade-deploy.sh}"
DIR_REAL="$(cd "$(dirname "$0")" && pwd)"
W="$(mktemp -d)"
trap 'rm -rf -- "$W"' EXIT
mkdir "$W/bin" "$W/stack"
# O script resolve observabilidade/ ao lado de si; a prova Red usa uma cópia da versão antiga.
mkdir -p "$W/s" && cp -r "$DIR_REAL/observabilidade" "$W/s/" && cp "$SCRIPT" "$W/s/observabilidade-deploy.sh"

# ssh falso: `bash -s -- args` roda local, com o stdin do heredoc; o resto (healthz, docker, mkdir) responde sem rede.
cat > "$W/bin/ssh" <<'EOF'
#!/usr/bin/env bash
shift 3   # -o BatchMode=yes <host>
if [ "$1" = bash ]; then exec "$@"; fi
case "$*" in *healthz*) echo 200 ;; esac
exit 0
EOF
printf '#!/usr/bin/env bash\nexit 0\n' > "$W/bin/scp"
# curl falso: registra a linha de comando; com -w devolve FAKE_HTTP (a conferência da ingestão); consome o stdin do -K -.
cat > "$W/bin/curl" <<'EOF'
#!/usr/bin/env bash
echo "$*" >> "$FAKE/curl.argv"
[ -t 0 ] || cat >> "$FAKE/curl.stdin"
case "$*" in *http_code*) printf '%s' "${FAKE_HTTP:-204}" ;; esac
exit 0
EOF
chmod +x "$W/bin/ssh" "$W/bin/scp" "$W/bin/curl"

falhas=0
# caso <nome> <rc esperado> <conteúdo do .env> <http do curl>
caso() {
  local nome="$1" esperado="$2" env="$3" http="$4" rc out
  printf '%s\n' "$env" > "$W/stack/.env"; rm -f "$W/curl.argv" "$W/curl.stdin"
  out=$(env PATH="$W/bin:$PATH" FAKE="$W" FAKE_HTTP="$http" DESTINO="$W/stack" PORTA=5080 \
    bash "$W/s/observabilidade-deploy.sh" subir 2>&1) && rc=0 || rc=$?
  if [ "$rc" != "$esperado" ]; then
    echo "FAIL $nome: exit=$rc, esperado=$esperado"; echo "$out" | sed 's/^/     /' | tail -6; falhas=$((falhas+1)); return
  fi
  if [ -f "$W/curl.argv" ] && grep -q 'SEGREDO' "$W/curl.argv"; then
    echo "FAIL $nome: o segredo apareceu na linha de comando do curl"; falhas=$((falhas+1)); return
  fi
  if grep -q 'SEGREDO' <<<"$out"; then echo "FAIL $nome: o segredo foi impresso"; falhas=$((falhas+1)); return; fi
  echo "ok   $nome: exit $rc"
}

BASE=$'ZO_ROOT_USER_EMAIL=a@b.c\nZO_ROOT_USER_PASSWORD=Xx-1\nOO_INGEST_NOME=coletor-otel'
caso 'já existe, token recusado (401)'  4 "$BASE"$'\nOO_AUTH=SEGREDO' 401
caso 'já existe, token recusado (403)'  4 "$BASE"$'\nOO_AUTH=SEGREDO' 403
caso 'já existe, sem rota (000)'        4 "$BASE"$'\nOO_AUTH=SEGREDO' 000
caso 'já existe, OO_AUTH vazio'         4 "$BASE"$'\nOO_AUTH=' 204
caso 'já existe, token aceito'          0 "$BASE"$'\nOO_AUTH=SEGREDO' 204
[ "$falhas" = 0 ] || exit 1
echo '5 casos aprovados.'
