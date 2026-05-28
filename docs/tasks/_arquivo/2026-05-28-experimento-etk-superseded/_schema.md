# Schema YAML — Tasks EasyStok

## Estrutura completa

```yaml
id: ETK-0042                                  # único, imutável, sequencial monotônico
title: "Implementar emissão NFe 55 background"
status: backlog                               # derivado da pasta — não editar
priority: P0 | P1 | P2 | P3                   # P0=blocker, P3=trivial
module: nota-fiscal | caixa | mobile | core | pedido | estoque | financeiro | rotulagem | infra
estimate_hours: 2                             # estimativa otimista, sem buffer
methodology: tdd | refactor | incremental     # default: tdd
created_by: claude-opus-4.7 | felipe
created_at: "2026-05-24T20:30:00Z"

depends_on:                                   # IDs de tasks que precisam estar done
  - ETK-0041
blocks:                                       # IDs de tasks bloqueadas por esta
  - ETK-0043

context: |
  Texto livre em pt-BR. O que precisa ser feito, por quê, qual problema resolve.
  Incluir links pra ADRs e planos relevantes (docs/plan/nota-fiscal/00-README.md).

files_to_create:
  - "EasyStock.Application/Fiscal/EmitirNfe/EmitirNfeUseCase.cs"
files_to_modify:
  - "EasyStock.Worker/Program.cs (registrar background service)"

acceptance_criteria:
  - "Use case EmitirNfe enfileira na outbox NfeEvent"
  - "Background service consome outbox e chama IFocusNfeClient.Emitir"
  - "Retry 3x exponencial em falha transiente"
  - "Modelo 55 (NFe), não NFCe modelo 65"

definition_of_done:
  - dotnet_build: pass
  - architecture_tests: pass
  - unit_tests: ">= 8 cenários cobrindo happy + 3-phase tx + retry + idempotency"
  - format: clean
  - manual_test: "Emitir NFe sandbox via /api/storefront/emitir-nfe-teste, conferir XML no Focus"

quality_gates:                                # comandos executáveis pelo complete.sh
  - cmd: "dotnet build EasyStok.sln -warnaserror"
  - cmd: "dotnet test --filter 'FullyQualifiedName~EmitirNfe'"
  - cmd: "dotnet test --filter 'Category=Architecture'"
  - cmd: "dotnet format --verify-no-changes"

phases:                                       # só se methodology=tdd
  red:
    description: "Escrever EmitirNfeUseCaseTests com 8 cenários (todos falhando — entity nem existe)"
    commit_template: "test(ETK-NNNN): red - EmitirNfeUseCaseTests com 8 cenários"
  green:
    description: "Implementar EmitirNfeUseCase + dependências mínimas pra passar"
    commit_template: "feat(ETK-NNNN): green - EmitirNfeUseCase"
  refactor:
    description: "Extrair NfeFactory se útil, limpar"
    commit_template: "refactor(ETK-NNNN): clean up"

verification_commands:                        # comandos pra outro agente verificar trabalho
  - "cd C:/easy/EasyStok && dotnet test --filter 'FullyQualifiedName~EmitirNfe'"

handoff_notes_required: true                  # default: true

# ── Campos só preenchidos ao completar ──

completed_by: claude-opus-4.7                 # quem completou
completed_at: "2026-05-24T22:00:00Z"
branch: feat/etk-0042-emitir-nfe              # branch do PR
pr_url: https://github.com/michel-az-de/EasyStok/pull/250
commits:
  - "abc1234 test(ETK-0042): red - ..."
  - "def5678 feat(ETK-0042): green - ..."
  - "ghi9012 refactor(ETK-0042): ..."
handoff_file: docs/dev/sessoes/2026-05-24-2200-etk-0042-emitir-nfe.md
```

## Campos obrigatórios mínimos

Pra task ser válida, precisa ter pelo menos:
- `id` (`ETK-NNNN`)
- `title` (uma linha)
- `priority` (P0-P3)
- `module` (lista acima)
- `estimate_hours` (number)
- `context` (texto livre, pt-BR)
- `acceptance_criteria` (lista, pelo menos 1)
- `quality_gates` (lista, pelo menos 1 — em geral build + test)

Os outros campos são opcionais mas todos preenchidos = task de boa qualidade.

## Numeração

- **`ETK-0001` em diante**, sequencial monotônico
- ID atribuído na criação — nunca muda mesmo se task é movida entre pastas
- Reservado: ETK-9999 é placeholder pra "criar próxima" em scripts de geração

## Status (derivado da pasta)

| Pasta | Status no YAML |
|---|---|
| `backlog/` | `backlog` |
| `in-progress/` | `in_progress` |
| `done/` | `done` |
| `blocked/` | `blocked` |

`_index.yaml` é regenerado automaticamente lendo a localização física.

## Prioridade

| Tag | Quando usar |
|---|---|
| `P0` | Blocker — sem isso outras tasks travam ou produção quebra |
| `P1` | Alta — escopo do MVP / módulo em curso |
| `P2` | Média — melhoria importante mas não crítica |
| `P3` | Baixa — nice-to-have, débito técnico não-urgente |

## Module

- `core` — Domain, Application, base
- `nota-fiscal` — NFe / NFCe / emissão fiscal
- `caixa` — SessaoCaixa, FechamentoCaixa, MovimentoCaixa
- `mobile` — MAUI / PWA
- `pedido` — Pedido, Venda, ItemVenda
- `estoque` — ItemEstoque, MovimentacaoEstoque
- `financeiro` — Contas a pagar/receber, comprovantes
- `rotulagem` — Rotulagem nutricional P-02
- `infra` — Postgres, Fly.io, CI, deploy
- `meta` — Mudança no sistema multi-agente (este sistema)

## Methodology

- `tdd` (default) — exige 3 commits separados red/green/refactor
- `incremental` — para spike, exploração, prototipo
- `refactor` — pra restruturação sem comportamento novo (ainda exige testes verdes)

## Quality gates obrigatórios mínimos (qualquer task)

Independente do que está na YAML:
- `dotnet build EasyStok.sln --nologo` (verde, 0 erros)
- `dotnet test --filter "Category=Architecture"` (100% pass)
- `dotnet format --verify-no-changes` nos arquivos novos (warning em pré-existentes é tolerado)
- Pra tasks tocando domain: full `dotnet test --filter "Category!=E2E"` regression

Tasks tocando concorrência/transação/outbox/webhook adicionam:
- `dotnet test --filter "Category=Concurrency|Category=Lifecycle"` 100% pass
