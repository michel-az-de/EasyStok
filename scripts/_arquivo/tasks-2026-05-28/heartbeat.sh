#!/usr/bin/env bash
# heartbeat.sh ETK-NNNN [--extend Nh] — atualiza heartbeat do lock
#
# Uso:
#   ./scripts/tasks/heartbeat.sh ETK-0042                # heartbeat normal
#   ./scripts/tasks/heartbeat.sh ETK-0042 --extend 2h    # estende expires_at

set -e
cd "$(git rev-parse --show-toplevel)"

TASK_ID="${1:?uso: heartbeat.sh ETK-NNNN [--extend Nh]}"
LOCK_FILE="docs/tasks/locks/$TASK_ID.lock"

[ -f "$LOCK_FILE" ] || { echo "❌ lock $TASK_ID não encontrado"; exit 1; }

NOW=$(python3 -c "from datetime import datetime, timezone; print(datetime.now(timezone.utc).isoformat())")

# Atualiza heartbeat_at
python3 - "$LOCK_FILE" "$NOW" <<'PY'
import sys, re
path, now = sys.argv[1], sys.argv[2]
content = open(path, encoding='utf-8').read()
content = re.sub(r'^heartbeat_at:.*$', f'heartbeat_at: {now}', content, count=1, flags=re.MULTILINE)
open(path, 'w', encoding='utf-8').write(content)
PY

# --extend
if [ "$2" = "--extend" ] && [ -n "$3" ]; then
  HOURS=$(echo "$3" | sed 's/h$//')
  NEW_EXPIRES=$(python3 -c "from datetime import datetime, timezone, timedelta; print((datetime.now(timezone.utc) + timedelta(hours=$HOURS)).isoformat())")
  python3 - "$LOCK_FILE" "$NEW_EXPIRES" <<'PY'
import sys, re
path, exp = sys.argv[1], sys.argv[2]
content = open(path, encoding='utf-8').read()
content = re.sub(r'^expires_at:.*$', f'expires_at: {exp}', content, count=1, flags=re.MULTILINE)
open(path, 'w', encoding='utf-8').write(content)
PY
  echo "✅ heartbeat $TASK_ID @ $NOW (expires: +$3)"
else
  echo "✅ heartbeat $TASK_ID @ $NOW"
fi
