#!/usr/bin/env bash
# Testes de scripts/ci/checar-testes-pulados.py (#2371). Rodar: bash scripts/ci/checar-testes-pulados.test.sh
set -uo pipefail
AQUI="$(cd "$(dirname "$0")" && pwd)"
CHECAR="$AQUI/checar-testes-pulados.py"
PY="$(command -v python3 || command -v python)"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
falhas=0

# trx mínimo: cada argumento é "outcome|mensagem".
trx() {
  local arq="$1"; shift
  {
    echo '<?xml version="1.0" encoding="utf-8"?>'
    echo '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
    local i=0
    for r in "$@"; do
      i=$((i + 1))
      echo "<UnitTestResult testName=\"T.Teste$i\" outcome=\"${r%%|*}\"><Output><StdOut>${r#*|}</StdOut></Output></UnitTestResult>"
    done
    echo '</Results></TestRun>'
  } > "$arq"
}

caso() { # nome, rc esperado, diretório
  local nome="$1" esperado="$2" dir="$3" saida rc
  saida="$("$PY" -I "$CHECAR" "$dir" 2>&1)"; rc=$?
  if [ "$rc" -eq "$esperado" ]; then echo "ok   $nome"; else echo "FALHOU $nome (rc=$rc, esperado $esperado)"; echo "$saida" | sed 's/^/     /'; falhas=$((falhas + 1)); fi
}

mkdir -p "$TMP/verde"; trx "$TMP/verde/a.trx" "Passed|" "Passed|"
caso "tudo passou: verde" 0 "$TMP/verde"

mkdir -p "$TMP/optin"; trx "$TMP/optin/a.trx" "Passed|" \
  "NotExecuted|Defina EASYSTOCK_PERF_PG com a connection string de um Postgres real para rodar a medição." \
  "NotExecuted|Ensaio opt-in requer EASYSTOK_COMANDA_GO_LIVE_FIXTURE explícito." \
  "NotExecuted|Fixture interativa requer EASYSTOK_COMANDA_BROWSER_FIXTURE explícito." \
  "NotExecuted|Fixture opt-in: informe EASYSTOK_R4_BROWSER_FIXTURE." \
  "NotExecuted|Inferência real desligada. Exige opt-in EASYSTOK_TEST_CADASTRO_CLI_REAL=1 e EASYSTOK_TEST_CLAUDE_PATH absoluto."
caso "pulo opt-in por variável: verde" 0 "$TMP/optin"

mkdir -p "$TMP/pg"; trx "$TMP/pg/a.trx" "Passed|" "NotExecuted|Docker/PostgreSQL unavailable"
caso "Postgres indisponível: vermelho" 1 "$TMP/pg"

mkdir -p "$TMP/motivo"; trx "$TMP/motivo/a.trx" "NotExecuted|Connection refused (localhost:5432)"
caso "motivo livre da fixture (ex.Message): vermelho" 1 "$TMP/motivo"

mkdir -p "$TMP/varios"; trx "$TMP/varios/a.trx" "Passed|"; trx "$TMP/varios/b.trx" "NotExecuted|PG indisponível"
caso "pulo em outro assembly: vermelho" 1 "$TMP/varios"

mkdir -p "$TMP/cli-sem-optin"; trx "$TMP/cli-sem-optin/a.trx" "NotExecuted|Caminho EASYSTOK_TEST_CLAUDE_PATH não configurado."
caso "caminho do CLI não é opt-in: vermelho" 1 "$TMP/cli-sem-optin"

mkdir -p "$TMP/optin-e-pg"; trx "$TMP/optin-e-pg/a.trx" \
  "NotExecuted|Fixture opt-in: informe EASYSTOK_R4_BROWSER_FIXTURE." \
  "NotExecuted|Docker/PostgreSQL indisponível."
caso "opt-in não encobre Docker indisponível: vermelho" 1 "$TMP/optin-e-pg"

mkdir -p "$TMP/vazio"
caso "sem trx: vermelho (não prova nada)" 1 "$TMP/vazio"

[ "$falhas" -eq 0 ] && echo "checar-testes-pulados: todos os casos ok" || { echo "checar-testes-pulados: $falhas caso(s) falharam"; exit 1; }
