#!/usr/bin/env bash
# =============================================================================
# waha-deploy.sh - ponte provisória do WhatsApp pelo WAHA na VPS (#2072, ADR-0062).
# Roda da máquina de dev (Git Bash). Guia completo: docs/dev/whatsapp-ponte-waha.md.
#
# Uso:
#   scripts/deploy/waha-deploy.sh subir            # gera segredos (se faltarem), sobe o ez-waha e cria a sessão
#   scripts/deploy/waha-deploy.sh codigo <numero>  # código de 8 letras para vincular o celular da loja
#   scripts/deploy/waha-deploy.sh qr [arquivo.png] # QR atual (vence em ~20 s; prefira o código ou o painel)
#   scripts/deploy/waha-deploy.sh status           # situação da sessão e final do número conectado
#   scripts/deploy/waha-deploy.sh ligar-api <numero>  # grava EZ_WAHA_* no .env do easystok (não imprime segredo)
#
# Segredos ficam só na VPS: /opt/stacks/waha/.env (chmod 600). Nenhum valor é impresso.
# Painel ao vivo: ssh -L 3100:127.0.0.1:3100 hostinger  e abra http://localhost:3100/dashboard
#
# Saídas: 0 ok | 2 uso inválido | 4 WAHA não respondeu | 5 sessão não criada | 6 número inválido
# =============================================================================
set -euo pipefail
VPS_HOST="${VPS_HOST:-hostinger}"
DESTINO="${DESTINO:-/opt/stacks/waha}"
STACK_API="${STACK_API:-/opt/stacks/easystok}"
PORTA="${PORTA:-3100}"
RAIZ="$(cd "$(dirname "$0")/../.." && pwd)"
ACAO="${1:-subir}"

uso() { echo "ERRO: $*" >&2; sed -n '/^# Uso:/,/^# Segredos/p' "$0" | sed '$d; s/^# \{0,1\}//' >&2; exit 2; }

digitos() {
  local n
  n="$(printf '%s' "${1:-}" | tr -cd '0-9')"
  [ "${#n}" -ge 10 ] && [ "${#n}" -le 15 ] || { echo "ERRO: número da loja só com dígitos e DDI (ex.: 5541999990000)" >&2; exit 6; }
  printf '%s' "$n"
}

# Chamada à API do WAHA de dentro da VPS (loopback), com a chave lida do .env. A chave não sai da VPS.
remoto_api() {
  local metodo="$1" caminho="$2" corpo="${3:-}"
  ssh -o BatchMode=yes "$VPS_HOST" "set -e; . '$DESTINO/.env'; curl -s -m 20 -X $metodo -H \"X-Api-Key: \$WAHA_API_KEY\" -H 'Content-Type: application/json' ${corpo:+--data '$corpo'} -w '\n%{http_code}' http://127.0.0.1:$PORTA$caminho"
}

case "$ACAO" in
  subir)
    echo "==> Preparando $VPS_HOST:$DESTINO"
    ssh -o BatchMode=yes "$VPS_HOST" "mkdir -p '$DESTINO'"
    scp -q "$RAIZ/scripts/deploy/waha/compose.yaml" "$VPS_HOST:$DESTINO/compose.yaml"

    echo "==> Segredos (gera só o que faltar; nada é impresso)"
    ssh -o BatchMode=yes "$VPS_HOST" bash -s -- "$DESTINO" <<'REMOTO'
set -euo pipefail
destino="$1"; env="$destino/.env"
umask 077; touch "$env"; chmod 600 "$env"
aleatorio() { openssl rand -hex 32 2>/dev/null || head -c 32 /dev/urandom | od -An -tx1 | tr -d ' \n'; }
tem() { grep -q "^$1=" "$env"; }
tem WAHA_API_KEY || echo "WAHA_API_KEY=$(aleatorio)" >>"$env"
. "$env"
tem WAHA_API_KEY_SHA512 || echo "WAHA_API_KEY_SHA512=$(printf %s "$WAHA_API_KEY" | sha512sum | cut -d' ' -f1)" >>"$env"
tem WAHA_DASHBOARD_PASSWORD || echo "WAHA_DASHBOARD_PASSWORD=$(aleatorio)" >>"$env"
tem EZ_WAHA_HMAC_KEY || echo "EZ_WAHA_HMAC_KEY=$(aleatorio)" >>"$env"
echo "    .env com $(grep -c '=' "$env") chaves (600)"
REMOTO

    echo "==> Subindo ez-waha (imagem fixada no compose)"
    ssh -o BatchMode=yes "$VPS_HOST" "cd '$DESTINO' && docker compose pull -q && docker compose up -d"

    echo "==> Esperando o WAHA responder"
    ok=0
    for _ in $(seq 1 40); do
      if [ "$(remoto_api GET /api/server/version | tail -n1)" = "200" ]; then ok=1; break; fi
      sleep 3
    done
    [ "$ok" = 1 ] || { echo "ERRO: ez-waha não respondeu" >&2; ssh "$VPS_HOST" "cd '$DESTINO' && docker compose logs --tail 30" >&2; exit 4; }

    echo "==> Sessão default (ignora grupo, status, canal e lista de transmissão)"
    codigo="$(remoto_api GET /api/sessions/default | tail -n1)"
    if [ "$codigo" = "404" ]; then
      codigo="$(remoto_api POST /api/sessions '{"name":"default","start":true,"config":{"ignore":{"status":true,"groups":true,"channels":true,"broadcast":true}}}' | tail -n1)"
      [ "$codigo" = "201" ] || { echo "ERRO: sessão não criada (HTTP $codigo)" >&2; exit 5; }
    else
      remoto_api POST /api/sessions/default/start >/dev/null || true
    fi
    "$0" status
    echo "==> Próximo passo: scripts/deploy/waha-deploy.sh codigo <numero-da-loja>  (ou o painel pelo túnel SSH)"
    ;;

  codigo)
    numero="$(digitos "${2:-}")"
    resposta="$(remoto_api POST /api/default/auth/request-code "{\"phoneNumber\":\"$numero\"}")"
    [ "$(printf '%s' "$resposta" | tail -n1)" = "201" ] || [ "$(printf '%s' "$resposta" | tail -n1)" = "200" ] \
      || { echo "ERRO: WAHA recusou o pedido de código: $(printf '%s' "$resposta" | head -n1)" >&2; exit 5; }
    echo "==> No celular da loja: WhatsApp Business > Aparelhos conectados > Conectar aparelho > Conectar com número de telefone"
    echo "    Código: $(printf '%s' "$resposta" | head -n1 | sed -n 's/.*"code":"\([^"]*\)".*/\1/p')"
    ;;

  qr)
    arquivo="${2:-$RAIZ/waha-qr.png}"
    ssh -o BatchMode=yes "$VPS_HOST" "set -e; . '$DESTINO/.env'; curl -sf -m 20 -H \"X-Api-Key: \$WAHA_API_KEY\" -H 'Accept: image/png' 'http://127.0.0.1:$PORTA/api/default/auth/qr?format=image'" >"$arquivo"
    echo "==> QR salvo em $arquivo (vence em ~20 s; leia já com o celular da loja)"
    ;;

  status)
    resposta="$(remoto_api GET /api/sessions/default)"
    corpo="$(printf '%s' "$resposta" | head -n1)"
    situacao="$(printf '%s' "$corpo" | sed -n 's/.*"status":"\([A-Z_]*\)".*/\1/p' | head -n1)"
    conta="$(printf '%s' "$corpo" | sed -n 's/.*"me":{"id":"\([0-9]*\)@c.us".*/\1/p')"
    echo "==> Sessão: ${situacao:-desconhecida} | número conectado: ${conta:+...${conta: -4}}${conta:-nenhum}"
    ;;

  ligar-api)
    numero="$(digitos "${2:-}")"
    ssh -o BatchMode=yes "$VPS_HOST" bash -s -- "$DESTINO" "$STACK_API" "$numero" <<'REMOTO'
set -euo pipefail
waha="$1/.env"; api="$2/.env"; numero="$3"
. "$waha"
grava() { grep -q "^$1=" "$api" && echo "    $1 já existe (mantido)" || { echo "$1=$2" >>"$api"; echo "    $1 gravado"; }; }
grava EZ_WAHA_BASE_URL "http://ez-waha:3000"
grava EZ_WAHA_API_KEY "$WAHA_API_KEY"
grava EZ_WAHA_HMAC_KEY "$EZ_WAHA_HMAC_KEY"
grava EZ_WAHA_PHONE_NUMBER_ID "$numero"
grava EZ_WHATSAPP_CLIENTE "waha"
REMOTO
    echo "==> Falta: mapear as EZ_WAHA_* e EZ_WHATSAPP_CLIENTE no compose do easystok (api e worker) e publicar a API."
    ;;

  *) uso "ação desconhecida: $ACAO" ;;
esac
