#!/usr/bin/env bash
# Testes do script remoto do vps-deploy.sh (issue #1223), sem tocar na VPS.
# Extrai o heredoc REMOTE e roda num container Linux com um `docker` falso
# que registra as chamadas e simula health. Uso: bash scripts/deploy/vps-deploy.test.sh
set -euo pipefail
cd "$(dirname "$0")"

sed -n "/^read -r -d '' REMOTE <<'REMOTE_EOF'/,/^REMOTE_EOF$/p" vps-deploy.sh | sed '1d;$d' > .remote.sh

cat > .fake-docker <<'EOF'
#!/usr/bin/env bash
# docker falso: estado em $STATE (tags "img dst=src"), health via FAKE_BAD.
echo "docker $*" >> "$STATE/calls"
case "$1" in
  build) exit "${FAKE_BUILD_FAIL:-0}" ;;
  exec) echo DUMP ;;
  image) grep -q "^$3 " "$STATE/tags" 2>/dev/null ;;
  tag) src=$(grep "^$2 " "$STATE/tags" | cut -d' ' -f2); sed -i "\#^$3 #d" "$STATE/tags"; echo "$3 ${src:-$2}" >> "$STATE/tags" ;;
  compose) : ;;
  logs) : ;;
  inspect)
    atual=$(grep "^easystok-api:vps " "$STATE/tags" | cut -d' ' -f2)
    case "$3" in
      *Health*) if [ "${FAKE_BAD:-0}" = 1 ] && [[ "$atual" != *vps-prev* && "$atual" != *vps-antiga* ]]; then echo unhealthy; else echo healthy; fi ;;
      *Running*) echo true ;;
      *Env*) echo "GIT_SHA=$SHA_FULL" ;;
    esac ;;
esac
EOF

cat > .caso.sh <<'EOF'
#!/usr/bin/env bash
# roda um caso: $1 = nome; env extra ja exportado pelo chamador
set -u
export STATE=$(mktemp -d); : > "$STATE/calls"; export BUILD_ROOT=$(mktemp -d) BACKUP_DIR=$(mktemp -d) STACK_DIR=/tmp
export SHA=novo0001 SHA_FULL=novo0001novo TS=T0 DB_CONTAINER=pg DB_NAME=db HEALTH_TIMEOUT=1 SERVICES="api worker web"
mkdir -p /tmp/bin && cp /t/.fake-docker /tmp/bin/docker && chmod +x /tmp/bin/docker
export PATH=/tmp/bin:$PATH
for s in api worker web; do echo "easystok-$s:vps easystok-$s:vps-antiga" >> "$STATE/tags"; done
tar -cf /tmp/src.tar -C /t .remote.sh
if [ "${SEGURA_TRAVA:-0}" = 1 ]; then exec 8>/tmp/easystok-deploy.lock; flock 8; fi
bash /t/.remote.sh < /tmp/src.tar > /tmp/out 2>&1; rc=$?
echo "rc=$rc"
echo "imagem=$(grep '^easystok-api:vps ' $STATE/tags | cut -d' ' -f2)"
echo "current=$(cat $BUILD_ROOT/easystok-current 2>/dev/null)"
echo "dump=$(ls $BACKUP_DIR | wc -l)"
echo "up=$(grep -c 'compose up' $STATE/calls 2>/dev/null)"
EOF

run() { docker run --rm -e "$1" -v "$(pwd -W):/t" bash:5 sh -c 'apk add -q util-linux >/dev/null 2>&1; bash /t/.caso.sh'; }
falhas=0
confere() { if grep -q -- "$2" <<<"$3"; then echo "ok   $1"; else echo "FAIL $1: esperava '$2'"; echo "$3" | sed 's/^/     /'; falhas=$((falhas+1)); fi; }

# --- trava de SHA (#1337): o lado local roda com --dry-run num repositorio git temporario, sem SSH.
script="$(pwd)/vps-deploy.sh"
tmp=$(mktemp -d)
git init -q --bare "$tmp/origin.git"
git clone -q "$tmp/origin.git" "$tmp/clone" 2>/dev/null
(
  cd "$tmp/clone" && git config user.email t@t && git config user.name t
  git commit -q --allow-empty -m c1 && git commit -q --allow-empty -m c2 && git push -q origin HEAD:master
  git checkout -q -b lateral HEAD~1 && git commit -q --allow-empty -m lado
)
git -C "$tmp/clone" fetch -q origin
mestre=$(git -C "$tmp/clone" rev-parse origin/master)
velho=$(git -C "$tmp/clone" rev-parse origin/master~1)
lado=$(git -C "$tmp/clone" rev-parse lateral)
local_() { (cd "$tmp/clone" && bash "$script" --dry-run "$@" 2>&1; echo "rc=$?"); }

r=$(local_);                 confere "trava: sem SHA publica o master"      "rc=0" "$r"
r=$(local_ "$mestre");       confere "trava: SHA do master segue"           "rc=0" "$r"
r=$(local_ "$velho");        confere "trava: SHA antigo e recusado"         "rc=7" "$r"
                              confere "trava: diz quantos commits atras"     "1 commit(s) atras do origin/master" "$r"
r=$(local_ --rollback "$velho"); confere "trava: --rollback libera o antigo" "rc=0" "$r"
                              confere "trava: --rollback avisa"              "ATENCAO: --rollback" "$r"
r=$(local_ "$lado");         confere "trava: SHA fora do master e recusado" "rc=7" "$r"
                              confere "trava: explica fora do master"        "nao esta no origin/master" "$r"
r=$(local_ --rollback "$lado"); confere "trava: --rollback nao libera fora do master" "rc=7" "$r"
rm -rf "$tmp"

r=$(run X=0);                confere "sucesso: exit 0"                  "rc=0" "$r"
                              confere "sucesso: promove vps-<sha>"       "imagem=easystok-api:vps-novo0001" "$r"
                              confere "sucesso: grava easystok-current"  "current=novo0001" "$r"
                              confere "sucesso: faz o dump"              "dump=1" "$r"
r=$(run FAKE_BAD=1);         confere "falha de health: exit 6"          "rc=6" "$r"
                              confere "falha de health: volta a imagem"  "imagem=easystok-api:vps-antiga" "$r"
                              confere "falha de health: sobe 2 vezes"    "up=2" "$r"
                              confere "falha de health: nao marca current" "current=$" "$r"
r=$(run SEGURA_TRAVA=1);     confere "trava ocupada: exit 3"            "rc=3" "$r"
                              confere "trava ocupada: nao sobe nada"     "up=0" "$r"
r=$(run FAKE_BUILD_FAIL=1);  confere "build falho: exit 4"              "rc=4" "$r"
                              confere "build falho: nao faz dump"        "dump=0" "$r"
                              confere "build falho: imagem intacta"      "imagem=easystok-api:vps-antiga" "$r"

rm -f .remote.sh .fake-docker .caso.sh
[ "$falhas" = 0 ] && echo "TODOS OS CASOS PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
