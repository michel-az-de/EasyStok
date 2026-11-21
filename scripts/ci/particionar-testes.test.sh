#!/usr/bin/env bash
# Testes de scripts/ci/particionar-testes.py (#2976). Rodar: bash scripts/ci/particionar-testes.test.sh
set -uo pipefail
AQUI="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="$AQUI/particionar-testes.py"
PY="$(command -v python3 || command -v python)"
TMP="$(mktemp -d)"; trap 'rm -rf "$TMP"' EXIT
falhas=0

ok() { echo "ok   $1"; }
falhou() { echo "FALHOU $1"; falhas=$((falhas + 1)); }
rodar() { "$PY" -I "$SCRIPT" "$@" > "$TMP/saida.txt" 2>&1; }

# Lista no formato do `dotnet test --list-tests` (inglês do runner).
mkdir -p "$TMP/listas"
cat > "$TMP/listas/Infra.txt" <<'EOF'
Test run for /x/Infra.dll (.NETCoreApp,Version=v10.0)
The following Tests are available:
    Ns.A.Um
    Ns.A.Dois
    Ns.B.Um(valor: 1)
    Ns.B.Um(valor: 2)
    Ns.B.Tres
    Ns.Grande.M1
    Ns.Grande.M2
    Ns.Grande.M3
    Ns.Grande.M4
EOF
# Lista local em português: o cabeçalho muda, o formato não.
cat > "$TMP/listas/Api.txt" <<'EOF'
Os seguintes Testes estão disponíveis:
    Api.C.X
    Api.C.Y
    Api.C.Sub.Z
EOF
cat > "$TMP/tempos.json" <<'EOF'
{"Infra::Ns.Grande.M1": 30, "Infra::Ns.Grande.M2": 30, "Infra::Ns.Grande.M3": 30, "Infra::Ns.Grande.M4": 30}
EOF

if rodar particionar --listas "$TMP/listas" --tempos "$TMP/tempos.json" --partes 3 --saida "$TMP/partes"; then ok "particiona"; else falhou "particiona"; cat "$TMP/saida.txt"; fi

# A classe grande (120 s, acima da meta por parte) vira um filtro por método e se espalha.
grandes=$(cat "$TMP"/partes/parte-*/Infra.filtro 2>/dev/null | tr '|' '\n' | grep -c 'FullyQualifiedName=Ns.Grande.M')
[ "$grandes" = 4 ] && ok "classe grande dividida por método" || falhou "classe grande dividida por método ($grandes)"
partes_com_grande=$(grep -l 'Ns.Grande' "$TMP"/partes/parte-*/Infra.filtro 2>/dev/null | wc -l)
[ "$partes_com_grande" -ge 3 ] && ok "métodos da classe grande em partes diferentes" || falhou "métodos da classe grande em partes diferentes ($partes_com_grande)"

# Api.C. contém Api.C.Sub.Z: o prefixo da classe pegaria outra classe, então vira lista de métodos.
if grep -qE 'FullyQualifiedName~Api\.C\.(\||$)' "$TMP"/partes/parte-*/Api.filtro 2>/dev/null; then falhou "prefixo ambíguo vira lista de métodos"; else ok "prefixo ambíguo vira lista de métodos"; fi
grep -q 'FullyQualifiedName~Api.C.Sub\.' "$TMP"/partes/parte-*/Api.filtro && ok "classe sem ambiguidade usa prefixo" || falhou "classe sem ambiguidade usa prefixo"
[ "$(cat "$TMP/partes/matriz.json")" = '[1, 2, 3]' ] && ok "matriz das partes" || falhou "matriz das partes ($(cat "$TMP/partes/matriz.json"))"

# TRX mínimo: argumentos são os testName do resultado.
trx() {
  local arq="$1"; shift
  {
    echo '<?xml version="1.0" encoding="utf-8"?>'
    echo '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
    for n in "$@"; do echo "<UnitTestResult testName=\"$n\" outcome=\"Passed\" duration=\"00:00:02.5000000\" />"; done
    echo '</Results></TestRun>'
  } > "$arq"
}
INFRA=("Ns.A.Um" "Ns.A.Dois" "Ns.B.Um(valor: 1)" "Ns.B.Um(valor: 2)" "Ns.B.Tres" "Ns.Grande.M1" "Ns.Grande.M2" "Ns.Grande.M3" "Ns.Grande.M4")

mkdir -p "$TMP/verde"
trx "$TMP/verde/Infra-parte-1.trx" "${INFRA[@]:0:5}"; trx "$TMP/verde/Infra-parte-2.trx" "${INFRA[@]:5}"
trx "$TMP/verde/Api-parte-1.trx" "Api.C.X" "Api.C.Y" "Api.C.Sub.Z"
if rodar guardar --listas "$TMP/listas" --trx "$TMP/verde"; then ok "guarda: cada teste uma vez"; else falhou "guarda: cada teste uma vez"; cat "$TMP/saida.txt"; fi

mkdir -p "$TMP/falta"
trx "$TMP/falta/Infra-parte-1.trx" "${INFRA[@]:1}"; cp "$TMP/verde/Api-parte-1.trx" "$TMP/falta/"
if rodar guardar --listas "$TMP/listas" --trx "$TMP/falta"; then falhou "guarda: teste que não rodou"; else grep -q 'Ns.A.Um' "$TMP/saida.txt" && ok "guarda: teste que não rodou" || falhou "guarda: teste que não rodou (sem nome)"; fi

mkdir -p "$TMP/dobro"
cp "$TMP/verde/"*.trx "$TMP/dobro/"; trx "$TMP/dobro/Infra-parte-3.trx" "Ns.A.Dois"
if rodar guardar --listas "$TMP/listas" --trx "$TMP/dobro"; then falhou "guarda: teste em duas partes"; else ok "guarda: teste em duas partes"; fi

mkdir -p "$TMP/semtrx"; cp "$TMP/verde/Infra-parte-"*.trx "$TMP/semtrx/"
if rodar guardar --listas "$TMP/listas" --trx "$TMP/semtrx"; then falhou "guarda: assembly sem trx"; else ok "guarda: assembly sem trx"; fi

if rodar tempos --trx "$TMP/verde" --saida "$TMP/novos.json" \
  && "$PY" -I -c 'import json,sys; t=json.load(open(sys.argv[1])); assert t["Infra::Ns.B.Um"]==5.0, t; assert t["Api::Api.C.X"]==2.5, t' "$TMP/novos.json"; then
  ok "tempos somam os casos do mesmo método"; else falhou "tempos somam os casos do mesmo método"; cat "$TMP/saida.txt"; fi

echo "falhas: $falhas"
[ "$falhas" -eq 0 ]
