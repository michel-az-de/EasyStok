#!/usr/bin/env bash
# Regenera as partes auto-discovery da knowledge base.
# Uso: bash .knowledge/update.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
KB="$ROOT/.knowledge"
DATE="$(date +%Y-%m-%d)"

cd "$ROOT"

# 1. recent-evolution.md
LAST_COMMITS="$(git log --oneline -10 2>/dev/null || echo 'sem git history')"
TEST_COUNT="$(grep -rE '\[Fact\]|\[Theory\]' --include='*.cs' EasyStock.*.Tests/ 2>/dev/null | wc -l || echo '?')"
BRANCH="$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo '?')"

cat > "$KB/recent-evolution.md" <<EOF
# Recent Evolution

> Auto-gerado por update.sh em $DATE. Edite manualmente as seções de "decisões" se quiser.

## Snapshot $DATE

- Branch: \`$BRANCH\`
- Testes (count aproximado de [Fact]/[Theory]): $TEST_COUNT

### Últimos 10 commits
\`\`\`
$LAST_COMMITS
\`\`\`

### Decisões de arquitetura recentes
_(editar manualmente — script não infere isso)_

### Direção atual
_(editar manualmente)_
EOF

echo "✓ recent-evolution.md regenerado ($DATE)"
echo ""
echo "Lembre de editar manualmente as seções 'Decisões' e 'Direção' se houve mudança."
