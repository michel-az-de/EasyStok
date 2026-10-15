#!/usr/bin/env bash
# Requer GNU date (coreutils), como na VPS. Rodado em container: docker run --rm -v "$PWD:/t" -w /t bash:5 sh -c "apk add -q coreutils; bash retencao.test.sh"
# Testa a logica de retencao do enviar.sh com datas simuladas (issue #1253).
# Nao usa rclone nem docker: carrega so as funcoes puras. Uso: bash retencao.test.sh
set -euo pipefail
cd "$(dirname "$0")"
BACKUP_EXTERNO_SO_FUNCOES=1 source ./enviar.sh

falhas=0
confere() { # nome esperado obtido
  if [ "$2" = "$3" ]; then echo "ok   - $1"; else echo "FALHA - $1"; echo "  esperado: [$2]"; echo "  obtido:   [$3]"; falhas=$((falhas+1)); fi
}
dias() { local i; for i in $(seq 0 $(($2 - 1))); do date -u -d "$1 - $i day" +%F; done; }

# 10 diarios terminando em 2026-10-04: mantem 7, apaga os 3 mais velhos.
confere "10 diarios -> apaga 3 mais velhos" \
  "2026-09-27 2026-09-26 2026-09-25" \
  "$(dias 2026-10-04 10 | shuf | excedentes 7 | xargs)"
confere "7 diarios -> nada" "" "$(dias 2026-10-04 7 | excedentes 7 | xargs)"
confere "vazio -> nada" "" "$(printf '' | excedentes 7 | xargs)"
confere "virada de ano ordena certo" "2025-12-28" \
  "$(dias 2026-01-04 8 | excedentes 7 | xargs)"
confere "pasta estranha nunca e apagada" "2026-09-01" \
  "$(printf '%s\n' manual-nao-apagar 2026-09-01 lixo 2026-10-01 2026-10-02 | excedentes 2 | xargs)"

# 6 domingos seguidos: mantem 4 semanais.
semanais="$(for i in 0 1 2 3 4 5; do date -u -d "2026-10-04 - $((i*7)) day" +%F; done)"
confere "6 semanais -> apaga 2" "2026-09-06 2026-08-30" "$(echo "$semanais" | excedentes 4 | xargs)"

# Domingo: so 2026-10-04 em 2026-09-28..2026-10-04.
dom=""; for d in $(dias 2026-10-04 7); do e_domingo "$d" && dom="$dom $d"; done
confere "domingos da semana" " 2026-10-04" "$dom"
for d in $semanais; do e_domingo "$d" || { confere "e_domingo $d" sim nao; }; done

# Simulacao de 60 dias rodando o cron diario: no fim, 7 diarios e 4 semanais.
diario=""; semanal=""
for d in $(dias 2026-11-30 60 | sort); do
  diario="$(printf '%s\n%s' "$diario" "$d" | sed '/^$/d')"
  e_domingo "$d" && semanal="$(printf '%s\n%s' "$semanal" "$d" | sed '/^$/d')"
  for x in $(echo "$diario" | excedentes 7); do diario="$(echo "$diario" | grep -vx "$x")"; done
  for x in $(echo "$semanal" | excedentes 4); do semanal="$(echo "$semanal" | grep -vx "$x")"; done
done
confere "60 dias -> 7 diarios" "2026-11-24 2026-11-25 2026-11-26 2026-11-27 2026-11-28 2026-11-29 2026-11-30" "$(echo "$diario" | xargs)"
confere "60 dias -> 4 semanais (domingos)" "2026-11-08 2026-11-15 2026-11-22 2026-11-29" "$(echo "$semanal" | xargs)"

if [ "$falhas" != 0 ]; then echo "==> $falhas falha(s)"; exit 1; fi
echo "==> retencao: todos os casos passaram"
