#!/usr/bin/env bash
# regen-index.sh — regenera docs/tasks/_index.yaml a partir das pastas físicas.

set -e
cd "$(git rev-parse --show-toplevel)"

TASKS_DIR="docs/tasks"
INDEX="$TASKS_DIR/_index.yaml"
NOW=$(python3 -c "from datetime import datetime, timezone; print(datetime.now(timezone.utc).isoformat())")

COUNT_BACKLOG=$(find "$TASKS_DIR/backlog" -maxdepth 1 -name "*.yaml" -not -name ".gitkeep" 2>/dev/null | wc -l)
COUNT_INPROGRESS=$(find "$TASKS_DIR/in-progress" -maxdepth 1 -name "*.yaml" -not -name ".gitkeep" 2>/dev/null | wc -l)
COUNT_DONE=$(find "$TASKS_DIR/done" -maxdepth 1 -name "*.yaml" -not -name ".gitkeep" 2>/dev/null | wc -l)
COUNT_BLOCKED=$(find "$TASKS_DIR/blocked" -maxdepth 1 -name "*.yaml" -not -name ".gitkeep" 2>/dev/null | wc -l)
TOTAL=$((COUNT_BACKLOG + COUNT_INPROGRESS + COUNT_DONE + COUNT_BLOCKED))

{
  echo "# _index.yaml — Tasks EasyStok (DERIVADO de tasks/*/*.yaml)"
  echo "# REGENERADO em $NOW. NÃO editar manualmente — re-rode regen-index.sh"
  echo ""
  echo "schema_version: 1"
  echo "regenerated_at: $NOW"
  echo "total_tasks: $TOTAL"
  echo "by_status:"
  echo "  backlog: $COUNT_BACKLOG"
  echo "  in_progress: $COUNT_INPROGRESS"
  echo "  done: $COUNT_DONE"
  echo "  blocked: $COUNT_BLOCKED"
  echo ""
  echo "tasks:"
} > "$INDEX"

# Parse cada YAML extrai campos minimos
python3 - "$TASKS_DIR" >> "$INDEX" <<'PY'
import sys, os, yaml, re
root = sys.argv[1]
folder_to_status = {'backlog':'backlog','in-progress':'in_progress','done':'done','blocked':'blocked'}
for folder in ['backlog','in-progress','done','blocked']:
    d = os.path.join(root, folder)
    if not os.path.isdir(d): continue
    for f in sorted(os.listdir(d)):
        if not f.endswith('.yaml') or f == '.gitkeep': continue
        try:
            with open(os.path.join(d,f), 'r', encoding='utf-8') as fh:
                t = yaml.safe_load(fh) or {}
        except Exception as e:
            print(f"# parse error: {f}: {e}")
            continue
        tid = t.get('id', f.replace('.yaml',''))
        title = t.get('title','').replace('"','\\"')
        prio = t.get('priority','P3')
        mod = t.get('module','core')
        est = t.get('estimate_hours',0)
        deps = t.get('depends_on',[]) or []
        blocks = t.get('blocks',[]) or []
        print(f"  - id: {tid}")
        print(f"    status: {folder_to_status[folder]}")
        print(f"    priority: {prio}")
        print(f"    module: {mod}")
        print(f"    title: \"{title}\"")
        print(f"    estimate_hours: {est}")
        print(f"    depends_on: {deps}")
        print(f"    blocks: {blocks}")
PY

echo "✅ _index.yaml regenerado: $TOTAL tasks ($COUNT_BACKLOG backlog, $COUNT_INPROGRESS in-progress, $COUNT_DONE done, $COUNT_BLOCKED blocked)"
