#!/usr/bin/env bash
# Testes do instalar.sh (E3, #2977) com HOME descartavel e restic/rclone falsos.
# Roda num debian com systemd-analyze para validar tambem as units. Precisa de docker.
# Uso: bash scripts/backup/instalar.test.sh
set -euo pipefail
export MSYS_NO_PATHCONV=1
cd "$(dirname "$0")"
if [ "${DENTRO:-0}" != 1 ]; then
  dir="$(pwd -W 2>/dev/null || pwd)"
  exec docker run --rm -e DENTRO=1 -v "$dir":/s:ro debian:12-slim bash -c \
    'apt-get update -qq >/dev/null && apt-get install -y -qq systemd >/dev/null 2>&1; bash /s/instalar.test.sh'
fi
falhas=0
confere() { if grep -q -- "$2" <<<"$3"; then echo "ok   $1"; else echo "FAIL $1: esperava '$2'"; echo "$3" | sed 's/^/     /'; falhas=$((falhas+1)); fi; }

# units
# copia com modo 644: o bind mount do Windows chega 777
mkdir -p /tmp/u && install -m 644 /s/systemd/* /tmp/u/
u="$(systemd-analyze verify /tmp/u/easystok-backup.service /tmp/u/easystok-backup.timer 2>&1 | grep -v 'backup-restic/backup.sh' || true)"
confere "units sem erro de sintaxe" "^$" "${u:-}"
confere "timer as 03:00 de Brasilia" "06:00:00 UTC" "$(systemd-analyze calendar --iterations=1 '*-*-* 03:00:00 America/Sao_Paulo' 2>&1)"

useradd -m felipe
prepara() {
  rm -rf /home/felipe/* /home/felipe/.config; mkdir -p /home/felipe/bin /home/felipe/src
  cp -r /s/backup.sh /s/instalar.sh /s/systemd /home/felipe/src/
  cat > /home/felipe/bin/restic <<'R'
#!/bin/sh
echo "$*" >> /home/felipe/restic.calls
case "$*" in version) echo "restic 0.19.1 compiled with go";; *"cat config"*) exit "${FAKE_CAT:-0}";; esac
R
  printf '#!/bin/sh\n' > /home/felipe/bin/rclone; chmod +x /home/felipe/bin/*
  chown -R felipe /home/felipe
}
config() {
  su felipe -c 'set -e; d=~/.config/easystok-backup; mkdir -p $d; chmod 700 $d
    echo s > $d/restic.senha; chmod 600 $d/restic.senha
    printf "[ezdrive]\ntype = drive\nscope = drive.file\nteam_drive = 0AOtKvTkSaRWCUk9PVA\nclient_id = cid-teste\nclient_secret = segredo-teste\ntoken = token-teste\n" > $d/ezdrive.conf
    chmod 600 $d/ezdrive.conf'
}
# altera o ezdrive.conf de teste: $1 = expressao sed
altera() { su felipe -c "sed -i '$1' ~/.config/easystok-backup/ezdrive.conf"; }
roda() { set +e; out="$(su felipe -c "FAKE_CAT=${1:-0} bash ~/src/instalar.sh --sem-timer" 2>&1)"; echo "rc=$?"; echo "$out"; set -e
  echo "calls:"; cat /home/felipe/restic.calls 2>/dev/null || true; }

prepara; r="$(roda)"
confere "sem config: exit 2"              "rc=2" "$r"
confere "sem config: aponta o runbook"    "backup-drive.md" "$r"
confere "sem config: nao toca no repo"   "^0$" "$(grep -c 'config\|init' /home/felipe/restic.calls || true)"
confere "sem config: backup.sh instalado" "700" "$(stat -c %a /home/felipe/backup-restic/backup.sh)"

prepara; config; chmod 644 /home/felipe/.config/easystok-backup/restic.senha; r="$(roda)"
confere "senha legivel por outros: exit 2" "rc=2" "$r"

prepara; config; r="$(roda 10)"
confere "repo novo: init"                 "^.*init$" "$r"
confere "repo novo: exit 0"               "rc=0" "$r"
prepara; config; r="$(roda 0)"
confere "repo existente: sem init"        "ja existe" "$r"
prepara; config; r="$(roda 12)"
confere "senha errada: exit 2"            "rc=2" "$r"
confere "nunca imprime a senha"           "^0$" "$(grep -c '^s$' <<<"$r" || true)"

prepara; config; r="$(roda)"
confere "ezdrive.conf completo: exit 0"   "rc=0" "$r"
prepara; config; altera '/^client_id/d'; r="$(roda)"
confere "sem client_id: recusa"           "rc=2" "$r"
confere "sem client_id: nomeia a chave"  "client_id" "$r"
prepara; config; altera 's/^client_id = .*/client_id = /'; r="$(roda)"
confere "client_id vazio: recusa"         "rc=2" "$r"
confere "client_id vazio: nomeia a chave" "client_id" "$r"
prepara; config; altera 's/^client_id = .*/client_id =    /'; r="$(roda)"
confere "client_id so com espacos: recusa" "rc=2" "$r"
prepara; config; altera 's/^token = .*/token = /'; r="$(roda)"
confere "token vazio: recusa"             "rc=2" "$r"
prepara; config; altera 's/drive\.file/drive/'; r="$(roda)"
confere "scope = drive (sem .file): recusa" "rc=2" "$r"
confere "scope: nomeia a chave"         "scope" "$r"
prepara; config; altera '/^token/d'; r="$(roda)"
confere "sem token: recusa"               "rc=2" "$r"
confere "sem token: nomeia a chave"      "token" "$r"
prepara; config; su felipe -c "printf '[gdrive]\ntype = drive\ntoken = antigo\n' > ~/.config/easystok-backup/rclone.conf; chmod 600 ~/.config/easystok-backup/rclone.conf"
antes="$(su felipe -c 'sha256sum ~/.config/easystok-backup/rclone.conf ~/.config/easystok-backup/ezdrive.conf')"; r="$(roda)"
depois="$(su felipe -c 'sha256sum ~/.config/easystok-backup/rclone.conf ~/.config/easystok-backup/ezdrive.conf')"
confere "rclone.conf antigo e ezdrive.conf intocados" "rc=0" "$r"
[ "$antes" = "$depois" ] && echo "ok   checksums iguais antes e depois" || { echo "FAIL checksums mudaram"; falhas=$((falhas+1)); }

[ "$falhas" = 0 ] && echo "TODOS OS CASOS PASSARAM" || { echo "$falhas FALHA(S)"; exit 1; }
