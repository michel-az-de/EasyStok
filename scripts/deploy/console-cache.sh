#!/usr/bin/env bash
# Mantém a política de cache do Console no Caddy compartilhado, sem reiniciar o Caddy.
#   /assets/* (nome com hash): cache imutável de um ano no navegador (#2254).
#   HTML, versao.json e o resto: no-store, para a aba ver a versão nova na hora.
#   Arquivo com extensão que não existe (favicon.ico...): 404, não o app inteiro (#2254).
#   Cabeçalhos de segurança do estático (CSP Report-Only, nosniff, Referrer, X-Frame), #2407.
# Idempotente: se o bloco já está na forma nova, só valida e recarrega.
set -euo pipefail
ssh -o BatchMode=yes "${VPS_HOST:-hostinger}" 'bash -s' <<'REMOTE'
set -euo pipefail
CONFIG=/opt/stacks/shared/Caddyfile
BACKUP="$CONFIG.antes-cache-console-$(date -u +%Y%m%dT%H%M%SZ)"
cp "$CONFIG" "$BACKUP"
python3 - <<'PY'
from pathlib import Path
import re
path = Path('/opt/stacks/shared/Caddyfile')
text = path.read_text()
blocks = list(re.finditer(r'^app\.easystok\.online \{\n.*?^\}', text, re.MULTILINE | re.DOTALL))
if len(blocks) != 1:
    raise SystemExit('ERRO: host do Console diferente do esperado; nenhum ajuste aplicado.')
block = blocks[0].group()
root = '\t\troot * /srv/docs/easystok-console\n'
fallback = '\t\ttry_files {path} /index.html\n\t\tfile_server\n\t}\n'
antigos = [
    '\thandle {\n' + root + fallback,
    '\thandle {\n\t\theader Cache-Control "no-store, max-age=0"\n' + root + fallback,
]
cache_e_spa = (
    '\t\t@imutavel path /assets/*\n'
    '\t\theader @imutavel Cache-Control "public, max-age=31536000, immutable"\n'
    '\t\t@mutavel not path /assets/*\n'
    '\t\theader @mutavel Cache-Control "no-store, max-age=0"\n'
    '\t\t@spa {\n'
    '\t\t\tnot file\n'
    '\t\t\tnot path_regexp \\.[A-Za-z0-9]+$\n'
    '\t\t}\n'
    '\t\trewrite @spa /index.html\n'
    '\t\tfile_server\n'
    '\t}\n'
)
# Mesmos valores do EasyStock.Console/nginx.conf (#2392); só o estático, /api/* tem os da API (#2407).
seguranca = (
    '\t\theader {\n'
    "\t\t\tContent-Security-Policy-Report-Only \"default-src 'self'; script-src 'self' https://accounts.google.com https://connect.facebook.net; style-src 'self' 'unsafe-inline' https://accounts.google.com; img-src 'self' data: blob: https:; font-src 'self' data:; media-src 'self' data: blob:; connect-src 'self' https://accounts.google.com https://connect.facebook.net https://graph.facebook.com https://www.facebook.com; frame-src https://accounts.google.com https://www.google.com https://www.facebook.com https://web.facebook.com; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'\"\n"
    '\t\t\tX-Content-Type-Options "nosniff"\n'
    '\t\t\tReferrer-Policy "strict-origin-when-cross-origin"\n'
    '\t\t\tX-Frame-Options "DENY"\n'
    '\t\t}\n'
)
antigos.append('\thandle {\n' + root + cache_e_spa)
novo = '\thandle {\n' + root + seguranca + cache_e_spa
if block.count(novo) == 1:
    raise SystemExit(0)
achados = [antigo for antigo in antigos if block.count(antigo) == 1]
if len(achados) != 1:
    raise SystemExit('ERRO: bloco do Console diferente do esperado; nenhum ajuste aplicado.')
# Preserva o inode do arquivo que já está montado no container.
with path.open('r+') as arquivo:
    arquivo.seek(0)
    arquivo.write(text.replace(block, block.replace(achados[0], novo), 1))
    arquivo.truncate()
PY
if ! docker exec shared-caddy caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1; then
  cp "$BACKUP" "$CONFIG"
  echo 'ERRO: configuração inválida; backup restaurado.' >&2
  exit 1
fi
if ! docker exec shared-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1; then
  cp "$BACKUP" "$CONFIG"
  docker exec shared-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile >/dev/null 2>&1
  echo 'ERRO: recarga falhou; backup restaurado.' >&2
  exit 1
fi
echo '==> Caddy validado e recarregado: assets imutáveis, HTML no-store, 404 para arquivo inexistente e cabeçalhos de segurança.'
REMOTE
