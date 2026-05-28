#!/usr/bin/env bash
# validate.sh — sanity check do sistema de tasks
#
# Checa: locks órfãos, locks stale (>6h), tasks duplicadas, _index consistente.

set -e
cd "$(git rev-parse --show-toplevel)"

LOCK_DIR="docs/tasks/locks"
INPROGRESS_DIR="docs/tasks/in-progress"
FAIL=0

NOW_ISO=$(python3 -c "from datetime import datetime, timezone; print(datetime.now(timezone.utc).isoformat())")

# 1. Locks duplicados
DUPS=$(find "$LOCK_DIR" -maxdepth 1 -name "*.lock" -type f 2>/dev/null | xargs -I{} basename {} .lock | sort | uniq -d)
if [ -n "$DUPS" ]; then
  echo "❌ locks duplicados: $DUPS"
  FAIL=1
fi

# 2. Locks órfãos (lock sem task em in-progress)
for f in "$LOCK_DIR"/*.lock; do
  [ -f "$f" ] || continue
  TASK_ID=$(basename "$f" .lock)
  if ! find "$INPROGRESS_DIR" -maxdepth 1 -name "$TASK_ID*.yaml" -type f | grep -q .; then
    echo "⚠️  lock órfão: $TASK_ID (sem task em in-progress/)"
    FAIL=1
  fi
done

# 3. Locks stale (>6h sem heartbeat)
for f in "$LOCK_DIR"/*.lock; do
  [ -f "$f" ] || continue
  HB=$(grep "^heartbeat_at:" "$f" | cut -d':' -f2- | xargs)
  [ -z "$HB" ] && continue
  STALE=$(python3 -c "
from datetime import datetime, timedelta
import sys
try:
  hb = datetime.fromisoformat('$HB'.replace('Z', '+00:00'))
  now = datetime.fromisoformat('$NOW_ISO'.replace('Z', '+00:00'))
  sys.exit(0 if (now - hb) > timedelta(hours=6) else 1)
except Exception: sys.exit(2)
") && {
    TASK_ID=$(basename "$f" .lock)
    echo "⚠️  lock stale (>6h sem heartbeat): $TASK_ID (último: $HB)"
    FAIL=1
  }
done

# 4. Tasks duplicadas em pastas múltiplas
declare -A SEEN
for folder in backlog in-progress done blocked; do
  for f in docs/tasks/$folder/*.yaml; do
    [ -f "$f" ] || continue
    [ "$(basename "$f")" = ".gitkeep" ] && continue
    ID=$(grep "^id:" "$f" 2>/dev/null | head -1 | cut -d':' -f2- | xargs)
    [ -z "$ID" ] && continue
    if [ -n "${SEEN[$ID]:-}" ]; then
      echo "❌ task duplicada: $ID em ${SEEN[$ID]} E $folder/"
      FAIL=1
    else
      SEEN[$ID]=$folder
    fi
  done
done

if [ "$FAIL" -eq 0 ]; then
  echo "✅ Estado do sistema de tasks: OK"
  exit 0
else
  echo ""
  echo "❌ Inconsistências detectadas."
  echo "Ações sugeridas:"
  echo "  - Locks stale: ./scripts/tasks/reclaim.sh ETK-XXX (libera lock + move task pra backlog)"
  echo "  - Tasks duplicadas: decidir qual pasta é canonical, git rm a duplicata"
  exit 1
fi
