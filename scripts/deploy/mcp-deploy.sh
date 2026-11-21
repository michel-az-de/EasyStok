#!/usr/bin/env bash
# =============================================================================
# mcp-deploy.sh - stack do MCP remoto baba-diag (ADR-0069, issue #3062), em /opt/stacks/mcp na VPS.
#
# Roda da maquina de dev (Git Bash), como o vps-deploy.sh: so felipe + docker, sem sudo.
# Separado do deploy do ERP: subir aqui nao toca em ez-api, ez-worker nem ez-web.
#
#   subir    guarda do vps-deploy (SHA = origin/master ou descendente), git archive so de tools/mcp-baba e da
#            stack, trava /tmp/easystok-deploy.lock, rede mcp (172.16.8.0/24, nunca apagada), chave SSH propria,
#            known_hosts do host, build easystok-mcp:vps-<sha>, up, healthy e GIT_SHA conferidos, shared-caddy
#            conectado a rede mcp, linha no deploy-history.log. Se nao ficar healthy, volta a imagem anterior.
#   caddy    confere o DNS, reconecta o shared-caddy e aplica o bloco do dominio (idempotente; valida e recarrega,
#            restaura o backup se falhar). Rodar de novo depois de recriar o shared-caddy.
#   chave    copia o gate.sh do origin/master para ~/build e imprime o passo root unico do Felipe.
#   token-*  arquivo /opt/stacks/mcp/auth/tokens (pessoa:sha256:validade). O token emitido vai so para um
#            arquivo local 600 (padrao ~/.mcp-token-<pessoa>), nunca para a tela.
#   descer   tira o bloco do Caddy, desconecta o shared-caddy e derruba a stack. Rede, chave e tokens ficam.
#
# Uso:
#   scripts/deploy/mcp-deploy.sh subir [sha]
#   scripts/deploy/mcp-deploy.sh caddy
#   scripts/deploy/mcp-deploy.sh chave
#   scripts/deploy/mcp-deploy.sh token-emitir <pessoa> [dias, padrao 90] [arquivo, padrao ~/.mcp-token-<pessoa>]
#   scripts/deploy/mcp-deploy.sh token-revogar <pessoa>
#   scripts/deploy/mcp-deploy.sh tokens
#   scripts/deploy/mcp-deploy.sh status
#   scripts/deploy/mcp-deploy.sh descer
#
# Saidas: 0 ok | 2 uso | 3 outro deploy em andamento | 4 build ou rede | 6 nao ficou healthy | 7 SHA recusado
# | 9 DNS ou Caddy
# =============================================================================
set -euo pipefail

VPS_HOST="${VPS_HOST:-hostinger}"
ENVS="STACK=/opt/stacks/mcp BUILD_ROOT=${BUILD_ROOT:-/home/felipe/build} DOMINIO=mcp.easystok.online IP_VPS=92.113.33.60"
ENVS="$ENVS CADDYFILE=/opt/stacks/shared/Caddyfile REDE=mcp SUBNET=172.16.8.0/24 GW=172.16.8.1"

uso() { echo "ERRO: $*" >&2; sed -n '/^# Uso:/,/^# Saidas:/p' "$0" | sed '$d; s/^# \{0,1\}//' >&2; exit 2; }
recusa() { echo "RECUSADO: $*" >&2; exit 7; }
# roda um script literal na VPS com as variaveis de ENVS e as extras ($2...); stdin passa adiante
remoto() { local s="$1"; shift; ssh -o BatchMode=yes "$VPS_HOST" "$ENVS $* bash -c $(printf '%q' "$s")"; }

# Comum aos scripts remotos: Caddy (validar, recarregar, restaurar) e conexao do shared-caddy a rede mcp.
read -r -d '' COMUM <<'EOF' || true
set -euo pipefail
umask 077
# mesma trava do vps-deploy: subir, caddy e descer nunca correm junto com outro deploy nem entre si
trava() { exec 9>/tmp/easystok-deploy.lock; flock -n 9 || { echo "ERRO: outro deploy em andamento (trava /tmp/easystok-deploy.lock)" >&2; exit 3; }; }
recarrega() { docker exec shared-caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1 \
  && docker exec shared-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1; }
restaura() { cat "$1" > "$CADDYFILE"; recarrega || true; }   # cat preserva o inode do bind de arquivo
conecta() {
  docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{$k}} {{end}}' shared-caddy | grep -qw "$REDE" \
    || docker network connect "$REDE" shared-caddy
}
EOF

read -r -d '' SUBIR <<'EOF' || true
registra() { local rc=$?; printf '%s\tmcp\t%s\trc=%s\t%s\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$SHA" "$rc" "$QUEM" "ADR-0069 baba-diag remoto" \
  >>"$BUILD_ROOT/deploy-history.log" 2>/dev/null || true; }
trap registra EXIT
trava
# o compose roda como 1000:1000, dono da chave e dos tokens; outro uid daria healthy com 401 para todos
[ "$(id -u)" = 1000 ] || { echo "ERRO: rode como o uid 1000 (felipe); este e $(id -u)" >&2; exit 4; }

SRC="$BUILD_ROOT/easystok-mcp-$SHA"
rm -rf "$SRC"; mkdir -p "$SRC"
# fonte com umask 022: com o 077 do COMUM, src/diag sai 700 root na imagem e o uid node nao le (visto em 21/11)
(umask 022; tar -x -C "$SRC")

echo "==> [1/5] rede $REDE"
docker network inspect "$REDE" >/dev/null 2>&1 || docker network create --driver bridge --subnet "$SUBNET" --gateway "$GW" "$REDE" >/dev/null
[ "$(docker network inspect -f '{{range .IPAM.Config}}{{.Subnet}} {{.Gateway}}{{end}}' "$REDE")" = "$SUBNET $GW" ] \
  || { echo "ERRO: rede $REDE existe com outra subnet; o from= do authorized_keys depende de $SUBNET" >&2; exit 4; }

echo "==> [2/5] chave SSH, known_hosts e tokens em $STACK"
mkdir -p "$STACK/ssh" "$STACK/auth"
chmod 700 "$STACK" "$STACK/ssh" "$STACK/auth"
[ -f "$STACK/ssh/id_ed25519" ] || ssh-keygen -q -t ed25519 -N '' -C ez-mcp -f "$STACK/ssh/id_ed25519"
printf '%s %s\n' "$GW" "$(cut -d' ' -f1,2 /etc/ssh/ssh_host_ed25519_key.pub)" > "$STACK/ssh/known_hosts"
cat > "$STACK/ssh/config" <<CFG
Host baba-mcp
  HostName $GW
  User baba-mcp
  IdentityFile ~/.ssh/id_ed25519
  IdentitiesOnly yes
  StrictHostKeyChecking yes
  UpdateHostKeys no
  UserKnownHostsFile ~/.ssh/known_hosts
  BatchMode yes
CFG
chmod 600 "$STACK/ssh/"*
[ -f "$STACK/auth/tokens" ] || printf '# pessoa:sha256:validade (scripts/deploy/mcp-deploy.sh token-emitir)\n' > "$STACK/auth/tokens"
chmod 600 "$STACK/auth/tokens"
install -m 600 "$SRC/scripts/deploy/mcp/compose.yaml" "$STACK/compose.yaml"

echo "==> [3/5] build easystok-mcp:vps-$SHA"
docker build -q --build-arg GIT_SHA="$SHA_FULL" -t "easystok-mcp:vps-$SHA" "$SRC/tools/mcp-baba" >/dev/null || exit 4
rm -rf "$SRC"
# so vira vps-prev a imagem que esta healthy agora: rollback para imagem quebrada repetiria o loop (#3072)
if docker image inspect easystok-mcp:vps >/dev/null 2>&1 \
  && [ "$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{end}}' ez-mcp 2>/dev/null)" = healthy ]; then
  docker tag easystok-mcp:vps easystok-mcp:vps-prev
fi
docker tag "easystok-mcp:vps-$SHA" easystok-mcp:vps

echo "==> [4/5] up e healthy"
cd "$STACK"
docker compose up -d >/dev/null
st=ausente
for _ in $(seq 1 40); do
  st="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{end}}' ez-mcp 2>/dev/null || echo ausente)"
  [ "$st" = healthy ] && break; sleep 3
done
sha_ok=0
docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' ez-mcp | grep -qx "GIT_SHA=$SHA_FULL" && sha_ok=1
if [ "$st" != healthy ] || [ "$sha_ok" != 1 ]; then
  echo "ERRO: ez-mcp health=$st sha_ok=$sha_ok" >&2
  docker logs --tail 30 ez-mcp >&2 2>&1 || true
  if docker image inspect easystok-mcp:vps-prev >/dev/null 2>&1; then
    docker tag easystok-mcp:vps-prev easystok-mcp:vps; docker compose up -d >/dev/null; echo "voltou para a imagem anterior" >&2
  else
    # sem imagem anterior saudavel nada fica em loop: o container sai (rede, chave e tokens ficam)
    docker compose down >/dev/null 2>&1 || true; echo "sem imagem anterior saudavel: ez-mcp derrubado" >&2
  fi
  exit 6
fi

echo "==> [5/5] shared-caddy na rede $REDE e faxina"
conecta
docker images easystok-mcp --format '{{.Tag}}' | grep -E '^vps-[0-9a-f]{8}$' | grep -vx "vps-$SHA" | sed 's/^/easystok-mcp:/' | xargs -r docker rmi >/dev/null 2>&1 || true
echo "OK: ez-mcp healthy em $SHA"
EOF

read -r -d '' CADDY <<'EOF' || true
ip="$( (command -v dig >/dev/null 2>&1 && dig +short A "$DOMINIO" @1.1.1.1 | tail -1) || true)"
[ -n "$ip" ] || ip="$(getent ahostsv4 "$DOMINIO" | awk 'NR==1{print $1}' || true)"
[ "$ip" = "$IP_VPS" ] || { echo "ERRO: $DOMINIO resolve para '${ip:-nada}', esperado $IP_VPS. Crie o registro A e espere propagar." >&2; exit 9; }
docker inspect ez-mcp >/dev/null 2>&1 || { echo "ERRO: ez-mcp nao existe; rode subir antes" >&2; exit 9; }
trava
conecta
if grep -q "^$DOMINIO {" "$CADDYFILE"; then echo "Caddy: bloco ja existe"; else
  bak="$CADDYFILE.antes-mcp-$(date -u +%Y%m%dT%H%M%SZ)"; cp -p "$CADDYFILE" "$bak"
  python3 - "$CADDYFILE" "$DOMINIO" <<'PY'
import sys, pathlib
p, d = pathlib.Path(sys.argv[1]), sys.argv[2]
bloco = ("\n# MCP remoto baba-diag (ADR-0069). Gerido por scripts/deploy/mcp-deploy.sh caddy/descer.\n"
         f"{d} {{\n\trequest_body {{\n\t\tmax_size 128KB\n\t}}\n\treverse_proxy ez-mcp:8080\n}}\n")
with p.open('r+') as f:
    t = f.read(); f.seek(0); f.write(t.rstrip('\n') + '\n' + bloco); f.truncate()
PY
  recarrega || { restaura "$bak"; echo "ERRO: Caddyfile invalido; backup restaurado" >&2; exit 9; }
  echo "Caddy: bloco aplicado (backup $bak)"
fi
for _ in $(seq 1 30); do
  c="$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "https://$DOMINIO/saude" || true)"
  [ "$c" = 200 ] && { echo "OK: https://$DOMINIO/saude 200"; exit 0; }; sleep 4
done
echo "ERRO: https://$DOMINIO/saude nao respondeu 200 (ultimo: $c). Ver: docker logs shared-caddy" >&2; exit 9
EOF

read -r -d '' DESCER <<'EOF' || true
trava
if grep -q "^$DOMINIO {" "$CADDYFILE"; then
  bak="$CADDYFILE.antes-mcp-descer-$(date -u +%Y%m%dT%H%M%SZ)"; cp -p "$CADDYFILE" "$bak"
  python3 - "$CADDYFILE" "$DOMINIO" <<'PY'
import re, sys, pathlib
p, d = pathlib.Path(sys.argv[1]), sys.argv[2]
padrao = re.compile(r"\n?# MCP remoto baba-diag[^\n]*\n" + re.escape(d) + r" \{\n(?:\t[^\n]*\n)*\}\n")
with p.open('r+') as f:
    t = f.read(); n = padrao.sub('\n', t, count=1)
    if n == t: sys.exit('bloco fora do formato do mcp-deploy.sh; tire a mao')
    f.seek(0); f.write(n); f.truncate()
PY
  recarrega || { restaura "$bak"; echo "ERRO: Caddy nao recarregou; backup restaurado" >&2; exit 9; }
  echo "Caddy: bloco removido (backup $bak)"
fi
docker network disconnect "$REDE" shared-caddy 2>/dev/null || true
[ ! -f "$STACK/compose.yaml" ] || (cd "$STACK" && docker compose down)
echo "OK: stack mcp derrubada; rede $REDE, chave e tokens mantidos"
EOF

read -r -d '' TOKEN_EMITIR <<'EOF' || true
arq="$STACK/auth/tokens"
[ -f "$arq" ] || { echo "ERRO: $arq nao existe; rode subir antes" >&2; exit 2; }
tok="$(head -c 32 /dev/urandom | base64 | tr '+/' '-_' | tr -d '=\n')"
hash="$(printf '%s' "$tok" | sha256sum | cut -d' ' -f1)"
validade="$(date -u -d "+$DIAS days" +%F)"
tmp="$STACK/auth/.tokens.$$"
{ awk -F: -v p="$PESSOA" '$1 != p' "$arq"; printf '%s:%s:%s\n' "$PESSOA" "$hash" "$validade"; } > "$tmp"
chmod 600 "$tmp"; mv -f "$tmp" "$arq"
echo "token de $PESSOA valido ate $validade (substitui o anterior, se havia). Guarde agora: nao aparece de novo." >&2
printf '%s\n' "$tok"
EOF

read -r -d '' TOKEN_REVOGAR <<'EOF' || true
arq="$STACK/auth/tokens"; tmp="$STACK/auth/.tokens.$$"
awk -F: -v p="$PESSOA" '$1 == p' "$arq" | grep -q . || { echo "sem token de $PESSOA" >&2; exit 0; }
awk -F: -v p="$PESSOA" '$1 != p' "$arq" > "$tmp"; chmod 600 "$tmp"; mv -f "$tmp" "$arq"
echo "revogado: $PESSOA (vale no proximo pedido, sem reiniciar)"
EOF

read -r -d '' TOKENS <<'EOF' || true
awk -F: '!/^#/ && NF == 3 { print $1 "  valido ate " $3 }' "$STACK/auth/tokens"
EOF

read -r -d '' STATUS <<'EOF' || true
docker ps -a --filter name=ez-mcp --format 'container: {{.Names}} | {{.Status}} | {{.Image}}'
docker inspect -f '{{range .Config.Env}}{{println .}}{{end}}' ez-mcp 2>/dev/null | sed -n 's/^GIT_SHA=/commit: /p'
docker inspect -f '{{range $k, $v := .NetworkSettings.Networks}}{{$k}} {{end}}' shared-caddy | grep -qw "$REDE" \
  && echo "shared-caddy: na rede $REDE" || echo "ALERTA: shared-caddy fora da rede $REDE (rode: mcp-deploy.sh caddy)"
grep -q "^$DOMINIO {" "$CADDYFILE" && echo "Caddy: bloco presente" || echo "Caddy: sem bloco"
echo "https /saude: $(curl -s -o /dev/null -w '%{http_code}' --max-time 10 "https://$DOMINIO/saude" || echo falhou)"
echo "tokens ativos: $(awk -F: '!/^#/ && NF == 3' "$STACK/auth/tokens" 2>/dev/null | wc -l)"
EOF

pessoa_ok() { [[ "${1:-}" =~ ^[a-z0-9._-]{1,40}$ ]] || uso "pessoa: minusculas, digitos, ponto, _ ou -, ate 40"; }

acao="${1:-}"; [ $# -eq 0 ] || shift
case "$acao" in
  subir)
    [ $# -le 1 ] || uso "subir aceita no maximo um sha"
    git fetch -q origin || recusa "git fetch origin falhou"
    MASTER="$(git rev-parse --verify 'origin/master^{commit}')"
    SHA_FULL="$(git rev-parse --verify -q "${1:-origin/master}^{commit}")" || recusa "'${1:-}' nao e um commit conhecido"
    git merge-base --is-ancestor "$MASTER" "$SHA_FULL" || recusa "${SHA_FULL:0:8} nao e o origin/master (${MASTER:0:8}) nem descendente dele"
    ESPERADO="$(git rev-parse -q --verify origin/master:scripts/deploy/mcp-deploy.sh || true)"
    [ -z "$ESPERADO" ] || [ "$(git hash-object --path=scripts/deploy/mcp-deploy.sh "$0")" = "$ESPERADO" ] \
      || recusa "esta copia do mcp-deploy.sh ($0) difere da do origin/master; rode a do master atualizado"
    QUEM="$(git config user.name || true) em ${USER:-${USERNAME:-?}}@$(hostname 2>/dev/null || echo '?')"
    QUEM="${QUEM//[$'\t\n\r']/ }"
    echo "==> mcp: ${SHA_FULL:0:8} ($(git log -1 --format=%s "$SHA_FULL" | cut -c1-70)) em $VPS_HOST"
    git archive --format=tar "$SHA_FULL" tools/mcp-baba scripts/deploy/mcp \
      | remoto "$COMUM"$'\n'"$SUBIR" "SHA=${SHA_FULL:0:8} SHA_FULL=$SHA_FULL QUEM=$(printf '%q' "$QUEM")"
    ;;
  caddy) remoto "$COMUM"$'\n'"$CADDY" </dev/null ;;
  descer) remoto "$COMUM"$'\n'"$DESCER" </dev/null ;;
  chave)
    git fetch -q origin || recusa "git fetch origin falhou"
    PUB="$(ssh -o BatchMode=yes "$VPS_HOST" 'cat /opt/stacks/mcp/ssh/id_ed25519.pub')" || uso "sem chave; rode subir antes"
    [[ "$PUB" =~ ^ssh-ed25519\ [A-Za-z0-9+/=]+\ ez-mcp$ ]] || uso "chave publica fora do formato esperado"
    LINHA="from=\"172.16.8.0/24\",command=\"/opt/baba-mcp/gate.sh --sem-banco\",restrict $PUB"
    # gate sem o modo --sem-banco ignora o argumento e entrega psql_ro a esta chave: so com o gate da #3062 no master
    GATE="$(git show origin/master:scripts/mcp/gate.sh)"
    grep -qF -- '--sem-banco) SEM_BANCO=1' <<<"$GATE" || recusa "o gate.sh do origin/master nao tem --sem-banco; mergeie a #3062 antes"
    printf '%s\n' "$GATE" | ssh -o BatchMode=yes "$VPS_HOST" 'umask 022; cat > ~/build/gate.sh.mcp'
    # o passo root vira arquivo na VPS: o comando do Felipe fica curto e sem aspas aninhadas (PowerShell)
    ssh -o BatchMode=yes "$VPS_HOST" 'umask 022; cat > ~/build/mcp-root.sh' <<FIM
#!/usr/bin/env bash
# Passo root unico do MCP remoto (ADR-0069), gerado por scripts/deploy/mcp-deploy.sh chave. Idempotente.
set -euo pipefail
install -o root -g root -m 755 /home/felipe/build/gate.sh.mcp /opt/baba-mcp/gate.sh
# prova antes de liberar a chave: --sem-banco nega psql_ro (64) e deixa containers passar (0)
# SQL valido de proposito: sem --sem-banco o gate aceitaria (0); so o modo novo nega (64)
rc=0; echo 'select 1' | GATE_DRY=1 SSH_ORIGINAL_COMMAND=psql_ro /opt/baba-mcp/gate.sh --sem-banco >/dev/null 2>&1 || rc=\$?
[ "\$rc" -eq 64 ] || { echo "ERRO: gate instalado nao nega psql_ro em --sem-banco (saiu \$rc); chave NAO liberada" >&2; exit 1; }
GATE_DRY=1 SSH_ORIGINAL_COMMAND=containers /opt/baba-mcp/gate.sh --sem-banco >/dev/null
echo "gate --sem-banco: psql_ro negado (64), containers ok"
grep -qF '$PUB' /home/baba-mcp/.ssh/authorized_keys || printf '%s\n' '$LINHA' >> /home/baba-mcp/.ssh/authorized_keys
sha256sum /opt/baba-mcp/gate.sh
grep -F ' ez-mcp' /home/baba-mcp/.ssh/authorized_keys
FIM
    echo "==> ~/build/mcp-root.sh na VPS:"; ssh -o BatchMode=yes "$VPS_HOST" 'cat ~/build/mcp-root.sh; sha256sum ~/build/gate.sh.mcp'
    echo; echo "Passo root (Felipe, uma vez, pede a senha do sudo):"
    echo "  ssh -t $VPS_HOST \"sudo bash /home/felipe/build/mcp-root.sh\""
    ;;
  token-emitir)
    pessoa_ok "${1:-}"; DIAS="${2:-90}"; SAIDA="${3:-$HOME/.mcp-token-$1}"
    [[ "$DIAS" =~ ^[0-9]{1,3}$ ]] && [ "$DIAS" -ge 1 ] && [ "$DIAS" -le 365 ] || uso "dias: 1 a 365"
    # o token vai so para um arquivo 600 local: nunca para a tela, log ou transcript de agente
    ( umask 077; remoto "$COMUM"$'\n'"$TOKEN_EMITIR" "PESSOA=$1 DIAS=$DIAS" </dev/null > "$SAIDA" )
    echo "token gravado em $SAIDA (so voce le); use com: --header \"Authorization: Bearer \$(cat $SAIDA)\"" >&2 ;;
  token-revogar) pessoa_ok "${1:-}"; remoto "$COMUM"$'\n'"$TOKEN_REVOGAR" "PESSOA=$1" </dev/null ;;
  tokens) remoto "$COMUM"$'\n'"$TOKENS" </dev/null ;;
  status) remoto "$COMUM"$'\n'"$STATUS" </dev/null ;;
  *) uso "acao desconhecida: '${acao}'" ;;
esac
