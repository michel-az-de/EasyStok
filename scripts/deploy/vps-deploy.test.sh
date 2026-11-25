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
W_T="$(cd "$W" && (pwd -W 2>/dev/null || pwd))"
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
# #3323: FAKE_SSH_9=1 simula o pull das imagens do CI falhando na VPS (rc 9); o build volta 0.
case "\$*" in *IMAGENS=pull*) [ "\${FAKE_SSH_9:-0}" = 1 ] && exit 9 ;; esac
exit 0
EOF
chmod +x "$W/bin/ssh"
# #3323: curl falso do GHCR (token e manifest). FAKE_GHCR=1: a imagem existe; FAKE_GHCR_APOS=N: passa a existir
# depois de N consultas de manifest.
cat > "$W/bin/curl" <<EOF
#!/usr/bin/env bash
echo "\$*" >> "$W/curl-calls"
case "\$*" in
  *ghcr.io/token*) echo '{"token":"t"}'; exit 0 ;;
  *manifests*)
    n=\$(( \$(cat "$W/curl-n" 2>/dev/null || echo 0) + 1 )); echo \$n > "$W/curl-n"
    [ "\${FAKE_GHCR:-0}" = 1 ] && exit 0
    [ -n "\${FAKE_GHCR_APOS:-}" ] && [ "\$n" -gt "\$FAKE_GHCR_APOS" ] && exit 0
    exit 22 ;;
esac
exit 22
EOF
# gh falso: FAKE_GH_SEQ = estados "<status> <conclusion>" do run do build-images, um por chamada, separados por |
# (vazio = sem run). FAKE_GH_AVANCA=1 avanca o master do origin quando o estado e "completed cancelled".
cat > "$W/bin/gh" <<EOF
#!/usr/bin/env bash
echo "\$*" >> "$W/gh-calls"
[ -n "\${FAKE_GH_SEQ:-}" ] || exit 0
n=\$(( \$(cat "$W/gh-n" 2>/dev/null || echo 0) + 1 )); echo \$n > "$W/gh-n"
IFS='|' read -ra seq <<<"\$FAKE_GH_SEQ"
i=\$(( n - 1 )); [ \$i -lt \${#seq[@]} ] || i=\$(( \${#seq[@]} - 1 ))
estado="\${seq[\$i]}"
if [ "\${FAKE_GH_AVANCA:-0}" = 1 ] && [ "\${estado%% *}" = completed ] && [ "\${estado#* }" = cancelled ]; then
  o="git --git-dir=$W_T/origin.git"
  novo=\$(GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@t GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@t \$o commit-tree "\$(\$o rev-parse 'master^{tree}')" -p "\$(\$o rev-parse master)" -m avanca)
  \$o update-ref refs/heads/master "\$novo"
fi
echo "\$estado"
EOF
chmod +x "$W/bin/curl" "$W/bin/gh"

# local <env...> -- <args...>: roda o script no clone; imprime rc, saida e se o ssh foi chamado
# Git Bash: pwd -W da o caminho Windows e MSYS_NO_PATHCONV impede converter /tmp/... no -e.
DIR_T="$(pwd -W 2>/dev/null || pwd)"; export MSYS_NO_PATHCONV=1
local_run() {
  local envs=() rc
  while [ "$1" != -- ]; do envs+=("$1"); shift; done; shift
  rm -f "$W/ssh-args" "$W/curl-calls" "$W/curl-n" "$W/gh-calls" "$W/gh-n"
  out=$(cd "$W/c" && env PATH="$W/bin:$PATH" ESPERA_PASSO=0 "${envs[@]}" bash "$SCRIPT" "$@" </dev/null 2>&1) && rc=0 || rc=$?
  echo "rc=$rc"; echo "$out"
  if [ -f "$W/curl-calls" ]; then echo "curl=sim"; else echo "curl=nao"; fi
  if [ -f "$W/gh-calls" ]; then echo "gh=sim"; else echo "gh=nao"; fi
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
r=$(local_run X=0 -- --origem outra);    confere "origem invalida: exit 2"             "rc=2" "$r"
# #2958: GitHub fora. origin aponta para lugar inexistente; o espelho da VPS e o origin.git
# (caminho Windows no Git Bash: o git nativo nao entende /tmp com MSYS_NO_PATHCONV)
W_T="$(cd "$W" && (pwd -W 2>/dev/null || pwd))"
g remote set-url origin "$W_T/github-fora.git"
r=$(local_run X=0 -- "$B");              confere "github fora: exit 7"                 "rc=7" "$r"
                                          confere "github fora: aponta o espelho"       "--origem espelho" "$r"
                                          confere "github fora: nao chama ssh"          "ssh=nao" "$r"
r=$(local_run "ESPELHO_URL=$W_T/origin.git" -- --origem espelho "$B")
                                          confere "espelho: exit 0"                     "rc=0" "$r"
                                          confere "espelho: avisa a origem"             "origem: ESPELHO" "$r"
                                          confere "espelho: chama ssh"                  "ssh=sim" "$r"
r=$(local_run "ESPELHO_URL=$W_T/origin.git" -- --origem espelho "$A")
                                          confere "espelho: SHA antigo exit 7"          "rc=7" "$r"
r=$(local_run "ESPELHO_URL=$W_T/nao-ha.git" -- --origem espelho "$B")
                                          confere "espelho inacessivel: exit 7"         "rc=7" "$r"
g remote set-url origin "$W_T/origin.git"
# #3323: imagens do CI. Pull do GHCR quando o sha esta la; senao espera so o build do master; fallback em build local.
r=$(local_run FAKE_GHCR=1 -- "$B");      confere "imagens prontas: exit 0"             "rc=0" "$r"
                                          confere "imagens prontas: avisa o GHCR"       "imagens: GHCR, prontas" "$r"
                                          confere "imagens prontas: remoto em pull"     "IMAGENS=pull" "$r"
                                          confere "imagens prontas: nao consulta o gh"  "gh=nao" "$r"
                                          confere "imagens prontas: mostra o relogio"   "em [0-9]*s (imagens: pull)" "$r"
r=$(local_run FAKE_GHCR=1 -- --local "$B"); confere "--local: build na VPS"            "build na VPS (--local)" "$r"
                                          confere "--local: remoto em build"            "IMAGENS=build" "$r"
                                          confere "--local: nao consulta GHCR nem gh"   "curl=nao" "$r"
                                          confere "--local: nao consulta o gh"          "gh=nao" "$r"
r=$(local_run FAKE_GHCR=1 "ESPELHO_URL=$W_T/origin.git" -- --origem espelho "$B")
                                          confere "espelho: nunca usa GHCR"             "curl=nao" "$r"
                                          confere "espelho: nunca usa gh"               "gh=nao" "$r"
                                          confere "espelho: remoto em build"            "IMAGENS=build" "$r"
r=$(local_run X=0 -- "$C");              confere "sha a frente do master: build"       "nao ha build do master para esperar" "$r"
                                          confere "sha a frente do master: sem gh"      "gh=nao" "$r"
r=$(local_run FAKE_GHCR=1 "ROLLBACK_CONFIRMA=${A:0:8}" -- --rollback "$A" --motivo "teste")
                                          confere "rollback: sem espera pelo CI"        "gh=nao" "$r"
r=$(local_run "FAKE_GH_SEQ=in_progress null|in_progress null|in_progress null" FAKE_GHCR_APOS=3 -- "$B")
                                          confere "espera o build do CI: exit 0"        "rc=0" "$r"
                                          confere "espera o build do CI: esperou"       "esperando o build-images" "$r"
                                          confere "espera o build do CI: depois puxa"   "GHCR pronto para" "$r"
                                          confere "espera o build do CI: remoto em pull" "IMAGENS=pull" "$r"
r=$(local_run "FAKE_GH_SEQ=completed failure" -- "$B")
                                          confere "build do CI falhou: build na VPS"    "terminou 'failure'; build na VPS" "$r"
                                          confere "build do CI falhou: remoto em build" "IMAGENS=build" "$r"
r=$(local_run X=0 -- "$B");              confere "sem run do build-images: build"       "sem run do build-images" "$r"
r=$(local_run "FAKE_GH_SEQ=in_progress null" ESPERA_IMAGENS=2 -- "$B")
                                          confere "teto da espera: build na VPS"        "teto de 2s; build na VPS" "$r"
r=$(local_run "FAKE_GH_SEQ=in_progress null" ESPERA_IMAGENS=0 -- "$B")
                                          confere "ESPERA_IMAGENS=0: nao espera"        "nao ha build do master para esperar" "$r"
r=$(local_run "FAKE_GH_SEQ=completed success" -- "$B")
                                          confere "CI ok sem imagem no GHCR: build"     "nao mostra o sha; build na VPS" "$r"
r=$(local_run FAKE_GHCR=1 FAKE_SSH_9=1 -- "$B")
                                          confere "pull falhou na VPS: refaz, exit 0"   "rc=0" "$r"
                                          confere "pull falhou na VPS: avisa o fallback" "refazendo em build na VPS" "$r"
                                          confere "pull falhou na VPS: 2a chamada em build" "IMAGENS=build" "$r"
r=$(local_run "FAKE_GH_SEQ=completed cancelled" FAKE_GH_AVANCA=1 -- "$B")
                                          confere "master avancou na espera: exit 7"    "rc=7" "$r"
                                          confere "master avancou na espera: explica"   "avancou para" "$r"
                                          confere "master avancou na espera: sem ssh"   "ssh=nao" "$r"
git --git-dir="$W_T/origin.git" update-ref refs/heads/master "$B"
r=$(local_run "FAKE_GH_SEQ=completed cancelled" -- "$B")
                                          confere "cancelado sem avanco: build na VPS"  "build-images cancelado" "$r"
                                          confere "cancelado sem avanco: exit 0"        "rc=0" "$r"
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
  # #3323: pull do GHCR; FAKE_PULL_FAIL=1 simula o registro fora. FAKE_IMG_SHA troca o GIT_SHA da imagem puxada.
  pull) [ "${FAKE_PULL_FAIL:-0}" = 1 ] && exit 1; echo "$3 $3" >> "$STATE/tags" ;;
  # #3117: o psql le o sinal de ponte muda; FAKE_PONTE_MUDA = lojas com incidente aberto, FAKE_PSQL_FAIL=1 = banco nao responde.
  exec) case "$*" in
          *psql*) [ "${FAKE_PSQL_FAIL:-0}" = 1 ] && exit 1; echo "${FAKE_PONTE_MUDA:-0}" ;;
          *) echo DUMP ;;
        esac ;;
  image) if [ "$3" = -f ]; then echo "GIT_SHA=${FAKE_IMG_SHA:-$SHA_FULL}"; else grep -q "^$3 " "$STATE/tags" 2>/dev/null; fi ;;
  tag) src=$(grep "^$2 " "$STATE/tags" | cut -d' ' -f2); sed -i "\#^$3 #d" "$STATE/tags"; echo "$3 ${src:-$2}" >> "$STATE/tags" ;;
  compose) : ;;
  logs) : ;;
  # #2545: faxina pos-deploy. FAKE_FAXINA_FAIL=1 simula docker recusando remover/podar.
  images) grep "^$2:" "$STATE/tags" | cut -d' ' -f1 | cut -d: -f2 ;;
  rmi) [ "${FAKE_FAXINA_FAIL:-0}" = 1 ] && exit 1; sed -i "\#^$2 #d" "$STATE/tags" ;;
  builder) exit "${FAKE_FAXINA_FAIL:-0}" ;;
  inspect)
    # #3117: a ponte so existe quando o caso define FAKE_WPP ("<status> <health>").
    if [ "$4" = wpp ]; then [ -n "${FAKE_WPP:-}" ] || exit 1; echo "$FAKE_WPP"; exit 0; fi
    atual=$(grep "^easystok-api:vps " "$STATE/tags" | cut -d' ' -f2)
    case "$3" in
      # #2372: o Worker tem health proprio (arquivo de vida); FAKE_WORKER_HEALTH=sem simula container sem healthcheck.
      *Health*) if [ "$4" = ez-worker ] && [[ "$atual" != *vps-prev* && "$atual" != *vps-antiga* ]] && [ -n "${FAKE_WORKER_HEALTH:-}" ]; then echo "$FAKE_WORKER_HEALTH";
                elif [ "${FAKE_BAD:-0}" = 1 ] && [[ "$atual" != *vps-prev* && "$atual" != *vps-antiga* ]]; then echo unhealthy; else echo healthy; fi ;;
      *Running*) echo true ;;
      *RestartCount*) if [[ "$atual" != *vps-prev* && "$atual" != *vps-antiga* ]]; then echo "${FAKE_WORKER_RESTARTS:-0}"; else echo 0; fi ;;
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
export MODO=deploy QUEM="Teste <t@t> em t@maquina" MOTIVO= WPP_CONTAINER=wpp
mkdir -p /tmp/bin && cp /t/.fake-docker /tmp/bin/docker && chmod +x /tmp/bin/docker
# E3: gancho do backup falso; grava os argumentos. FAKE_HOOK_RC simula o backup falhando.
cat > /tmp/hook.sh <<'HOOK'
#!/bin/sh
echo "$*" > "$STATE/hook"
exit ${FAKE_HOOK_RC:-0}
HOOK
chmod +x /tmp/hook.sh
export BACKUP_HOOK=${BACKUP_HOOK:-/nao/existe}
export PATH=/tmp/bin:$PATH
for s in api worker web; do
  echo "easystok-$s:vps easystok-$s:vps-antiga" >> "$STATE/tags"
  # #2545: sobras de deploys anteriores (5 imagens de rollback e 2 tags de sha)
  for d in 01 02 03 04 05; do echo "easystok-$s:vps-prev-202611${d}T000000Z img-$s-$d" >> "$STATE/tags"; done
  for h in abc12345 def67890; do echo "easystok-$s:vps-$h img-$s-$h" >> "$STATE/tags"; done
done
tar -cf /tmp/src.tar -C /t .remote.sh
if [ "${SEGURA_TRAVA:-0}" = 1 ]; then exec 8>/tmp/easystok-deploy.lock; flock 8; fi
bash /t/.remote.sh < /tmp/src.tar > /tmp/out 2>&1; rc=$?
echo "rc=$rc"
echo "imagem=$(grep '^easystok-api:vps ' $STATE/tags | cut -d' ' -f2)"
echo "current=$(cat $BUILD_ROOT/easystok-current 2>/dev/null)"
echo "dump=$(ls $BACKUP_DIR | wc -l)"
echo "up=$(grep -c 'compose up' $STATE/calls 2>/dev/null)"
echo "pulls=$(grep -c '^docker pull' $STATE/calls)"
echo "builds=$(grep -c '^docker build ' $STATE/calls)"
echo "fonte=$(ls -d $BUILD_ROOT/easystok-$SHA 2>/dev/null | wc -l)"
echo "ghcr_sobra=$(grep -c '^ghcr.io/' $STATE/tags)"
echo "hist=$(cat $BUILD_ROOT/deploy-history.log 2>/dev/null)"
echo "log_worker=$(grep -c 'logs --tail 40 ez-worker' $STATE/calls 2>/dev/null)"
echo "prev=$(grep -c '^easystok-web:vps-prev-' $STATE/tags)"
echo "prev_velha=$(grep -c '^easystok-web:vps-prev-2026110[123]T' $STATE/tags)"
echo "prev_nova=$(grep -c '^easystok-web:vps-prev-\(T0\|2026110[45]T\)' $STATE/tags)"
echo "shas=$(grep -c '^easystok-web:vps-[0-9a-f]\{8\} ' $STATE/tags)"
echo "poda=$(grep -c 'builder prune' $STATE/calls)"
echo "aviso_ponte=$(grep -c 'AVISO: ponte WPPConnect' /tmp/out)"
echo "aviso_sinal=$(grep -c 'AVISO: sinal de ponte muda nao foi lido' /tmp/out)"
echo "mexeu_ponte=$(grep -cE '^docker (restart|stop|start|kill|rm|pause) ' $STATE/calls)"
sleep 1; echo "hook=$(cat $STATE/hook 2>/dev/null)"
EOF

run() { docker run --rm -e "$1" -v "$DIR_T:/t" bash:5 sh -c 'apk add -q util-linux >/dev/null 2>&1; bash /t/.caso.sh'; }
run2() { docker run --rm -e "$1" -e "$2" -v "$DIR_T:/t" bash:5 sh -c 'apk add -q util-linux >/dev/null 2>&1; bash /t/.caso.sh'; }

r=$(run X=0);                confere "sucesso: exit 0"                  "rc=0" "$r"
                              confere "sucesso: promove vps-<sha>"       "imagem=easystok-api:vps-novo0001" "$r"
                              confere "sucesso: grava easystok-current"  "current=novo0001" "$r"
                              confere "sucesso: faz o dump"              "dump=1" "$r"
                              confere "sucesso: historico com quem e rc" "hist=.*deploy	novo0001	rc=0	Teste <t@t>" "$r"
                              confere "sucesso: guarda so 3 imagens de rollback" "prev=3" "$r"
                              confere "sucesso: as 3 guardadas sao as mais novas" "prev_nova=3" "$r"
                              confere "sucesso: remove as de rollback antigas" "prev_velha=0" "$r"
                              confere "sucesso: remove as tags de sha antigas" "shas=0" "$r"
                              confere "sucesso: poda o cache de build"   "poda=1" "$r"
                              confere "sucesso: builda as 3 imagens na VPS" "builds=3" "$r"
                              confere "sucesso: sem pull no build local" "pulls=0" "$r"
                              confere "sucesso: fonte extraida"          "fonte=1" "$r"
                              confere "sucesso: historico traz imagens e duracao" "imagens=build	dur=[0-9]*s" "$r"
# #3323: IMAGENS=pull usa as imagens do CI e nao toca na fonte nem no docker build.
r=$(run IMAGENS=pull);       confere "pull: exit 0"                     "rc=0" "$r"
                              confere "pull: promove a imagem do GHCR"   "imagem=ghcr.io/michel-az-de/easystok-api:" "$r"
                              confere "pull: 3 pulls e nenhum build"     "pulls=3" "$r"
                              confere "pull: nenhum docker build"        "builds=0" "$r"
                              confere "pull: nao extrai a fonte"         "fonte=0" "$r"
                              confere "pull: tira a tag do GHCR"         "ghcr_sobra=0" "$r"
                              confere "pull: marca current"              "current=novo0001" "$r"
                              confere "pull: historico diz imagens=pull" "imagens=pull	dur=[0-9]*s" "$r"
r=$(run2 IMAGENS=pull FAKE_PULL_FAIL=1); confere "pull falho: exit 9"   "rc=9" "$r"
                              confere "pull falho: nao troca a imagem"   "imagem=easystok-api:vps-antiga" "$r"
                              confere "pull falho: nao faz dump"         "dump=0" "$r"
                              confere "pull falho: nao sobe nada"        "up=0" "$r"
r=$(run2 IMAGENS=pull FAKE_IMG_SHA=outro); confere "GIT_SHA errado: exit 9" "rc=9" "$r"
                              confere "GIT_SHA errado: nao troca a imagem" "imagem=easystok-api:vps-antiga" "$r"
                              confere "GIT_SHA errado: nao sobe nada"    "up=0" "$r"
r=$(run2 IMAGENS=pull FAKE_BAD=1); confere "pull + health falho: exit 6" "rc=6" "$r"
                              confere "pull + health falho: volta a imagem" "imagem=easystok-api:vps-antiga" "$r"
r=$(run RETEM_PREV=5);       confere "RETEM_PREV=5: guarda 5"           "prev=5" "$r"
r=$(run FAKE_FAXINA_FAIL=1); confere "faxina falha: deploy segue exit 0" "rc=0" "$r"
                              confere "faxina falha: marca current"      "current=novo0001" "$r"
r=$(run FAKE_WORKER_RESTARTS=3); confere "worker em laco de restart: exit 6"     "rc=6" "$r"
                              confere "worker em laco: volta a imagem"   "imagem=easystok-api:vps-antiga" "$r"
                              confere "worker em laco: nao marca current" "current=$" "$r"
                              confere "worker em laco: mostra o log do worker" "log_worker=1" "$r"
r=$(run FAKE_WORKER_HEALTH=unhealthy); confere "worker unhealthy: exit 6"     "rc=6" "$r"
                              confere "worker unhealthy: volta a imagem" "imagem=easystok-api:vps-antiga" "$r"
                              confere "worker unhealthy: mostra o log do worker" "log_worker=1" "$r"
r=$(run FAKE_WORKER_HEALTH=starting); confere "worker sem ficar healthy no prazo: exit 6" "rc=6" "$r"
r=$(run FAKE_WORKER_HEALTH=sem); confere "worker sem healthcheck: aceita Running" "rc=0" "$r"
r=$(run FAKE_BAD=1);         confere "falha de health: exit 6"          "rc=6" "$r"
                              confere "falha de health: volta a imagem"  "imagem=easystok-api:vps-antiga" "$r"
                              confere "falha de health: sobe 2 vezes"    "up=2" "$r"
                              confere "falha de health: nao marca current" "current=$" "$r"
                              confere "falha de health: historico rc=6"  "hist=.*novo0001	rc=6" "$r"
                              confere "falha de health: nao remove imagem de rollback" "prev=6" "$r"
                              confere "falha de health: nao remove tag de sha" "shas=2" "$r"
                              confere "falha de health: nao poda o cache" "poda=0" "$r"
# #3117: o deploy VERIFICA a ponte WPPConnect e avisa; nunca reinicia a ponte nem muda o exit code.
r=$(run X=0);                confere "sem ponte na VPS: nao avisa"      "aviso_ponte=0" "$r"
r=$(run "FAKE_WPP=running healthy"); confere "ponte saudavel: nao avisa" "aviso_ponte=0" "$r"
                              confere "ponte saudavel: exit 0"           "rc=0" "$r"
r=$(run "FAKE_WPP=running unhealthy"); confere "ponte unhealthy: avisa" "aviso_ponte=1" "$r"
                              confere "ponte unhealthy: deploy segue exit 0" "rc=0" "$r"
                              confere "ponte unhealthy: marca current"   "current=novo0001" "$r"
                              confere "ponte unhealthy: nao mexe na ponte" "mexeu_ponte=0" "$r"
r=$(run "FAKE_WPP=exited sem"); confere "ponte parada: avisa"           "aviso_ponte=1" "$r"
                              confere "ponte parada: nao mexe na ponte"  "mexeu_ponte=0" "$r"
r=$(run2 "FAKE_WPP=running healthy" FAKE_PONTE_MUDA=2); confere "ponte muda: avisa" "aviso_ponte=1" "$r"
                              confere "ponte muda: deploy segue exit 0"  "rc=0" "$r"
                              confere "ponte muda: nao mexe na ponte"    "mexeu_ponte=0" "$r"
r=$(run2 "FAKE_WPP=running healthy" FAKE_PSQL_FAIL=1); confere "sinal ilegivel: diz que nao leu" "aviso_sinal=1" "$r"
                              confere "sinal ilegivel: deploy segue exit 0" "rc=0" "$r"
r=$(run2 "FAKE_WPP=running unhealthy" FAKE_BAD=1); confere "rollback: tambem verifica a ponte" "aviso_ponte=1" "$r"
                              confere "rollback: exit 6 mantido"         "rc=6" "$r"
r=$(run SEGURA_TRAVA=1);     confere "trava ocupada: exit 3"            "rc=3" "$r"
                              confere "trava ocupada: nao sobe nada"     "up=0" "$r"
                              confere "trava ocupada: historico rc=3"    "hist=.*rc=3" "$r"
r=$(run BACKUP_HOOK=/tmp/hook.sh); confere "backup: entrega o dump ao restic" "hook=pre-deploy .*/db-predeploy-T0.dump" "$r"
                              confere "backup: deploy segue exit 0"     "rc=0" "$r"
r=$(run FAKE_HOOK_RC=1 2>/dev/null; docker run --rm -e BACKUP_HOOK=/tmp/hook.sh -e FAKE_HOOK_RC=1 -v "$DIR_T:/t" bash:5 sh -c 'apk add -q util-linux >/dev/null 2>&1; bash /t/.caso.sh'); confere "backup falha: deploy segue exit 0" "rc=0" "$r"
r=$(run X=0);                confere "sem backup instalado: exit 0"     "rc=0" "$r"
                              confere "sem backup instalado: nao chama" "hook=$" "$r"
r=$(run FAKE_BUILD_FAIL=1);  confere "build falho: exit 4"              "rc=4" "$r"
                              confere "build falho: nao faz dump"        "dump=0" "$r"
                              confere "build falho: imagem intacta"      "imagem=easystok-api:vps-antiga" "$r"

rm -f .remote.sh .fake-docker .caso.sh
[ "$falhas" = 0 ] && echo "TODOS OS CASOS PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
