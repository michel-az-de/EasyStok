#!/usr/bin/env bash
# Testes do backup.sh (E3, #2977) sem tocar na VPS nem no Drive.
# Sobe um postgres:17 descartavel e um volume de teste; o restic e falso e registra as
# chamadas e o conteudo do stage no instante do `backup`. Precisa de docker.
# Uso: bash scripts/backup/backup.test.sh
set -euo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash no Windows: nao converter /tmp/... dos argumentos do docker
cd "$(dirname "$0")"
SCRIPT="$(pwd)/backup.sh"

# Sem flock (Git Bash no Windows): roda a si mesmo num container Linux com o docker do host.
if ! command -v flock >/dev/null 2>&1; then
  dir="$(pwd -W 2>/dev/null || pwd)"
  exec docker run --rm -v //var/run/docker.sock:/var/run/docker.sock -v "$dir":/s:ro docker:27-cli \
    sh -c 'apk add -q bash util-linux findutils coreutils grep sed procps tar >/dev/null && bash /s/backup.test.sh'
fi

falhas=0
confere() { if grep -q -- "$2" <<<"$3"; then echo "ok   $1"; else echo "FAIL $1: esperava '$2'"; echo "$3" | sed 's/^/     /' | head -20; falhas=$((falhas+1)); fi; }
nao_tem() { if grep -q -- "$2" <<<"$3"; then echo "FAIL $1: nao esperava '$2'"; falhas=$((falhas+1)); else echo "ok   $1"; fi; }

# ------------------------------------------------------------- estatico
nao_tem "nenhum forget usa --prune" "prune" "$(grep -v '^[[:space:]]*#' "$SCRIPT" | grep 'forget\|esquece')"

# ------------------------------------------------------------- ambiente
PG=ezbk-teste-pg-$$; VOL=ezbk-teste-vol-$$
W="$(mktemp -d)"
limpa() { docker rm -f "$PG" >/dev/null 2>&1 || true; docker volume rm -f "$VOL" >/dev/null 2>&1 || true; rm -rf "$W"; }
trap limpa EXIT
docker run -d --name "$PG" -e POSTGRES_PASSWORD=teste postgres:17 >/dev/null
docker volume create "$VOL" >/dev/null
docker run --rm -v "$VOL":/d alpine:latest sh -c 'mkdir -p /d/a/Cache && echo foto > /d/a/foto.jpg && echo lixo > /d/a/Cache/x' >/dev/null
for _ in $(seq 1 60); do docker exec "$PG" pg_isready -U postgres -q 2>/dev/null && break; sleep 1; done
sleep 2
docker exec -i "$PG" psql -U postgres -q -v ON_ERROR_STOP=1 >/dev/null <<'SQL'
CREATE ROLE easystock LOGIN PASSWORD 'segredo-que-nao-pode-vazar' NOBYPASSRLS;
CREATE DATABASE easystock OWNER easystock;
\c easystock
CREATE TABLE produtos (id int, "EmpresaId" uuid);
INSERT INTO produtos SELECT g, '00000000-0000-0000-0000-000000000001' FROM generate_series(1, 7) g;
CREATE TABLE clientes (id int);
INSERT INTO clientes SELECT generate_series(1, 3);
SQL

# restic falso: grava argumentos; no `backup` guarda a lista do stage (antes da limpeza)
mkdir -p "$W/bin" "$W/stack/wppconnect" "$W/shared/initdb"
cat > "$W/bin/restic" <<'EOF'
#!/usr/bin/env bash
echo "$*" >> "$FAKE_STATE/calls"
for a in "$@"; do
  case "$a" in
    backup)
      [ -n "${FAKE_RESTIC_DORME:-}" ] && sleep "$FAKE_RESTIC_DORME"
      [ -n "${FAKE_RESTIC_FALHA:-}" ] && exit 1
      [ -d "${STAGE_VER:-/nao}" ] && (cd "$STAGE_VER" && find . -type f | sort > "$FAKE_STATE/stage")
      [ -f "$STAGE_VER/db/manifesto.txt" ] && cp "$STAGE_VER/db/manifesto.txt" "$FAKE_STATE/manifesto"
      [ -f "$STAGE_VER/db/globals.sql" ] && cp "$STAGE_VER/db/globals.sql" "$FAKE_STATE/globals"
      [ -f "$STAGE_VER/volumes/$FAKE_VOL.tar" ] && tar -tf "$STAGE_VER/volumes/$FAKE_VOL.tar" > "$FAKE_STATE/tar"
      exit 0 ;;
  esac
done
exit 0
EOF
printf '#!/bin/sh\nexit 0\n' > "$W/bin/rclone"
chmod +x "$W/bin/restic" "$W/bin/rclone"
echo 'EZ_CRYPTO_KEK=x' > "$W/stack/.env"; echo 'services: {}' > "$W/stack/compose.yaml"
echo 'x' > "$W/shared/initdb/30-easystok.sql"
echo senha > "$W/senha"; : > "$W/rclone.conf"

caso() {
  local base; base="$(mktemp -d "$W/base.XXXX")"
  export FAKE_STATE="$base/state"; mkdir -p "$FAKE_STATE"; : > "$FAKE_STATE/calls"
  export BASE="$base/b" STAGE_VER="$base/b/stage" FAKE_VOL="$VOL"
  export RESTIC="$W/bin/restic" RCLONE="$W/bin/rclone" RESTIC_PASSWORD_FILE="$W/senha" RCLONE_CONFIG="$W/rclone.conf"
  export DB_CONTAINER="$PG" STACK_DIR="$W/stack" SHARED_DIR="$W/shared" VOLUMES="$VOL"
  set +e; env "$@" bash "$SCRIPT" ${ARGS:-} > "$base/out" 2>&1; local rc=$?; set -e
  echo "rc=$rc"
  echo "calls:"; sed 's/^/  /' "$FAKE_STATE/calls"
  echo "stage:"; sed 's/^/  /' "$FAKE_STATE/stage" 2>/dev/null || true
  echo "manifesto:"; sed 's/^/  /' "$FAKE_STATE/manifesto" 2>/dev/null || true
  echo "tar:"; sed 's/^/  /' "$FAKE_STATE/tar" 2>/dev/null || true
  echo "globals_com_senha=$(grep -ci 'password\|SCRAM' "$FAKE_STATE/globals" 2>/dev/null || echo 0)"
  echo "globals_role=$(grep -c 'CREATE ROLE easystock' "$FAKE_STATE/globals" 2>/dev/null || echo 0)"
  echo "sobra_no_stage=$(find "$base/b/stage" -type f 2>/dev/null | wc -l)"
  echo "estado:"; sed 's/^/  /' "$base/b/estado-${ESTADO_MODO:-diario}" 2>/dev/null || true
  echo "log:"; sed 's/^/  /' "$base/out"
}

r=$(caso X=1)
confere "diario: exit 0"                      "rc=0" "$r"
confere "diario: snapshot com tag diario"     "backup --host easystok-vps --tag diario" "$r"
confere "diario: leva o dump"                 "./db/easystock.dump" "$r"
confere "diario: leva globals e initdb"       "./db/30-easystok.sql" "$r"
confere "diario: leva o .env (KEK)"           "./config.*/stack/.env" "$r"
confere "diario: leva o volume"               "./volumes/$VOL.tar" "$r"
confere "diario: manifesto conta produtos"    "produtos|7" "$r"
confere "diario: manifesto conta clientes"    "clientes|3" "$r"
confere "diario: globals tem a role"          "globals_role=1" "$r"
confere "diario: globals sem hash de senha"   "globals_com_senha=0" "$r"
confere "diario: tar leva o arquivo"          "./a/foto.jpg" "$r"
nao_tem "diario: tar sem cache do Chrome"     "Cache/x" "$r"
confere "diario: tira lock velho"             "unlock" "$r"
confere "diario: retencao 7/4/12"             "forget --host easystok-vps --tag diario --group-by host,tags --keep-daily 7 --keep-weekly 4 --keep-monthly 12" "$r"
confere "diario: stage limpo no fim"          "sobra_no_stage=0" "$r"
confere "diario: estado rc=0"                 "rc=0" "$(sed -n '/^estado:/,/^log:/p' <<<"$r")"
nao_tem "diario: sem rotulos fora do dia 1"   "tag rotulos" "$r"
confere "diario: nao deixa dump no container" "^0$" "$(docker exec "$PG" sh -c 'ls /tmp/ezbk-* 2>/dev/null | wc -l')"

r=$(caso DB_NAME=naoexiste)
confere "banco inexistente: exit 4"           "rc=4" "$r"
nao_tem "banco inexistente: nao chama backup" "tag diario" "$r"
confere "banco inexistente: estado rc=4"      "rc=4" "$(sed -n '/^estado:/,/^log:/p' <<<"$r")"

r=$(caso CONFIG_OBRIGATORIOS=/nao/existe/.env)
confere "config ausente: exit 5"              "rc=5" "$r"
confere "config ausente: stage limpo"         "sobra_no_stage=0" "$r"

r=$(caso FAKE_RESTIC_FALHA=1)
confere "restic falha: exit 6"                "rc=6" "$r"
nao_tem "restic falha: nao esquece"           "forget" "$r"
confere "restic falha: stage limpo"           "sobra_no_stage=0" "$r"

r=$(caso RESTIC_PASSWORD_FILE=/nao/existe)
confere "sem senha: exit 2"                   "rc=2" "$r"

r=$(caso DIA_DO_MES=01)
confere "dia 1 sem tabela: rotulos dormente"  "retencao de 5 anos dormente" "$r"
r=$(caso DIA_DO_MES=01 ROTULOS_TABELAS=produtos)
confere "dia 1 com tabela: snapshot rotulos"  "tag rotulos --stdin-filename easystock-rotulos.dump" "$r"
confere "dia 1 com tabela: 5 anos"            "tag rotulos --group-by host,tags --keep-within 5y" "$r"

echo dump > "$W/pre.dump"
r=$(ARGS="pre-deploy $W/pre.dump" ESTADO_MODO=pre-deploy caso X=1)
confere "pre-deploy: exit 0"                  "rc=0" "$r"
confere "pre-deploy: tag pre-deploy"          "backup --host easystok-vps --tag pre-deploy $W/pre.dump" "$r"
confere "pre-deploy: 10 ultimos"              "tag pre-deploy --group-by host,tags --keep-last 10" "$r"
r=$(ARGS="pre-deploy /nao/existe" caso X=1)
confere "pre-deploy sem dump: exit 2"         "rc=2" "$r"

# Trava: um backup em andamento faz o segundo sair com 3; morto no meio, nao trava o seguinte.
base="$W/trava"; mkdir -p "$base/state" "$base/b"; : > "$base/state/calls"
envs=(FAKE_STATE="$base/state" BASE="$base/b" STAGE_VER="$base/b/stage" RESTIC="$W/bin/restic" RCLONE="$W/bin/rclone"
      RESTIC_PASSWORD_FILE="$W/senha" RCLONE_CONFIG="$W/rclone.conf" DB_CONTAINER="$PG" STACK_DIR="$W/stack"
      SHARED_DIR="$W/shared" VOLUMES="$VOL")
# setsid: grupo proprio, para matar a arvore toda como o systemd mata o cgroup
setsid env "${envs[@]}" FAKE_RESTIC_DORME=30 bash "$SCRIPT" > "$base/primeiro" 2>&1 &
pid=$!
for _ in $(seq 1 60); do grep -q "tag diario" "$base/state/calls" 2>/dev/null && break; sleep 0.5; done
set +e; env "${envs[@]}" bash "$SCRIPT" > "$base/segundo" 2>&1; rc2=$?; set -e
confere "trava ocupada: segundo sai com 3"    "^3$" "$rc2"
kill -9 -- "-$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true
set +e; env "${envs[@]}" bash "$SCRIPT" > "$base/terceiro" 2>&1; rc3=$?; set -e
confere "morto no meio: o seguinte roda (aceite 5)" "^0$" "$rc3"

[ "$falhas" = 0 ] && echo "TODOS OS CASOS PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
