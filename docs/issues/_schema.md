# Issues EasyStok — schema YAML canônico

Sinalização padrão de problemas durante **desenvolvimento, revisão, merge, produção** ou auto-detect do sistema.

Mesmo padrão de pasta-canonical das tasks. Resolver = `git mv` de `open/` pra `resolved/`.

```
docs/issues/
├── _schema.md       ← este arquivo
├── open/            ← abertas (status='open')
│   └── ISSUE-YYYYMMDD-slug.yaml
└── resolved/        ← resolvidas (status='resolved')
    └── ISSUE-YYYYMMDD-slug.yaml
```

## Esquema

```yaml
id: ISSUE-20260524-mongo-vo-serializer-nre
title: "VO serializers do Mongo lançam NRE em prod"
severity: P0-blocker | P1-major | P2-minor | P3-trivial
found_during: development | review | merge | production | system-check
found_by: claude-opus-4.7 | felipe
found_in_task: ETK-0042                       # opcional, FK pra task
timestamp: 2026-05-24T20:30:00Z
affects_files:
  - "EasyStock.Infra.Mongodb/Serializers/DinheiroSerializer.cs"
description: |
  Texto livre em pt-BR. O que aconteceu, qual o impacto, por que importa.
  Inclua mensagem de erro / stack trace quando relevante.
repro_steps:
  - "Rodar Api.IntegrationTests com Database:Provider=Mongo"
  - "Console: 'NullReferenceException at Dinheiro.Serializer.Read'"
resolution: ""                                 # vazio até resolver
resolved_by: ""
resolved_at: ""
status: open | resolved | wontfix
```

## Severity

| Tag | Quando usar |
|---|---|
| `P0-blocker` | Bloqueia merge. Tudo para até resolver. |
| `P1-major` | Quebra função importante. Prioridade alta mas não bloqueia merge. |
| `P2-minor` | Bug em borda, edge case. Pode mergear, agendar fix. |
| `P3-trivial` | Cosmético, log, typo. Não bloqueia nada. |

## Found during

| Tag | Trigger |
|---|---|
| `development` | Bug em código alheio fora do escopo da task atual |
| `review` | Problema no PR de outro agente, vira follow-up |
| `merge` | Regressão, conflito sério, anomalia de estado |
| `production` | Bug capturado em produção |
| `system-check` | Auto-detect (SYS-DUP, SYS-ORPHAN-LOCK, etc.) |

## Auto-detect

O parser do dashboard gera issues `SYS-*` automaticamente:
- `SYS-DUP-<task-id>` — task aparece em pastas múltiplas (P0)
- `SYS-ORPHAN-LOCK-<task-id>` — lock sem task em in-progress (P1)
- `SYS-STALE-LOCK-<task-id>` — lock sem heartbeat > 6h (P1)

Essas somem sozinhas quando o estado é corrigido.

## Como resolver manual

```bash
# 1. Editar issue YAML em open/
# Preencher resolution, resolved_by, resolved_at, status: resolved

# 2. Mover
git mv docs/issues/open/ISSUE-*.yaml docs/issues/resolved/

# 3. Commit
git commit -m "fix(ISSUE-XXX): <descrição>"
```

Dashboard atualiza em ~300ms via SSE.
