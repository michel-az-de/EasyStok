#!/usr/bin/env bash
# Testes do vps-deploy.sh (issues #1223 e #1336), sem tocar na VPS.
# Parte 1 (guarda local, #1336): roda o script num repositorio git temporario
#   com um `ssh` falso, para conferir a recusa de SHA antigo e o --rollback.
# Parte 2 (script remoto, #1223): extrai o heredoc REMOTE e roda num container
#   Linux com um `docker` falso que registra as chamadas e simula health.
# Uso: bash scripts/deploy/vps-deploy.test.sh
set -euo pipefail
cd "$(dirname "$0")"
SCRIPT="$(pwd)/vps-deploy.sh"

falhas=0
confere() { if grep -q -- "$2" <<<"$3"; then echo "ok   $1"; else echo "FAIL $1: esperava '$2'"; echo "$3" | sed 's/^/     /'; falhas=$((falhas+1)); fi; }

# ---------------------------------------------------------------- parte 1
# origin com A <- B (master); clone local com C descendente nao publicado.
W="$(mktemp -d)"
git init -q --bare "$W/origin.git"
git clone -q "$W/origin.git" "$W/c" 2>/dev/null
g() { (cd "$W/c" && git -c user.name=t -c user.email=t@t "$@"); }
g checkout -q -b master
g commit -q --allow-empty -m A; A=$(g rev-parse HEAD)
g commit -q --allow-empty -m B; B=$(g rev-parse HEAD)
g push -q origin master
g commit -q --allow-empty -m C; C=$(g rev-parse HEAD)
mkdir -p "$W/bin"
cat > "$W/bin/ssh" <<EOF
#!/usr/bin/env bash
# ssh falso: consome o tar e guarda os argumentos sem o corpo do script remoto
cat >/dev/null
printf '%s\n' "\$@" | sed 's/ bash -c .*//' > "$W/ssh-args"
EOF
chmod +x "$W/bin/ssh"

# local <env...> -- <args...>: roda o script no clone; imprime rc, saida e se o ssh foi chamado
local_run() {
  local envs=() rc
  while [ "$1" != -- ]; do envs+=("$1"); shift; done; shift
  rm -f "$W/ssh-args"
  out=$(cd "$W/c" && env PATH="$W/bin:$PATH" "${envs[@]}" bash "$SCRIPT" "$@" </dev/null 2>&1) && rc=0 || rc=$?
  echo "rc=$rc"; echo "$out"
  if [ -f "$W/ssh-args" ]; then echo "ssh=sim"; cat "$W/ssh-args"; else echo "ssh=nao"; fi
}

r=$(local_run X=0 -- --dry-run);         confere "dry-run origin/master: exit 0"       "rc=0" "$r"
                                          confere "dry-run origin/master: guarda OK"    "guarda: OK" "$r"
r=$(local_run X=0 -- "$A");              confere "SHA antigo: exit 7"                  "rc=7" "$r"
                                          confere "SHA antigo: diz que e anterior"      "RECUSADO" "$r"
                                          confere "SHA antigo: aponta o --rollback"     "--rollback <sha>" "$r"
                                          confere "SHA antigo: nao chama ssh"           "ssh=nao" "$r"
r=$(local_run X=0 -- --dry-run "$A");    confere "dry-run SHA antigo: exit 7"          "rc=7" "$r"
r=$(local_run X=0 -- "$B");              confere "origin/master: exit 0"               "rc=0" "$r"
                                          confere "origin/master: chama ssh"            "ssh=sim" "$r"
                                          confere "origin/master: modo deploy"          "MODO=deploy" "$r"
                                          confere "origin/master: registra quem"        "QUEM=" "$r"
r=$(local_run X=0 -- "$C");              confere "descendente: exit 0"                 "rc=0" "$r"
r=$(local_run X=0 -- --rollback "$A");   confere "rollback sem motivo: exit 8"         "rc=8" "$r"
                                          confere "rollback sem motivo: nao chama ssh"  "ssh=nao" "$r"
r=$(local_run X=0 -- --rollback "$A" --motivo "API quebrou no boot")
                                          confere "rollback sem confirmacao: exit 8"    "rc=8" "$r"
                                          confere "rollback sem confirmacao: sem ssh"   "ssh=nao" "$r"
r=$(local_run ROLLBACK_CONFIRMA=errado -- --rollback "$A" --motivo "API quebrou no boot")
                                          confere "rollback confirmacao errada: exit 8" "rc=8" "$r"
r=$(local_run "ROLLBACK_CONFIRMA=${A:0:8}" -- --rollback "$A" --motivo "API quebrou no boot")
                                          confere "rollback confirmado: exit 0"         "rc=0" "$r"
                                          confere "rollback confirmado: modo rollback"  "MODO=rollback" "$r"
                                          confere "rollback confirmado: leva o motivo"  "MOTIVO=API" "$r"
r=$(local_run X=0 -- --dry-run --rollback "$A" --motivo "API quebrou no boot")
                                          confere "dry-run rollback: exit 0 sem pedir"  "rc=0" "$r"
r=$(local_run "ROLLBACK_CONFIRMA=${C:0:8}" -- --rollback "$C" --motivo "fora do master")
                                          confere "rollback fora do master: exit 7"     "rc=7" "$r"
r=$(local_run X=0 -- --nao-existe);      confere "opcao desconhecida: exit 2"          "rc=2" "$r"
# copia do script diferente da do origin/master: recusa o deploy de verdade
mkdir -p "$W/c/scripts/deploy"; echo "# outra versao" > "$W/c/scripts/deploy/vps-deploy.sh"
g add scripts/deploy/vps-deploy.sh; g commit -q -m D; g push -q origin HEAD:master; D=$(g rev-parse HEAD)
r=$(local_run X=0 -- "$D");              confere "script desatualizado: exit 7"        "rc=7" "$r"
                                          confere "script desatualizado: explica"       "desatualizad" "$r"
rm -rf "$W"

# ---------------------------------------------------------------- parte 2
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
export MODO=deploy QUEM="Teste <t@t> em t@maquina" MOTIVO=
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
echo "hist=$(cat $BUILD_ROOT/deploy-history.log 2>/dev/null)"
EOF

run() { docker run --rm -e "$1" -v "$(pwd -W):/t" bash:5 sh -c 'apk add -q util-linux >/dev/null 2>&1; bash /t/.caso.sh'; }

r=$(run X=0);                confere "sucesso: exit 0"                  "rc=0" "$r"
                              confere "sucesso: promove vps-<sha>"       "imagem=easystok-api:vps-novo0001" "$r"
                              confere "sucesso: grava easystok-current"  "current=novo0001" "$r"
                              confere "sucesso: faz o dump"              "dump=1" "$r"
                              confere "sucesso: historico com quem e rc" "hist=.*deploy	novo0001	rc=0	Teste <t@t>" "$r"
r=$(run FAKE_BAD=1);         confere "falha de health: exit 6"          "rc=6" "$r"
                              confere "falha de health: volta a imagem"  "imagem=easystok-api:vps-antiga" "$r"
                              confere "falha de health: sobe 2 vezes"    "up=2" "$r"
                              confere "falha de health: nao marca current" "current=$" "$r"
                              confere "falha de health: historico rc=6"  "hist=.*novo0001	rc=6" "$r"
r=$(run SEGURA_TRAVA=1);     confere "trava ocupada: exit 3"            "rc=3" "$r"
                              confere "trava ocupada: nao sobe nada"     "up=0" "$r"
                              confere "trava ocupada: historico rc=3"    "hist=.*rc=3" "$r"
r=$(run FAKE_BUILD_FAIL=1);  confere "build falho: exit 4"              "rc=4" "$r"
                              confere "build falho: nao faz dump"        "dump=0" "$r"
                              confere "build falho: imagem intacta"      "imagem=easystok-api:vps-antiga" "$r"

rm -f .remote.sh .fake-docker .caso.sh
[ "$falhas" = 0 ] && echo "TODOS OS CASOS PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
