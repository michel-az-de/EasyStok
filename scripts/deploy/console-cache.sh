#!/usr/bin/env bash
# Mantém a política de atualização somente no host do Console, sem reiniciar o Caddy.
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
anchor = '\thandle {\n\t\troot * /srv/docs/easystok-console\n'
replacement = '\thandle {\n\t\theader Cache-Control "no-store, max-age=0"\n\t\troot * /srv/docs/easystok-console\n'
if block.count(replacement) != 1:
    if block.count(anchor) != 1:
        raise SystemExit('ERRO: bloco do Console diferente do esperado; nenhum ajuste aplicado.')
    # Preserva o inode do arquivo que já está montado no container.
    path.write_text(text.replace(block, block.replace(anchor, replacement), 1))
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
echo '==> Caddy validado e recarregado: no-store somente nos arquivos do Console.'
REMOTE
