# ADR-0043 — Adocao da Policy v4.0 (PR-first, issue-driven)

- Status: Aceito
- Data: 2026-07-09
- Supersede: ADR-0022 (master-first trunk-based)
- Relacionado: ADR-0040 (gate unico pre-commit), ADR-0029 (poka-yoke / worktree-status)

## Contexto

O EasyStok operava sob a policy v3.1 (master-first, trunk-based, ADR-0022): commit direto no master,
"nao exige PR", branch/worktree proibidos por default. A direcao dos repos de `C:\rep` passou a exigir:
**PR sempre** (exceto hotfix urgente autorizado), fluxo alinhado ao GitHub (abrir -> comentar -> fechar issue
para gestao), limpeza de branches/worktrees ao terminar, e historico/memoria para continuidade e zerar retrabalho.

## Decisao

Adotar o **Protocolo Operacional v4.0** (ver `CLAUDE.md`): toda tarefa = issue + branch + PR, com
**auto-merge por tier de risco** (baixo = chore/docs/test/fix-trivial, auto no verde; alto =
feat/refactor/migracao/auth/RLS/policy, aguarda label `aprovado`). Worktrees isolados fora do repo
(`C:\rep\.worktrees\<repo>\<slug>`). Definition of Done com criterio de Aceite verificavel na issue.

O gate mecanico de pre-commit (`scripts/poka-yoke/gate.ps1`, ADR-0040) e preservado como R4/§2, e agora
o CI do PR repete o gate e destrava o auto-merge do tier baixo.

## Consequencias

- Master deixa de receber commit direto (salvo §HOTFIX autorizado, com issue post-hoc).
- Ganha-se auditabilidade (revert granular, trilha, review async), NAO seguranca independente
  (autor=revisor=merger); o gate real de correcao e o tier ALTO + aprovacao humana.
- Identidade de commit passa a `michel.az.de@gmail.com` (email vinculado a conta michel-az-de -> atribui no GitHub).
  Atualiza o R12 (antes felipe.azevedo@gmail.com, que nao linkava a conta).
- Automacao global via `/tarefa-inicio`, `/tarefa-fim`, `/hotfix` + hooks SessionStart/Stop.
- O Dev Janitor pula repos fora do trunk (guard em janitor-scope.ps1) e nao toca `C:\rep\.worktrees`.
- `.claude/settings.json` ganha `permissions.allow` (project-scope) preservando os hooks poka-yoke existentes.

## Alternativas consideradas

- Manter v3.1 trunk-based: rejeitada (nao atende PR-sempre nem a gestao por issue pedida).
- CLAUDE.md global unico para todos os repos: rejeitada (preferencia por policy por-repo com override).
- Auto-merge total sem tier: rejeitada (daria falsa sensacao de gate; alto risco sem aprovacao humana).
