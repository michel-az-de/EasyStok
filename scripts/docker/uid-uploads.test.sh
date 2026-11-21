#!/usr/bin/env bash
# #2245: a API e o worker gravam no mesmo volume uploads-data (/app/uploaded-files).
# O volume pertence ao appuser da API; se o worker rodar com outro uid, ele não grava o
# canhoto do aviso. Este teste falha se os uids ou gids dos dois Dockerfiles divergirem.
set -euo pipefail
raiz="$(cd "$(dirname "$0")/../.." && pwd)"
api="$raiz/Dockerfile"
worker="$raiz/EasyStock.Worker/Dockerfile"

uid_de() { grep -oE -- '--uid [0-9]+' "$1" | head -1 | awk '{print $2}'; }
gid_de() { grep -oE -- '--gid [0-9]+' "$1" | head -1 | awk '{print $2}'; }
usuario_final() { grep -E '^USER ' "$1" | tail -1 | awk '{print $2}'; }

falhas=0
confere() { if [ "$2" = "$3" ]; then echo "ok   $1"; else echo "FALHA $1: esperado '$2', veio '$3'"; falhas=$((falhas + 1)); fi; }

confere "uid do worker igual ao da API" "$(uid_de "$api")" "$(uid_de "$worker")"
confere "gid do worker igual ao da API" "$(gid_de "$api")" "$(gid_de "$worker")"
confere "worker roda como appuser" "appuser" "$(usuario_final "$worker")"

[ "$falhas" -eq 0 ] || exit 1
