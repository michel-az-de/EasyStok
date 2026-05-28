#!/usr/bin/env bash
# claim.sh ETK-NNNN — claim atômico: cria worktree + branch + lock + move pra in-progress
#
# Uso:
#   ./scripts/tasks/claim.sh ETK-0042
#   CLAUDE_AGENT_ID=claude-opus-4.7 ./scripts/tasks/claim.sh ETK-0042
#
# Falha se: task não existe, já tem lock, push rejeitado (race), worktree path ocupado.

set -e
cd "$(git rev-parse --show-toplevel)"

TASK_ID="${1:?uso: claim.sh ETK-NNNN}"
AGENT="${CLAUDE_AGENT_ID:-claude-opus-4.7}"
SESSION="${CLAUDE_SESSION_ID:-sess_$(date +%s)}"

REPO_ROOT="$(git rev-parse --show-toplevel)"
LOCK_DIR="docs/tasks/locks"
BACKLOG_DIR="docs/tasks/backlog"
INPROGRESS_DIR="docs/tasks/in-progress"
LOCK_FILE="$LOCK_DIR/$TASK_ID.lock"
TASK_FILE=$(find "$BACKLOG_DIR" -maxdepth 1 -name "$TASK_ID*.yaml" -type f | head -1)

# Validações
[ -z "$TASK_FILE" ] && { echo "❌ task $TASK_ID não encontrada em $BACKLOG_DIR/"; exit 2; }
[ -f "$LOCK_FILE" ] && {
  EXISTING=$(grep "^agent:" "$LOCK_FILE" | cut -d':' -f2- | xargs)
  echo "❌ task $TASK_ID já tem lock (agent: $EXISTING)"
  exit 3
}

# Sync com origin
git fetch origin master --quiet || { echo "❌ git fetch falhou"; exit 4; }

NOW=$(python3 -c "from datetime import datetime, timezone; print(datetime.now(timezone.utc).isoformat())")
EXPIRES=$(python3 -c "from datetime import datetime, timezone, timedelta; print((datetime.now(timezone.utc) + timedelta(hours=3)).isoformat())")

SLUG=$(basename "$TASK_FILE" .yaml | sed "s/^$TASK_ID-//")
BRANCH="feat/${TASK_ID,,}-${SLUG}"          # bash 4+ lowercase
WORKTREE_PATH=".claude/worktrees/wt-${TASK_ID,,}"
TASK_BASENAME=$(basename "$TASK_FILE")
TASK_FILE_NEW="$INPROGRESS_DIR/$TASK_BASENAME"

# Cria worktree atomicamente (cria branch + workdir num passo só)
if [ -d "$WORKTREE_PATH" ]; then
  echo "❌ worktree $WORKTREE_PATH já existe"
  exit 5
fi

git worktree add "$WORKTREE_PATH" -b "$BRANCH" origin/master || {
  echo "❌ git worktree add falhou"
  exit 6
}

# Cria lock no checkout principal
cat > "$LOCK_FILE" <<EOF
task_id: $TASK_ID
agent: $AGENT
session_id: $SESSION
worktree_path: $WORKTREE_PATH
branch: $BRANCH
claimed_at: $NOW
expires_at: $EXPIRES
heartbeat_at: $NOW
EOF

# Move task backlog → in-progress (no checkout principal)
git mv "$TASK_FILE" "$TASK_FILE_NEW"
git add "$LOCK_FILE"

# Commit no master local
git commit --quiet -m "claim($TASK_ID): $AGENT"

echo ""
echo "✅ CLAIMED: $TASK_ID"
echo ""
echo "   Agent:    $AGENT"
echo "   Branch:   $BRANCH"
echo "   Worktree: $REPO_ROOT/$WORKTREE_PATH"
echo "   Expires:  $EXPIRES"
echo ""
echo "Próximos passos:"
echo "  1. cd $WORKTREE_PATH"
echo "  2. ler task: cat ../$TASK_FILE_NEW"
echo "  3. TDD: red → green → refactor (commits separados)"
echo "  4. heartbeat a cada ~20min: ./scripts/tasks/heartbeat.sh $TASK_ID"
echo "  5. ao terminar:              ./scripts/tasks/complete.sh $TASK_ID"
