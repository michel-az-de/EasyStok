#!/usr/bin/env bash
# =============================================================================
# vps-deploy.sh - deploy canonico do EasyStok na VPS (host SSH "hostinger",
# stack /opt/stacks/easystok). Issue #1223.
#
# Roda da maquina de dev (Git Bash). Nao precisa de checkout na VPS. Passos (no
# servidor, sob trava):
#   1. flock /tmp/easystok-deploy.lock -> recusa um segundo deploy simultaneo
#   2. imagens (#3323). Padrao: puxa do GHCR easystok-<svc>:<sha completo>, que o
#      build-images.yml publica no push do master, confere o GIT_SHA de cada uma e
#      da a tag easystok-<svc>:vps-<sha>; nao sobe fonte. Se o build do CI ainda
#      corre, espera (ESPERA_IMAGENS). Fallback: envia o `git archive` do SHA,
#      extrai em ~/build/easystok-<sha> e roda docker build api/worker/web com
#      GIT_SHA. Cai no fallback com --local, com --origem espelho, para SHA que nao
#      e o master atual, sem run do build-images, build do CI falho ou cancelado,
#      espera esgotada ou pull com falha (rc 9 interno: o script refaz em build).
#   4. pg_dump -Fc do banco            -> a migration roda no startup da API
#   5. guarda :vps atual como vps-prev-<ts> e promove vps-<sha> -> vps
#   6. compose up -d, espera healthy e confere GIT_SHA no ez-api
#   7. se falhar: volta as imagens para vps-prev-<ts> e sobe de novo.
#      O banco NAO e revertido: o dump do passo 4 fica para restauracao manual.
#   8. se ficou no ar: faxina (issue #2545). Guarda so as RETEM_PREV imagens
#      vps-prev-<ts> mais novas por servico, remove as tags vps-<sha> de deploys
#      anteriores e poda o cache de build acima de CACHE_TETO. Falha na faxina
#      nao derruba o deploy. Dumps, volumes e banco ficam fora.
#
# Guarda (issue #1336, incidente 2026-10-01): antes do git archive faz git fetch
# e RECUSA o SHA que nao for o origin/master atual nem descendente dele, porque
# publicar um commit antigo desfaz em producao tudo o que entrou depois. Voltar
# de proposito a uma versao anterior so com --rollback, que exige motivo e
# confirmacao. Tambem recusa rodar a partir de uma copia deste script diferente
# da do origin/master (worktree antigo). Cada execucao grava uma linha em
# $BUILD_ROOT/deploy-history.log na VPS: data, modo, sha, rc, quem, motivo.
#
# Uso:
#   scripts/deploy/vps-deploy.sh                 # origin/master
#   scripts/deploy/vps-deploy.sh <sha>           # origin/master ou descendente dele
#   scripts/deploy/vps-deploy.sh --rollback <sha> --motivo "<por que>"
#                                                # commit anterior do master; pede
#                                                # para digitar o sha (ou ROLLBACK_CONFIRMA=<sha8>)
#   scripts/deploy/vps-deploy.sh --dry-run [...] # guarda + plano + sintaxe, sem SSH
#   scripts/deploy/vps-deploy.sh --origem espelho [...] # GitHub fora (#2958): a guarda
#                                                # usa o master do espelho da VPS
#   scripts/deploy/vps-deploy.sh --local [...]   # builda na VPS, sem usar as imagens do CI
#
# Saidas: 0 ok | 2 uso invalido | 3 outro deploy em andamento | 4 build falhou
# | 5 dump vazio | 6 nao ficou healthy (imagem revertida) | 7 SHA recusado pela
# guarda | 8 rollback sem motivo ou sem confirmacao | 9 so interno: as imagens do CI
# nao serviram e o script refaz em build local.
#
# Variaveis: VPS_HOST (hostinger), STACK_DIR (/opt/stacks/easystok),
# BUILD_ROOT (/home/felipe/build), BACKUP_DIR (/home/felipe/backups; /opt/backups e do root), DB_CONTAINER
# (shared-postgres), DB_NAME (easystock), HEALTH_TIMEOUT (180 s), RETEM_PREV (3
# imagens de rollback por servico), CACHE_TETO (10GB de cache de build),
# WPP_CONTAINER (easystok-wppconnect-1, a ponte que o deploy so verifica, #3117),
# ESPELHO_URL ($VPS_HOST:git/EasyStok.git, espelho bare da #2958),
# REGISTRO (ghcr.io/michel-az-de, dono das imagens easystok-<svc>), ESPERA_IMAGENS
# (600 s de teto para esperar o build-images; 0 nao espera) e ESPERA_PASSO (10 s).
# =============================================================================
set -euo pipefail

VPS_HOST="${VPS_HOST:-hostinger}"
STACK_DIR="${STACK_DIR:-/opt/stacks/easystok}"
BUILD_ROOT="${BUILD_ROOT:-/home/felipe/build}"
BACKUP_DIR="${BACKUP_DIR:-/home/felipe/backups}"
# E3 (#2977, ADR-0068): o dump pre-deploy tambem vai para o restic, em segundo plano.
BACKUP_HOOK="${BACKUP_HOOK:-/home/felipe/backup-restic/backup.sh}"
DB_CONTAINER="${DB_CONTAINER:-shared-postgres}"
WPP_CONTAINER="${WPP_CONTAINER:-easystok-wppconnect-1}"
DB_NAME="${DB_NAME:-easystock}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-180}"
RETEM_PREV="${RETEM_PREV:-3}"
CACHE_TETO="${CACHE_TETO:-10GB}"
REGISTRO="${REGISTRO:-ghcr.io/michel-az-de}"
ESPERA_IMAGENS="${ESPERA_IMAGENS:-600}"
ESPERA_PASSO="${ESPERA_PASSO:-10}"
SERVICES="api worker web"

uso() { echo "ERRO: $*" >&2; sed -n '/^# Uso:/,/^# Saidas:/p' "$0" | sed '$d; s/^# \{0,1\}//' >&2; exit 2; }

ESPELHO_URL="${ESPELHO_URL:-$VPS_HOST:git/EasyStok.git}"
DRY_RUN=0 ROLLBACK=0 MOTIVO="" REF="" ORIGEM=github LOCAL=0
while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=1 ;;
    --local) LOCAL=1 ;;
    --rollback) [ -n "${2:-}" ] || uso "--rollback exige um sha"; ROLLBACK=1; REF="$2"; shift ;;
    --motivo) [ -n "${2:-}" ] || uso "--motivo exige um texto"; MOTIVO="$2"; shift ;;
    --origem) [ "${2:-}" = espelho ] || uso "--origem so aceita 'espelho'"; ORIGEM=espelho; shift ;;
    -*) uso "opcao desconhecida: $1" ;;
    *) [ -z "$REF" ] || uso "mais de um sha informado"; REF="$1" ;;
  esac
  shift
done
# Ref do master que a guarda usa: o do GitHub ou, com --origem espelho, o do espelho da VPS.
MASTER_REF=origin/master
[ "$ORIGEM" = github ] || MASTER_REF=espelho/master
REF="${REF:-$MASTER_REF}"
[ "$ROLLBACK" = 1 ] || [ -z "$MOTIVO" ] || uso "--motivo so vale com --rollback"

# Script executado na VPS. Heredoc literal: nada aqui e expandido localmente.
read -r -d '' REMOTE <<'REMOTE_EOF' || true
set -euo pipefail
registra() {
  local rc=$?
  # #3323: duas colunas novas no fim (imagens do deploy e segundos na VPS); as seis antigas ficam onde estavam.
  printf '%s\t%s\t%s\trc=%s\t%s\t%s\timagens=%s\tdur=%ss\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$MODO" "$SHA" "$rc" "$QUEM" "$MOTIVO" \
    "${IMAGENS:-build}" "$SECONDS" >>"$BUILD_ROOT/deploy-history.log" 2>/dev/null || true
}
trap registra EXIT
exec 9>/tmp/easystok-deploy.lock
flock -n 9 || { echo "ERRO: outro deploy em andamento (trava /tmp/easystok-deploy.lock)" >&2; exit 3; }

IMAGENS="${IMAGENS:-build}"
REGISTRO="${REGISTRO:-ghcr.io/michel-az-de}"

# #3323: puxa do GHCR a imagem do sha que o build-images publicou, confere o GIT_SHA dela e da a tag
# vps-<sha>. Falha ou GIT_SHA diferente devolve 1 e o script sai com 9: nada foi trocado e o script
# local refaz o deploy em build local (so ai a fonte sobe).
puxa_imagens() {
  local s ref sha_img p pids="" ok=1
  for s in $SERVICES; do
    docker pull -q "$REGISTRO/easystok-$s:$SHA_FULL" >/dev/null &
    pids="$pids $!"
  done
  for p in $pids; do wait "$p" || ok=0; done
  [ "$ok" = 1 ] || { echo "AVISO: pull de imagem do CI falhou" >&2; return 1; }
  for s in $SERVICES; do
    ref="$REGISTRO/easystok-$s:$SHA_FULL"
    sha_img="$(docker image inspect -f '{{range .Config.Env}}{{println .}}{{end}}' "$ref" | sed -n 's/^GIT_SHA=//p' | head -1)"
    if [ "$sha_img" != "$SHA_FULL" ]; then
      echo "AVISO: $ref traz GIT_SHA='$sha_img', esperado $SHA_FULL" >&2
      return 1
    fi
  done
  for s in $SERVICES; do
    ref="$REGISTRO/easystok-$s:$SHA_FULL"
    docker tag "$ref" "easystok-$s:vps-$SHA"
    docker rmi "$ref" >/dev/null 2>&1 || true
  done
}

SRC="$BUILD_ROOT/easystok-$SHA"
if [ "$IMAGENS" = pull ]; then
  echo "==> [1/6] imagens do CI: $REGISTRO/easystok-<svc>:$SHA"
  puxa_imagens || exit 9
else
  echo "==> [1/6] fonte em $SRC"
  rm -rf "$SRC"
  mkdir -p "$SRC"
  tar -x -C "$SRC"

  dockerfile() {
    case "$1" in
      api) echo Dockerfile ;;
      worker) echo EasyStock.Worker/Dockerfile ;;
      web) echo Dockerfile.Web ;;
    esac
  }

  echo "==> [2/6] build: $SERVICES"
  for s in $SERVICES; do
    log="$BUILD_ROOT/build-$SHA-$s.log"
    docker build --build-arg GIT_SHA="$SHA_FULL" -f "$SRC/$(dockerfile "$s")" \
      -t "easystok-$s:vps-$SHA" "$SRC" >"$log" 2>&1 \
      || { echo "ERRO: build de $s falhou, ver $log" >&2; exit 4; }
  done
fi

echo "==> [3/6] pg_dump de $DB_NAME"
mkdir -p "$BACKUP_DIR"
DUMP="$BACKUP_DIR/$DB_NAME-predeploy-$TS.dump"
docker exec "$DB_CONTAINER" pg_dump -U postgres -Fc "$DB_NAME" >"$DUMP"
[ -s "$DUMP" ] || { echo "ERRO: dump vazio; nada foi trocado nos containers" >&2; exit 5; }
# Envio ao Drive em segundo plano (setsid): falha ou demora do backup nao segura o deploy;
# o vigia da estacao acusa a falta do snapshot.
if [ -x "${BACKUP_HOOK:-}" ]; then
  setsid "$BACKUP_HOOK" pre-deploy "$DUMP" </dev/null >>"$BUILD_ROOT/backup-predeploy.log" 2>&1 &
  echo "    dump enviado ao backup em segundo plano"
fi

echo "==> [4/6] guarda :vps como vps-prev-$TS e promove vps-$SHA"
for s in $SERVICES; do
  if docker image inspect "easystok-$s:vps" >/dev/null 2>&1; then
    docker tag "easystok-$s:vps" "easystok-$s:vps-prev-$TS"
  fi
  docker tag "easystok-$s:vps-$SHA" "easystok-$s:vps"
done

up() { (cd "$STACK_DIR" && docker compose up -d --force-recreate $SERVICES); }

healthy() {
  local fim=$(( $(date +%s) + HEALTH_TIMEOUT )) ok c reinicios
  while [ "$(date +%s)" -lt "$fim" ]; do
    ok=1
    for c in ez-api ez-web; do
      [ "$(docker inspect -f '{{.State.Health.Status}}' "$c" 2>/dev/null)" = healthy ] || ok=0
    done
    [ "$(docker inspect -f '{{.State.Running}}' ez-worker 2>/dev/null)" = true ] || ok=0
    # #2372: com healthcheck (arquivo de vida, #2107) o Worker precisa ficar healthy; Running nao prova loop vivo.
    # Sem healthcheck no compose, vale so o Running acima.
    case "$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}sem{{end}}' ez-worker 2>/dev/null)" in
      healthy|sem) ;;
      *) ok=0 ;;
    esac
    # I4.1 (#1674): Worker em laco de restart fica "Running" entre uma queda e outra. O container acabou
    # de ser recriado, entao qualquer restart ja e falha; nao adianta esperar o resto do prazo.
    reinicios=$(docker inspect -f '{{.RestartCount}}' ez-worker 2>/dev/null || echo 0)
    if [ "${reinicios:-0}" -gt 0 ] 2>/dev/null; then
      echo "ERRO: ez-worker reiniciou $reinicios vez(es) depois do deploy (laco de restart)." >&2
      return 1
    fi
    [ "$ok" = 1 ] && return 0
    sleep 5
  done
  return 1
}

sha_no_ar() {
  docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' ez-api | sed -n 's/^GIT_SHA=//p' | head -1
}

# #2545: sem isto o disco enche (80 GB de cache e 30 imagens de rollback por
# servico em 14/11/2026). So roda depois do deploy saudavel: o caminho de falha
# precisa das imagens antigas. Remover a tag nao apaga camada ainda em uso.
faxina() {
  local s t
  for s in $SERVICES; do
    docker images "easystok-$s" --format '{{.Tag}}' | grep '^vps-prev-' | sort -r       | tail -n +"$(( ${RETEM_PREV:-3} + 1 ))"       | while read -r t; do docker rmi "easystok-$s:$t" >/dev/null || true; done
    docker images "easystok-$s" --format '{{.Tag}}' | grep -E '^vps-[0-9a-f]{7,40}$' | grep -vx "vps-$SHA"       | while read -r t; do docker rmi "easystok-$s:$t" >/dev/null || true; done
    # #3323: sobra de pull interrompido; o caminho normal ja tira a tag do GHCR depois do retag.
    docker images "$REGISTRO/easystok-$s" --format '{{.Tag}}' | while read -r t; do docker rmi "$REGISTRO/easystok-$s:$t" >/dev/null || true; done
  done
  docker builder prune -af --reserved-space "${CACHE_TETO:-10GB}" >/dev/null
}

# #3117: em 21/11/2026 a ponte WPPConnect ficou conectada sem entregar mensagem por cinco horas. O deploy so
# VERIFICA e avisa: nao reinicia a ponte (reiniciar sessao de WhatsApp arrisca QR e bloqueio do numero) e nao
# muda o codigo de saida (cada leitura tem teto de 10 s). Le o estado do container e o sinal do vigia (endpoint_health_state, wppconnect/<loja>
# com incidente aberto). Sem a ponte na VPS, sai calado. O que fazer: docs/dev/incidentes/2026-11-21-ponte-wppconnect-muda.md
verifica_ponte() {
  local c="${WPP_CONTAINER:-easystok-wppconnect-1}" estado mudas
  estado=$(timeout 10 docker inspect -f '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}sem{{end}}' "$c" 2>/dev/null) || return 0
  case "$estado" in
    "running healthy"|"running sem") ;;
    *) echo "AVISO: ponte WPPConnect ($c) esta '$estado'. O deploy NAO reinicia a ponte; veja docs/dev/incidentes/2026-11-21-ponte-wppconnect-muda.md" >&2 ;;
  esac
  mudas=$(timeout 10 docker exec "$DB_CONTAINER" psql -U postgres -d "$DB_NAME" -Atc \
    "select count(*) from endpoint_health_state where endpoint_name like 'wppconnect/%' and last_alerted_at is not null" 2>/dev/null) || mudas=
  case "$mudas" in
    0) ;;
    ''|*[!0-9]*) echo "AVISO: sinal de ponte muda nao foi lido (a consulta ao banco falhou); confira a ponte pelo runbook." >&2 ;;
    *) echo "AVISO: ponte WPPConnect muda em $mudas loja(s): conectada e sem entregar mensagem ao webhook. O deploy NAO reinicia a ponte; veja docs/dev/incidentes/2026-11-21-ponte-wppconnect-muda.md" >&2 ;;
  esac
  return 0
}

echo "==> [5/6] compose up"
up
if healthy && [ "$(sha_no_ar)" = "$SHA_FULL" ]; then
  echo "$SHA" >"$BUILD_ROOT/easystok-current"
  echo "==> [6/6] OK: $SHA no ar. Dump pre-deploy: $DUMP"
  faxina || echo "AVISO: faxina de imagens e cache falhou; o deploy esta no ar" >&2
  verifica_ponte || true
  exit 0
fi

echo "ERRO: $SHA nao passou na verificacao (api e web healthy, worker healthy ou rodando sem healthcheck, sem reiniciar) em ${HEALTH_TIMEOUT}s; voltando para vps-prev-$TS" >&2
docker logs --tail 40 ez-api >&2 || true
# I4.1 (#1674): a falha pode ser so do Worker; sem o log dele o motivo fica na VPS.
docker logs --tail 40 ez-worker >&2 || true
for s in $SERVICES; do
  if docker image inspect "easystok-$s:vps-prev-$TS" >/dev/null 2>&1; then
    docker tag "easystok-$s:vps-prev-$TS" "easystok-$s:vps"
  fi
done
up
if healthy; then
  echo "ROLLBACK de imagem OK. O banco NAO foi revertido; se a migration quebrou, restaure $DUMP" >&2
else
  echo "ROLLBACK NAO FICOU HEALTHY: intervencao manual. Dump: $DUMP" >&2
fi
verifica_ponte || true
exit 6
REMOTE_EOF

recusa() { echo "RECUSADO: $1" >&2; shift; [ $# = 0 ] || printf '%s\n' "$@" >&2; exit 7; }

# --- guarda (#1336): o SHA tem de ser o origin/master atual ou descendente dele
if [ "$ORIGEM" = github ]; then
  git fetch -q origin || recusa "git fetch origin falhou; sem o origin/master atual nao da para conferir o SHA."     "Com o GitHub fora, use o espelho da VPS (#2958): --origem espelho"
else
  git fetch -q "$ESPELHO_URL" '+refs/heads/master:refs/remotes/espelho/master'     || recusa "git fetch do espelho $ESPELHO_URL falhou; sem o master do espelho nao da para conferir o SHA."
fi
MASTER="$(git rev-parse --verify "$MASTER_REF^{commit}")"
SHA_FULL="$(git rev-parse --verify -q "$REF^{commit}")" || recusa "'$REF' nao e um commit conhecido (rode git fetch?)."
SHA="${SHA_FULL:0:8}"
ATRAS="$(git rev-list --count "$SHA_FULL..$MASTER")"

if [ "$ROLLBACK" = 0 ]; then
  if ! git merge-base --is-ancestor "$MASTER" "$SHA_FULL"; then
    if git merge-base --is-ancestor "$SHA_FULL" "$MASTER"; then
      onde="esta $ATRAS commit(s) atras do origin/master (${MASTER:0:8}); publicar desfaz em producao:"
    else
      onde="nao descende do origin/master (${MASTER:0:8}); ficariam de fora $ATRAS commit(s) do master, entre eles:"
    fi
    recusa "$SHA $onde" \
      "$(git log --format='  %h %s' -10 "$SHA_FULL..$MASTER")" \
      "Para publicar o master atual: scripts/deploy/vps-deploy.sh" \
      "Para voltar de proposito a uma versao anterior: --rollback <sha> --motivo \"<por que>\""
  fi
  MODO=deploy
  FRENTE="$(git rev-list --count "$MASTER..$SHA_FULL")"
  guarda="$SHA e o origin/master atual"
  [ "$FRENTE" = 0 ] || guarda="$SHA descende do origin/master (${MASTER:0:8}), $FRENTE commit(s) a frente"
else
  [ -n "$MOTIVO" ] || { echo "ERRO: --rollback exige --motivo \"<por que>\"" >&2; exit 8; }
  git merge-base --is-ancestor "$SHA_FULL" "$MASTER" \
    || recusa "rollback so para commit que ja esteve no origin/master; $SHA nao esta na historia dele."
  MODO=rollback
  guarda="ROLLBACK para $SHA, $ATRAS commit(s) atras do origin/master (${MASTER:0:8}). Motivo: $MOTIVO"
fi

# Copia antiga deste script (worktree parado) nao tem as guardas novas: so roda a do master.
ESPERADO="$(git rev-parse -q --verify "$MASTER_REF:scripts/deploy/vps-deploy.sh" || true)"
if [ -n "$ESPERADO" ] && [ "$(git hash-object --path=scripts/deploy/vps-deploy.sh "$0")" != "$ESPERADO" ]; then
  msg="esta copia do vps-deploy.sh ($0) difere da do origin/master: script desatualizado ou alterado. Rode o do master atualizado."
  if [ "$DRY_RUN" = 1 ]; then echo "AVISO: $msg" >&2; else recusa "$msg"; fi
fi

TS="$(date -u +%Y%m%dT%H%M%SZ)"
QUEM="$(git config user.name || true) <$(git config user.email || true)> em ${USER:-${USERNAME:-?}}@$(hostname 2>/dev/null || echo '?')"
QUEM="${QUEM//[$'\t\n\r']/ }"
MOTIVO="${MOTIVO//[$'\t\n\r']/ }"

echo "==> Deploy de $SHA ($(git log -1 --format=%s "$SHA_FULL" | cut -c1-70)) em $VPS_HOST"
echo "    guarda: OK, $guarda"
[ "$ORIGEM" = github ] || echo "    origem: ESPELHO da VPS ($ESPELHO_URL), GitHub nao consultado"
echo "    servicos: $SERVICES | dump: $BACKUP_DIR/$DB_NAME-predeploy-$TS.dump | rollback: vps-prev-$TS"
echo "    historico: $BUILD_ROOT/deploy-history.log ($MODO, $QUEM)"

if [ "$ROLLBACK" = 1 ] && [ "$DRY_RUN" = 0 ]; then
  git log --format='    desfaz %h %s' -10 "$SHA_FULL..$MASTER"
  if [ -n "${ROLLBACK_CONFIRMA:-}" ]; then resp="$ROLLBACK_CONFIRMA"
  elif [ -t 0 ]; then read -r -p "Digite $SHA para confirmar o rollback: " resp
  else resp=""; fi
  [ "$resp" = "$SHA" ] || { echo "ERRO: rollback nao confirmado; digite exatamente $SHA (ou ROLLBACK_CONFIRMA=$SHA)." >&2; exit 8; }
fi

# --- imagens (#3323): puxa do GHCR o que o build-images publicou para o sha; build na VPS e o fallback.
IMAGENS=build ESPEROU=0
registro_tem() { # $1 = servico; 0 se o GHCR tem a imagem do sha (repo publico: pull anonimo)
  local repo="${REGISTRO#ghcr.io/}/easystok-$1" tok
  tok="$(curl -fsS --max-time 8 "https://ghcr.io/token?scope=repository:$repo:pull" 2>/dev/null | sed -n 's/.*"token" *: *"\([^"]*\)".*/\1/p')" || return 1
  [ -n "$tok" ] || return 1
  curl -fsSI --max-time 8 -o /dev/null -H "Authorization: Bearer $tok" \
    -H "Accept: application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json, application/vnd.docker.distribution.manifest.list.v2+json" \
    "https://ghcr.io/v2/$repo/manifests/$SHA_FULL" 2>/dev/null
}
registro_tem_todas() { local s; for s in $SERVICES; do registro_tem "$s" || return 1; done; }
# "<status> <conclusion>" do run do build-images para o sha; vazio se nao ha run ou se o gh nao existe.
estado_build_ci() {
  command -v gh >/dev/null 2>&1 || return 0
  gh run list --workflow build-images.yml --commit "$SHA_FULL" --limit 1 --json status,conclusion \
    -q 'if length == 0 then empty else .[0] | "\(.status) \(.conclusion)" end' 2>/dev/null || true
}
espera_build_ci() {
  local fim=$(( SECONDS + ESPERA_IMAGENS )) st sem_imagem=0 ultimo=$SECONDS
  ESPEROU=1
  echo "    imagens: o GHCR ainda nao tem $SHA; esperando o build-images (teto ${ESPERA_IMAGENS}s)"
  while :; do
    if registro_tem_todas; then IMAGENS=pull; echo "    imagens: GHCR pronto para $SHA"; return 0; fi
    st="$(estado_build_ci)"
    case "$st" in
      "") echo "    imagens: sem run do build-images para $SHA; build na VPS"; return 0 ;;
      "completed cancelled") echo "    imagens: build-images cancelado (push mais novo no master?); build na VPS"; return 0 ;;
      "completed success") sem_imagem=$(( sem_imagem + 1 ))
        [ "$sem_imagem" -le 6 ] || { echo "    imagens: build-images ok, mas o GHCR nao mostra o sha; build na VPS"; return 0; } ;;
      completed*) echo "    imagens: build-images terminou '${st#completed }'; build na VPS"; return 0 ;;
    esac
    [ "$SECONDS" -lt "$fim" ] || { echo "    imagens: teto de ${ESPERA_IMAGENS}s; build na VPS"; return 0; }
    if [ $(( SECONDS - ultimo )) -ge 30 ]; then ultimo=$SECONDS; echo "    imagens: build-images ${st:-sem estado}; ${SECONDS}s"; fi
    sleep "$ESPERA_PASSO"
  done
}
escolhe_imagens() {
  if [ "$LOCAL" = 1 ]; then echo "    imagens: build na VPS (--local)"; return 0; fi
  if [ "$ORIGEM" != github ]; then echo "    imagens: build na VPS (origem espelho: sem GitHub nem GHCR)"; return 0; fi
  if registro_tem_todas; then IMAGENS=pull; echo "    imagens: GHCR, prontas para $SHA"; return 0; fi
  if [ "$ROLLBACK" = 1 ] || [ "$SHA_FULL" != "$MASTER" ] || [ "$ESPERA_IMAGENS" = 0 ]; then
    echo "    imagens: build na VPS (o GHCR nao tem $SHA e nao ha build do master para esperar)"; return 0
  fi
  if [ "$DRY_RUN" = 1 ]; then echo "    imagens: o GHCR ainda nao tem $SHA; o deploy esperaria o build-images (teto ${ESPERA_IMAGENS}s)"; return 0; fi
  espera_build_ci
}
escolhe_imagens
# Durante a espera o master pode ter avancado (e o cancel-in-progress do build-images cancela o sha antigo):
# a guarda roda de novo, para nao publicar um sha que ficou para tras.
if [ "$ESPEROU" = 1 ] && [ "$ROLLBACK" = 0 ]; then
  git fetch -q origin || recusa "git fetch origin falhou depois da espera; sem o origin/master atual nao da para conferir o SHA."
  NOVO="$(git rev-parse --verify "$MASTER_REF^{commit}")"
  if [ "$NOVO" != "$MASTER" ] && ! git merge-base --is-ancestor "$NOVO" "$SHA_FULL"; then
    recusa "durante a espera o origin/master avancou para ${NOVO:0:8} e $SHA ficou para tras; publicar desfaz em producao:" \
      "$(git log --format='  %h %s' -10 "$SHA_FULL..$NOVO")" \
      "Para publicar o master atual: scripts/deploy/vps-deploy.sh"
  fi
fi

if [ "$DRY_RUN" = 1 ]; then
  bash -n <<<"$REMOTE" && echo "==> script remoto: sintaxe OK"
  echo "==> --dry-run: nada foi executado."
  exit 0
fi

ENVS="SHA=$SHA SHA_FULL=$SHA_FULL TS=$TS STACK_DIR=$STACK_DIR BUILD_ROOT=$BUILD_ROOT"
ENVS="$ENVS BACKUP_DIR=$BACKUP_DIR DB_CONTAINER=$DB_CONTAINER DB_NAME=$DB_NAME"
ENVS="$ENVS BACKUP_HOOK=$BACKUP_HOOK WPP_CONTAINER=$WPP_CONTAINER"
ENVS="$ENVS HEALTH_TIMEOUT=$HEALTH_TIMEOUT SERVICES='$SERVICES'"
ENVS="$ENVS RETEM_PREV=$RETEM_PREV CACHE_TETO=$CACHE_TETO"
ENVS="$ENVS MODO=$MODO QUEM=$(printf '%q' "$QUEM") MOTIVO=$(printf '%q' "$MOTIVO")"

# pull: nada sobe (stdin vazio). build: stdin do ssh = fonte (tar). O script remoto vai como argumento de bash -c.
# core.autocrlf=false: o checkout do CI e LF; o archive do Windows nao pode virar CRLF (hash do PWA no Dockerfile).
roda_remoto() { # $1 = pull|build
  local envs="$ENVS IMAGENS=$1 REGISTRO=$REGISTRO"
  if [ "$1" = pull ]; then
    ssh -o BatchMode=yes "$VPS_HOST" "$envs bash -c $(printf '%q' "$REMOTE")" </dev/null
  else
    git -c core.autocrlf=false archive --format=tar "$SHA_FULL" \
      | ssh -o BatchMode=yes "$VPS_HOST" "$envs bash -c $(printf '%q' "$REMOTE")"
  fi
}
rc=0
if [ "$IMAGENS" = pull ]; then
  roda_remoto pull || rc=$?
  if [ "$rc" = 9 ]; then
    echo "AVISO: as imagens do CI nao serviram (nada foi trocado); refazendo em build na VPS." >&2
    IMAGENS=build
  else
    echo "==> vps-deploy.sh: rc=$rc em ${SECONDS}s (imagens: pull)"
    exit "$rc"
  fi
fi
rc=0
roda_remoto build || rc=$?
echo "==> vps-deploy.sh: rc=$rc em ${SECONDS}s (imagens: build)"
exit "$rc"
