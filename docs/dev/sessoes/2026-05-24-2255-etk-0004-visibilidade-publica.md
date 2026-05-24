# Sessao ETK-0004 — Visibilidade publica (ROADMAP + CHANGELOG + FEATURES)

Data: 2026-05-24 22:26 → 22:55 (UTC-3)  
Worktree: `.claude/worktrees/wt-etk-0004/` (branch `feat/etk-0004-roadmap-publicado`)  
Identidade Git: felipe.azevedo@gmail.com / michel-az-de  
Status final: completo (aguardando merge do PR)

## Contexto

Felipe pediu hardening de gestao: "melhorar documentacao, tasks, politicas,
features; evitar prejuizos, retrabalho, branches perdidas, redundancia,
perda de codigo; endurecer regras de desenvolvimento; mapear tasks/bugs feitos".

Diagnostico inicial mostrou que **o esqueleto de gestao ja existia**:
- Sistema ETK-NNNN (ADR-0020, 25 tasks bootstrap, PR #226)
- 21 ADRs em docs/adr/
- docs/dev/incidentes/ + sessoes/ + flaky-tests.md
- docs/plan/ rico (8 docs Caixa V2 + Rotulagem P-02)
- .knowledge/ como single source of truth tecnica
- AGENTS.md + CLAUDE.md v2.1 + politica protocolo

**O gap real**: 10 branches `feat/task-ez-*` ativas (storefront, OTP — trabalho
da ultima semana) que **nao foram catalogadas no sistema ETK**, usam naming
antigo (`TASK-EZ-NNN` em vez de `ETK-NNNN`), worktrees fora do padrao
(`ez003` vs `wt-etk-NNNN`), nenhum lock/heartbeat, nenhum YAML. Sistema
formal criado em 2026-05-24, mas trabalho real em paralelo nao migrou.

Felipe escolheu (via 4-opcoes): **frente "Inventario + ROADMAP + CHANGELOG"**
como primeira pancada de gestao. Esta sessao executa essa frente como umbrella
da task formal ETK-0004 (originalmente so ROADMAP).

## O que foi feito

3 artefatos publicos de visibilidade entregues em 1 PR (umbrella ETK-0004):

1. **`ROADMAP.md`** na raiz (154 linhas)
   - Estado atual snapshot 2026-05-24
   - Como o trabalho e organizado (link ADR-0020)
   - Proximas entregas: Etapas 1-5+ (Marco zero, Defesas estruturais,
     Triagem PRs, ROADMAP, Rotulagem P-02, Caixa V2 deferred)
   - Estabilizacao continua (10 itens de stability-roadmap)
   - Marcos de longo prazo (NF-e, marketplaces, variantes, multi-empresa)
   - Como acompanhar (dashboard, _index.yaml, ADRs, handoffs)

2. **`docs/CHANGELOG.md`** (274 linhas)
   - Formato Keep a Changelog adaptado para Conventional Commits
   - 158 PRs squashed agrupados por mes (Maio dominante: 153 PRs)
   - Highlights mensais (sistema ETK, Compras E2E, Financeiro F1-F14,
     NF-e fundacao, Estoque hardening, Security CVEs, Deploy Fly auto,
     PWA Casa da Baba ondas, Mobile MAUI F0-F4c, Helpdesk E2E,
     Admin redesign, etc.)
   - Listagem por tipo (feat / fix / perf / test / arch / refactor /
     chore / ci / docs)
   - Tabela de eventos infra/decisao sem PR (descomissionamentos, ADRs)

3. **`docs/FEATURES.md`** (391 linhas)
   - Mapa publico por modulo de negocio (22 modulos)
   - Status por modulo: ✅ Pronto / 🟡 Parcial / 📋 Planejado / ❌ Nao
     iniciado / ⛔ Diferido
   - Cobre: Identidade & Auth, Multi-tenant, Catalogo & Estoque, Pedido
     & Venda, Compras, Caixa (V1+V2 deferred), Financeiro, Billing,
     Pagamentos, NF-e, Rotulagem (Etapa 5), Etiquetas, Notificacoes,
     Helpdesk, Mobile MAUI+PWA, KDS, IA, Admin, Auditoria & LGPD,
     Onboarding, Webhooks, Infra
   - **Nao duplica .knowledge/** — aponta pra `current-state.md` e
     `domain-glossary.md` para detalhes tecnicos

## O que ficou pendente (proximas frentes possiveis)

1. **Auditoria + retrofit das 10 branches orfas** (a alternativa nao escolhida
   na primeira pancada): mapear cada `feat/task-ez-*` → criar/casar com
   ETK-NNNN no backlog, identificar duplicacoes com ETK-0006-0008 (caixa) e
   ETK-0016-0017 (rotulagem), gerar issues SYS-ORPHAN-*. **Risco real**: pode
   haver duplicacao entre essas branches e o bootstrap de tasks.

2. **Endurecer entrada** (ETK-0019 Husky pre-commit + ETK-0020 CI billing +
   branch naming enforcement). Trava o problema na entrada antes que aconteca
   de novo.

3. **Bug `scripts/tasks/claim.sh` Windows nativo** (descoberto nesta sessao):
   depende de `bash` 5+ e `python3` (UTC isoformat). Em Windows nativo nao
   roda. Replicado manualmente em PowerShell nesta sessao. **Sugestao**:
   - Opcao A: portar pra PowerShell paralelo (`scripts/tasks/claim.ps1`)
   - Opcao B: documentar dependencia (WSL ou Git Bash + Python) em
     `docs/tasks/README.md`
   - Opcao C: criar wrapper `.cmd` que invoca WSL por baixo
   - Vira issue: ISSUE-20260524-claim-sh-windows-compat.yaml (P2, infra/meta)

4. **`docs/dev/incidentes/` sem indice por area/severidade/modulo**. Hoje
   tem 4 incidentes em pasta plana. Util criar `_index.md` ou frontmatter
   YAML em cada um.

5. **Sem `docs/dev/incidentes/_schema.md`** equivalente ao de tasks/issues.
   Cada incidente esta solto. Padronizar.

6. **Tag `v1.0.0` ainda nao criada** (ETK-0001 marco zero pendente). Sem isso
   o CHANGELOG comeca como `[Unreleased]` indefinidamente.

## Decisoes tomadas

1. **Escopo expandido**: ETK-0004 absorveu CHANGELOG + FEATURES alem do
   ROADMAP original. Justificativa registrada no `context:` do YAML.
   Felipe aprovou na sessao via AskUserQuestion.

2. **1 PR umbrella em vez de 3 PRs separados** (`feat/etk-0004-roadmap-publicado`).
   Justificativa: doc-only, baixo risco, menos churn. ADR-0020 e flexivel.

3. **Naming dos artefatos publicos** seguindo Felipe-style (PT-BR direto,
   sem floreio): `ROADMAP.md`, `CHANGELOG.md`, `FEATURES.md` em vez de
   versoes mais formais tipo `PROJECT_ROADMAP.md`.

4. **Inventario nao foi criado como `docs/inventario/`** (estrutura), virou
   `docs/FEATURES.md` (1 pagina). Motivo: `.knowledge/` ja tem o inventario
   tecnico completo (current-state.md, domain-glossary.md, architecture.md).
   Duplicar seria contraproducente.

5. **Commit do claim feito manualmente** (replicando claim.sh) porque o
   script depende de bash+python3 nao disponiveis em Windows nativo.
   Documentado como pendencia #3 acima.

## Commits criados

Em master local (3 commits):
- `325b2931` `claim(ETK-0004): claude-opus-4.7`
- `a8f02d6d` `chore(ETK-0004): atualiza in-progress com resolucao + regen index`
- `595f90ae` `done(ETK-0004): claude-opus-4.7 @ 2026-05-24T22:55:00+00:00`

Na branch `feat/etk-0004-roadmap-publicado` (1 commit):
- `6521d27a` `feat(ETK-0004): ROADMAP publico + CHANGELOG + FEATURES`
  - 3 arquivos, 819 insercoes

E mais 1 commit deste handoff:
- `<sha>` `docs(sessao): handoff ETK-0004 visibilidade publica`

Hook Husky `rotulagem-architecture-tests` rodou e passou em todos
(verde, 1/1 arch test, ~1-2min cada).

**Aviso**: o commit `6521d27a` precisou de `git reset --soft HEAD~1` +
recommit porque o primeiro tentativa pegou mensagem errada de um arquivo
temp pre-existente. Resolvido sem perda — reset --soft preserva working
tree + index.

## Branches criadas/deletadas

Criadas:
- `feat/etk-0004-roadmap-publicado` (worktree `.claude/worktrees/wt-etk-0004/`)

Deletadas:
- Nenhuma (cleanup do worktree wt-etk-0004 fica para apos merge do PR)

## Estado de worktrees (snapshot)

```
C:/easy/EasyStok                                         feat/task-ez-003-entity-frete-zona (sessao anterior, limpa)
C:/easy/EasyStok/.claude/worktrees/brave-shannon-078b0e  dev/brave-shannon-078b0e (sessao merge PRs antiga)
C:/easy/EasyStok/.claude/worktrees/wt-ez-auth-001        feat/task-ez-auth-001-solicitar-otp (orfa do sistema)
C:/easy/EasyStok/.claude/worktrees/wt-etk-0004           feat/etk-0004-roadmap-publicado (esta sessao)
C:/easy/EasyStok/.claude/worktrees/wt-tasks-bootstrap    master (usado para claim/done)
C:/easy/EasyStok-ez004                                   feat/task-ez-004-entities-janela-vaga (orfa)
C:/easy/EasyStok-ez007                                   feat/task-ez-007-entities-avaliacao-feedback (orfa)
```

5 worktrees + 2 paths externos. **6 branches `feat/task-ez-*` orfas** do
sistema ETK aguardando triagem (pendencia #1 acima).

## Proxima acao recomendada

1. **Push master local** (3 commits: claim + chore + done) — exige
   autorizacao explicita (R9).
2. **Push branch `feat/etk-0004-roadmap-publicado`** — exige autorizacao
   explicita (R9).
3. **Abrir PR via `gh pr create`** com summary apontando para os 3 artefatos.
4. **Merge via `gh pr merge --admin --squash --delete-branch`** apos revisao
   do Felipe.
5. **Cleanup worktree wt-etk-0004** (`git worktree remove`).
6. **Escolher proxima frente** entre as 5 pendencias listadas acima.
   Recomendacao: auditoria das branches orfas (pendencia #1) — fecha o
   gap mais agudo identificado no diagnostico inicial.

## Referencias

- Task YAML: [docs/tasks/done/ETK-0004-roadmap-publicado.yaml](../../tasks/done/ETK-0004-roadmap-publicado.yaml)
- ADR-0020 (sistema ETK): [docs/adr/0020-tdd-tasks-numeradas-multitarefa.md](../../adr/0020-tdd-tasks-numeradas-multitarefa.md)
- ADR-0021 (Etapa 5 Rotulagem): [docs/adr/0021-rotulagem-p02-etapa5-do-roadmap.md](../../adr/0021-rotulagem-p02-etapa5-do-roadmap.md)
- Plano Rotulagem: [docs/plan/p-02-rotulagem-nutricional.md](../../plan/p-02-rotulagem-nutricional.md)
- Plano Caixa V2 (deferred): [docs/plan/00-reconhecimento.md](../../plan/00-reconhecimento.md)
- Single source tecnica: [.knowledge/current-state.md](../../../.knowledge/current-state.md)
- Stability roadmap: [.knowledge/stability-roadmap.md](../../../.knowledge/stability-roadmap.md)
- PR (a abrir): https://github.com/michel-az-de/EasyStok/pull/<TBD>
