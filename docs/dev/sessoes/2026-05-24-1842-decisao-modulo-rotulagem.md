# Sessao decisao-modulo-rotulagem (ETK-0005)

Data: 2026-05-24 18:42
Worktree: .claude/worktrees/wt-etk-0005 (aninhado em wt-tasks-bootstrap)
Branch: feat/etk-0005-decisao-modulo-novo
Identidade Git: felipe.azevedo@gmail.com / gh: michel-az-de
Status final: completo

## Contexto

CLAUDE.md §6 Etapa 5 do ROADMAP tinha pendencia: *"Modulo novo (Caixa Conciliado
V2 OU Rotulagem P-02). Decisao pendente, depende de validacao premissas."*

A task [ETK-0005](../../tasks/done/ETK-0005-decisao-modulo-novo.yaml)
materializa essa decisao, gerada como uma das 25 tasks bootstrap do commit #226
(sistema ETK-NNNN — ver [ADR-0020](../../adr/0020-tdd-tasks-numeradas-multitarefa.md)).

## O que foi feito

1. **Inventario inicial CLAUDE.md §0** rodado.
   - Branch principal: feat/task-ez-003-entity-frete-zona (paralelo, nao
     toquei — ver §pendencias)
   - Master local: 0/0 ahead/behind origin (78434ea0)
   - Working tree: limpo
   - 5 worktrees ativos
   - gh: michel-az-de
   - Build: verde (0 erros, 8 warnings)

2. **Leitura de contexto:**
   - ADRs 0011 (nomenclatura PT-BR), 0015 (SessaoCaixa entity), 0020 (sistema
     ETK), 0014/0016/0017 (Caixa V2)
   - docs/plan/README.md (plano Caixa V2 consolidado, 9 docs, 5-8 semanas)
   - docs/plan/p-02-rotulagem-nutricional.md (plano Rotulagem, 10-12 semanas,
     MVP 2026-08-09)
   - docs/tasks/_index.yaml (25 tasks ETK; 7 Caixa P1, 2 Rotulagem P2)

3. **Sessao Q&A com Felipe (8 perguntas em 2 rodadas)**: convergencia clara
   para Rotulagem P-02. Criterios decisivos:
   - Dor real hoje: rotulos irregulares circulando = risco multa Anvisa
     R$6k-1,5M/unidade (vs Caixa = friccao operacional sem multa pendente)
   - Estrategia: diferencial competitivo defensavel (IA + Anvisa compliance)
   - Bloqueadores externos da Rotulagem: contornaveis em paralelo com F0
   - Pre-requisitos ROADMAP (Etapas 1-4): tratar depois da decisao

4. **Claim atomico ETK-0005** via scripts/tasks/claim.sh.
   - Branch feat/etk-0005-decisao-modulo-novo criada de origin/master
   - Worktree em .claude/worktrees/wt-etk-0005 (NOTA: aninhado dentro de
     wt-tasks-bootstrap — efeito colateral de rodar claim.sh de dentro do
     worktree wt-tasks-bootstrap em vez do principal C:/easy/EasyStok que
     esta em outra branch). Funcional, mas estetico.
   - Lock criado + ETK-0005 movido para in-progress/ + commit `claim(ETK-0005)`
     em master local.

5. **Trabalho na branch feat/etk-0005:**
   - Criado `docs/adr/0021-rotulagem-p02-etapa5-do-roadmap.md` (modelo
     ADRs 0018-0019)
   - ETK-0016 e ETK-0017 sobem P2 → P1 (yaml updates: depends_on, blocks,
     context enriquecido)
   - CLAUDE.md §6 atualizado: Etapa 5 = Rotulagem P-02 (ADR-0021);
     Etapa 6+ = Caixa V2 (diferido). Removida linha "Decisao pendente".
   - _index.yaml regenerado

6. **Trabalho em master local (via wt-tasks-bootstrap):**
   - ETK-0005 yaml em in-progress/ atualizado: status=in_progress,
     acceptance_criteria com check de FEITO, outcome documentado
   - _index.yaml regenerado (1 in-progress, 24 backlog)

## O que ficou pendente

1. **Materializacao de tasks Rotulagem alem de ETK-0016/0017**: Felipe
   escolheu execucao minimalista. Proximas tasks (F0 setup, PerfilNutricional,
   IConformidadeValidator, etc.) serao criadas conforme avanca a partir da
   proxima sessao.

2. **Bloqueadores externos a destravar em paralelo:**
   - Contador da Casa da Baba (F0: validar layout PDF + F6: revisar PDF real)
   - Politica de privacidade do QR publico (decisao legal antes de F+1)

3. **Caixa V2 diferido**: ETK-0006..0012 + ETK-0023 permanecem P1 no backlog
   sem trabalho ativo. Retomar apos F2 da Rotulagem.

4. **Pre-requisitos ROADMAP**: ETK-0001 (deploy v1.0), ETK-0002 (defesas
   estruturais), ETK-0020 (CI billing) ficaram em backlog. Felipe optou por
   tratar depois.

5. **Worktree aninhado wt-etk-0005**: efeito colateral de rodar claim.sh
   do wt-tasks-bootstrap (porque o working tree principal C:/easy/EasyStok
   estava em outra branch feat/task-ez-003). Funcional. Limpeza opcional em
   sessao futura via `git worktree move`.

6. **task-ez-003/004/007 worktrees em paralelo**: Felipe confirmou que sao
   trabalho em andamento, nao tocar.

## Decisoes tomadas

1. **Rotulagem P-02 vence Caixa Conciliado V2** para Etapa 5 do ROADMAP.
   Registrada em ADR-0021.
2. **Caixa V2 diferido para Etapa 6+** (sem cronograma fixo). Retomar apos
   F2 da Rotulagem (~6 semanas).
3. **ETK-0016 e ETK-0017 sobem P2 → P1** como entry tasks do modulo.
4. **Execucao minimalista**: nao materializei 5-10 tasks adicionais agora;
   crio conforme avanca a partir da proxima sessao.
5. **Caminho formal** (vs informal/master direto): claim ETK-0005 + branch
   feat + PR + gh pr merge --admin --squash --delete-branch.

## Commits criados

**Em master local (via wt-tasks-bootstrap, branch master):**
- `43440b5d claim(ETK-0005): claude-opus-4.7` (pelo claim.sh)
- A criar antes do complete.sh: `chore(ETK-0005): atualiza task in-progress
  com resolucao + regen index`
- A criar pelo complete.sh: `done(ETK-0005): claude-opus-4.7 @ <timestamp>`

**Em branch feat/etk-0005-decisao-modulo-novo (via wt-etk-0005):**
- A criar: `docs(ETK-0005): ADR-0021 + reprioriza ETK-0016/0017 + CLAUDE.md §6`

## Branches criadas/deletadas

- Criada: `feat/etk-0005-decisao-modulo-novo` (origem origin/master)
- Apos merge: branch deletada via `gh pr merge --delete-branch`

## Proxima acao recomendada

1. **Felipe: destravar bloqueadores externos em paralelo**
   - Marcar reuniao com contador da Casa da Baba para validar layout PDF (F0)
   - Decidir politica de privacidade da pagina publica /caixa/verificar (F+1)

2. **Proxima sessao Claude:**
   - validate.sh
   - cat docs/tasks/_index.yaml | grep -A 4 "ETK-0016"
   - ./scripts/tasks/claim.sh ETK-0016
   - cd .claude/worktrees/wt-etk-0016/
   - TDD: red (Entity RotuloNutricional tests falhando)
        → green (entity criada, tests verdes)
        → refactor (limpeza)
   - heartbeat a cada 20min
   - complete.sh ETK-0016
   - PR + merge

3. **Apos F2 Rotulagem (~6 semanas)**: reavaliar paralelismo com Caixa V2.

## Referencias

- ADR-0021 (esta decisao): docs/adr/0021-rotulagem-p02-etapa5-do-roadmap.md
- ADR-0020 (sistema ETK + TDD): docs/adr/0020-tdd-tasks-numeradas-multitarefa.md
- ADR-0011 (nomenclatura PT-BR herdada): docs/adr/0011-nomenclatura-pt-br-rotulagem.md
- Plano Rotulagem: docs/plan/p-02-rotulagem-nutricional.md
- Plano Caixa V2 (diferido): docs/plan/README.md
- Task ETK-0005: docs/tasks/done/ETK-0005-decisao-modulo-novo.yaml
- Tasks foco (P1): docs/tasks/backlog/ETK-0016-entity-rotulo-nutricional.yaml,
  ETK-0017-usecase-gerar-rotulo.yaml

## Notas operacionais

- Inventario inicial (R0): conformidade integral.
- R5 (1 sessao por task): conformidade. Lock atomico criado, heartbeat nao
  rodado porque sessao < 20min.
- R8 (build + test pre-commit): build verde validado antes de commit.
- R9 (autorizacoes): Felipe disse "go" autorizando push + PR + merge.
- R14 (handoff): este documento.
- R15 (perguntar em duvida): 8 perguntas estruturadas em 2 rodadas antes de
  qualquer acao.
- R16 (TDD): nao aplicavel — methodology=incremental (justificado no YAML).
