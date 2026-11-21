#!/usr/bin/env bash
# #2440: o build de produção do console (modo API) exige CONSOLE_SHA (#2393), mas o .git fica
# fora do contexto do Docker (.dockerignore). Sem o ARG repassado ao npm run build, o
# Dockerfile.web do modo API sempre falha. Este teste falha se o ARG sumir ou vier depois do build.
set -euo pipefail
raiz="$(cd "$(dirname "$0")/../.." && pwd)"
df="$raiz/EasyStock.Console/Dockerfile.web"

linha_de() { grep -nE "$1" "$df" | head -1 | cut -d: -f1 || true; }
arg=$(linha_de '^ARG CONSOLE_SHA' || true)
build=$(linha_de '^RUN .*npm run build' || true)

falhas=0
confere() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FALHA $1: esperado '$2', veio '$3'"; falhas=$((falhas + 1)); fi; }

confere "Dockerfile.web declara ARG CONSOLE_SHA" "sim" "$([ -n "$arg" ] && echo sim || echo nao)"
confere "ARG CONSOLE_SHA vem antes do npm run build" "sim" "$([ -n "$arg" ] && [ -n "$build" ] && [ "$arg" -lt "$build" ] && echo sim || echo nao)"
confere "npm run build recebe o CONSOLE_SHA" "sim" "$(grep -qE '^RUN CONSOLE_SHA="\$CONSOLE_SHA" npm run build' "$df" && echo sim || echo nao)"

[ "$falhas" -eq 0 ] || exit 1
