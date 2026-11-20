#!/usr/bin/env bash
# Testes do espelho.sh e das units (#2958) num debian descartavel, sem rede e sem a VPS.
# O "GitHub" e um repositorio bare local; o curl da API de issues e falso. Precisa de docker.
# Uso: bash scripts/espelho/espelho.test.sh
set -euo pipefail
export MSYS_NO_PATHCONV=1
cd "$(dirname "$0")"
if [ "${DENTRO:-0}" != 1 ]; then
  dir="$(pwd -W 2>/dev/null || pwd)"
  exec docker run --rm -e DENTRO=1 -v "$dir":/s:ro debian:12-slim bash -c \
    'apt-get update -qq >/dev/null && apt-get install -y -qq git systemd >/dev/null 2>&1; bash /s/espelho.test.sh'
fi
falhas=0
confere() { if grep -q -- "$2" <<<"$3"; then echo "ok   $1"; else echo "FAIL $1: esperava '$2'"; echo "$3" | sed 's/^/     /'; falhas=$((falhas+1)); fi; }

mkdir -p /tmp/u && install -m 644 /s/systemd/* /tmp/u/
u="$(systemd-analyze verify /tmp/u/easystok-espelho.service /tmp/u/easystok-espelho.timer 2>&1 | grep -v 'git/espelho.sh' || true)"
confere "units sem erro de sintaxe" "^$" "${u:-}"
confere "timer a cada 15 min" "OnUnitActiveSec=15min" "$(cat /tmp/u/easystok-espelho.timer)"

T=/tmp/t; mkdir -p $T/bin
git config --global user.name t; git config --global user.email t@t; git config --global init.defaultBranch master
git init -q --bare $T/github.git
git clone -q $T/github.git $T/dev 2>/dev/null
BR=master
publica() { git -C $T/dev push -q origin "$@"; }
git -C $T/dev commit -q --allow-empty -m A; publica "$BR"; publica "$BR:velha"
A="$(git -C $T/dev rev-parse HEAD)"
git -C $T/github.git update-ref refs/pull/1/head "$A"   # o GitHub expoe 1289 refs de PR
export ORIGEM_URL=$T/github.git ESPELHO_GIT=$T/git/EasyStok.git ESTADO=$T/git/estado-espelho META_DIR=$T/git/meta META_HORAS=0
roda() { set +e; out="$(bash /s/espelho.sh 2>&1)"; echo "rc=$?"; echo "$out"; set -e; }

r="$(roda)";                         confere "primeira vez: exit 0"            "rc=0" "$r"
                                     confere "primeira vez: espelho bare"      "true" "$(git -C $ESPELHO_GIT rev-parse --is-bare-repository)"
                                     confere "primeira vez: estado com master" "master=$A" "$(cat $ESTADO)"
                                     confere "sem refs de PR no espelho"       "^0$" "$(git -C $ESPELHO_GIT for-each-ref refs/pull | wc -l)"
git -C $T/dev commit -q --allow-empty -m B; publica "$BR"; publica --delete velha
B="$(git -C $T/dev rev-parse HEAD)"
r="$(roda)";                         confere "push novo: master atualizado"    "master=$B" "$(cat $ESTADO)"
                                     confere "branch apagada: some do espelho" "^0$" "$(git -C $ESPELHO_GIT branch --list velha | wc -l)"
mv $T/github.git $T/github.fora; r="$(roda)"; mv $T/github.fora $T/github.git
                                     confere "github fora: exit 4"             "rc=4" "$r"
                                     confere "github fora: estado rc=4"        "rc=4" "$(cat $ESTADO)"
                                     confere "github fora: espelho intacto"    "$B" "$(git -C $ESPELHO_GIT rev-parse master)"
exec 8>$T/git/.espelho.lock; flock -n 8
r="$(roda)";                         confere "trava ocupada: exit 3"           "rc=3" "$r"
exec 8>&-
r="$(roda)";                         confere "trava livre: exit 0"             "rc=0" "$r"

# issues e PRs: curl falso devolve 2 paginas com itens e a terceira vazia
cat > $T/bin/curl <<'C'
#!/bin/sh
for a; do case "$a" in *page=*) p="${a##*page=}";; esac; done
out=""; while [ $# -gt 0 ]; do [ "$1" = -o ] && out="$2"; shift; done
if [ "$p" -le 2 ]; then echo "[{\"number\": $p}]" > "$out"; else echo "[]" > "$out"; fi
C
chmod +x $T/bin/curl
r="$(PATH=$T/bin:$PATH META_HORAS=20 roda)"
                                     confere "meta: exit 0"                    "rc=0" "$r"
                                     confere "meta: 2 paginas"                 "^2$" "$(ls $META_DIR | wc -l)"
touch $T/marca; r="$(PATH=$T/bin:$PATH META_HORAS=20 roda)"
                                     confere "meta: nao repete no mesmo dia"   "^0$" "$(find $META_DIR -newer $T/marca | wc -l)"
printf '#!/bin/sh\nexit 22\n' > $T/bin/curl
rm -rf $META_DIR; r="$(PATH=$T/bin:$PATH META_HORAS=20 roda)"
                                     confere "meta falha: espelho segue ok"    "rc=0" "$r"
                                     confere "meta falha: avisa"               "aviso: exportacao" "$r"
printf '#!/bin/sh
echo chamado >> %s/curl.calls
exit 22
' $T > $T/bin/curl
r="$(PATH=$T/bin:$PATH META_HORAS=20 roda)"
                                     confere "meta falha: so tenta 1 vez por dia" "^0$" "$(cat $T/curl.calls 2>/dev/null | wc -l)"

[ "$falhas" = 0 ] && echo "TODOS OS CASOS PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
