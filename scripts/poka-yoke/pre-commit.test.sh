#!/bin/sh
# Prova do .husky/pre-commit (issue 2922): verde passa, vermelho bloqueia e aponta o log,
# e ausencia de log (wscript desabilitado) tambem bloqueia. Uso: sh scripts/poka-yoke/pre-commit.test.sh
raiz=$(cd "$(dirname "$0")/../.." && pwd)
t=$(mktemp -d); trap 'rm -rf "$t"' EXIT
mkdir -p "$t/bin" "$t/.build"
falhas=0
roda() { # $1=nome $2=corpo do dotnet falso $3=exit esperado $4=texto esperado na saida
  printf '#!/bin/sh\n%s\n' "$2" > "$t/bin/dotnet"; chmod +x "$t/bin/dotnet"
  out=$(cd "$t" && PATH="$t/bin:$PATH" sh "$raiz/.husky/pre-commit" 2>&1); rc=$?
  if [ "$rc" -eq "$3" ] && { [ -z "$4" ] || printf '%s' "$out" | grep -q "$4"; }; then echo "OK   $1"
  else echo "FALHA $1 (rc=$rc): $out"; falhas=1; fi
}
roda verde 'echo "[gate] verde" > .build/gate-ultimo.log; exit 0' 0 ""
roda vermelho 'echo "[gate] VERMELHO no build" > .build/gate-ultimo.log; exit 1' 1 "gate-ultimo.log"
roda sem-log 'exit 0' 1 "wscript"
exit $falhas
