#!/usr/bin/env bash
# complete.sh ETK-NNNN — finaliza task: gates + move pra done + cleanup worktree + handoff
#
# Pré-requisitos:
# - quality_gates da task YAML executados com sucesso (manual antes de chamar)
# - commits TDD feitos (red/green/refactor se methodology=tdd)
# - handoff já escrito em docs/dev/sessoes/

set -e
cd "$(git rev-parse --show-toplevel)"

TASK_ID="${1:?uso: complete.sh ETK-NNNN}"
LOCK_FILE="docs/tasks/locks/$TASK_ID.lock"
INPROGRESS_DIR="docs/tasks/in-progress"
DONE_DIR="docs/tasks/done"

[ -f "$LOCK_FILE" ] || { echo "❌ lock $TASK_ID não encontrado"; exit 1; }

TASK_FILE=$(find "$INPROGRESS_DIR" -maxdepth 1 -name "$TASK_ID*.yaml" -type f | head -1)
[ -z "$TASK_FILE" ] && { echo "❌ task $TASK_ID não está em in-progress/"; exit 2; }

WORKTREE=$(grep "^worktree_path:" "$LOCK_FILE" | cut -d':' -f2- | xargs)
BRANCH=$(grep "^branch:" "$LOCK_FILE" | cut -d':' -f2- | xargs)
AGENT=$(grep "^agent:" "$LOCK_FILE" | cut -d':' -f2- | xargs)

# Confirma
echo "Você executou TODOS os quality gates? (build + test + format)"
echo "Você escreveu o handoff em docs/dev/sessoes/?"
echo ""
read -r -p "Continuar com complete? (sim/não): " CONFIRM
[ "$CONFIRM" = "sim" ] || { echo "abortado"; exit 3; }

NOW=$(python3 -c "from datetime import datetime, timezone; print(datetime.now(timezone.utc).isoformat())")
TASK_BASENAME=$(basename "$TASK_FILE")
TASK_FILE_DONE="$DONE_DIR/$TASK_BASENAME"

# Move in-progress → done
git mv "$TASK_FILE" "$TASK_FILE_DONE"

# Remove lock
git rm "$LOCK_FILE"

# Commit no master local
git commit --quiet -m "done($TASK_ID): $AGENT @ $NOW"

# Push branch da task (worktree) pra origin
if [ -d "$WORKTREE" ]; then
  cd "$WORKTREE"
  git push -u origin "$BRANCH" --quiet || {
    echo "⚠️  push da branch $BRANCH falhou. Faça manualmente:"
    echo "    cd $WORKTREE && git push -u origin $BRANCH"
  }
  cd "$(git -C "$WORKTREE" rev-parse --show-toplevel)" 2>/dev/null || cd -
fi

cd "$(git rev-parse --show-toplevel)"

# Push master local pra origin
git push origin master --quiet

echo ""
echo "✅ COMPLETED: $TASK_ID"
echo ""
echo "   Movido para: $TASK_FILE_DONE"
echo "   Branch:      $BRANCH (pushed)"
echo "   Worktree:    $WORKTREE (mantido — pode remover com:)"
echo "     git worktree remove $WORKTREE"
echo ""
echo "Próximo passo: abra PR via:"
echo "   gh pr create --base master --head $BRANCH --title 'feat($TASK_ID): ...' --body-file <handoff>"
