#!/usr/bin/env bash
# Guarda do publicador real, sem rede, build ou acesso a servidor.
# Uso: bash scripts/deploy/console-deploy.test.sh [script anterior para prova Red]
set -euo pipefail

SCRIPT="${1:-$(cd "$(dirname "$0")" && pwd)/console-deploy.sh}"
BASE_TMP="$(cd /tmp && pwd -P)"
W="$(mktemp -d "$BASE_TMP/easystok-console-deploy-test.XXXXXX")"
[[ "$W" == /* && "$W" == "$BASE_TMP"/easystok-console-deploy-test.* ]] || exit 1
trap '[[ "$W" == /* && "$W" == "$BASE_TMP"/easystok-console-deploy-test.* ]] && rm -rf -- "$W"' EXIT
mkdir "$W/bin"

# Git é a fronteira que escolhe origin/master; nenhuma consulta sai deste processo.
cat > "$W/bin/git" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
[ "${1:-}" != -C ] || shift 2
case "$*" in
  'rev-parse --show-toplevel') printf '%s\n' "$REPO_TESTE" ;;
  'fetch -q origin') ;;
  'rev-parse --verify origin/master^{commit}') printf '%s\n' "$SHA_ESCOLHIDO" ;;
  'log -1 --format=%s '*) printf '%s\n' 'Commit sintético' ;;
  *) printf 'Git inesperado na prova: %s\n' "$*" >&2; exit 90 ;;
esac
EOF

for comando in curl npm ssh scp; do
  cat > "$W/bin/$comando" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "${0##*/}" >> "$CHAMADAS_TESTE"
exit 91
EOF
  chmod +x "$W/bin/$comando"
done
chmod +x "$W/bin/git"

caso() {
  local nome="$1" esperado="$2" codigo="$3" saida rc
  : > "$W/chamadas"
  saida=$(env PATH="$W/bin:$PATH" REPO_TESTE="$W" \
    SHA_ESCOLHIDO=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa \
    CHAMADAS_TESTE="$W/chamadas" CONSOLE_SHA_ESPERADO="$esperado" \
    bash "$SCRIPT" </dev/null 2>&1) && rc=0 || rc=$?
  if [ "$rc" != "$codigo" ] || [ -s "$W/chamadas" ]; then
    printf 'FAIL %s: exit=%s, esperado=%s\n' "$nome" "$rc" "$codigo"
    printf '%s\n' "$saida" | sed -n '1,8p'
    [ ! -s "$W/chamadas" ] || { printf 'Fronteiras chamadas: '; tr '\n' ' ' < "$W/chamadas"; printf '\n'; }
    return 1
  fi
  printf 'ok   %s: exit %s; curl/npm/ssh/scp não executados\n' "$nome" "$codigo"
}

falhas=0
caso 'master diferente do SHA revisado' bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb 7 || falhas=$((falhas+1))
caso 'SHA esperado inválido' nao-e-um-sha 2 || falhas=$((falhas+1))
[ "$falhas" = 0 ] || exit 1
printf '2 casos do publicador aprovados.\n'
