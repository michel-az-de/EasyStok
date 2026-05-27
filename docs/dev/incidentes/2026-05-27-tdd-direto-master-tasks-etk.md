# Drift R1 v2.1: TDD direto em master para tasks ETK (não-violação, decisão a registrar)

**Data:** 2026-05-27
**Tipo:** drift de regra / decisão operacional não-formalizada
**Severidade:** baixa (sem perda de dado, sem corrupção, sem desalinhamento de fluxo)
**Sessão de descoberta:** Fase 1 — auditoria funcional v1.0

## Contexto

Durante a sessão de auditoria Fase 1 (PR #248 mergeado 17:27 UTC → PR #250 mergeado 19:42 UTC), o Claude detectou que master avançou **13 commits do Felipe** entre os dois PRs.

Esses commits cobrem tasks formais ETK-NNNN (TASK-EZ-WEBHOOK-001, TASK-EZ-AVAL-001, TASK-EZ-APROVAR-001, TASK-EZ-PEDIDOS-001) em padrão TDD red/green/refactor — **disciplina exatamente prescrita por R1 v2.1 + R16** — mas pushados **direto em master**, sem branch + PR + admin squash merge.

R1 v2.1 (CLAUDE.md, versão atual em master) diz:

```
R1. Branch flow HIBRIDO (ADR-0020, v2.1):
    - Tasks formais com ID ETK-NNNN: SEMPRE branch + PR + 
      gh pr merge --admin --squash --delete-branch.
      Branch nome: feat/etk-NNNN-slug (gerada por scripts/tasks/claim.sh).
    - Hotfix / typo / doc < 1h e < 5 arquivos sem teste novo:
      commit direto em master OK conforme AGENTS.md.
```

Os 13 commits são **tasks formais ETK-NNNN** com tests novos + migrations + features de várias horas — fora da exceção "doc/hotfix < 1h < 5 arquivos". Pela letra do R1 v2.1, **deveriam** ter sido via branch + PR.

## Os 13 commits

Listados em ordem cronológica (mais antigo primeiro):

| SHA | Autor | Mensagem |
|---|---|---|
| `015a6a6d` | Felipe | `chore(TASK-EZ-WEBHOOK-001): base — incorpora CHECKOUT-001 (IMercadoPagoClient + Pedido AguardandoPagamento)` |
| `9b4c0add` | Felipe | `test(TASK-EZ-AVAL-001): red — AvaliacaoTokenServiceTests + ComentarioSanitizerTests + CriarAvaliacaoPedidoUseCaseTests` |
| `96c593d2` | Felipe | `feat(TASK-EZ-AVAL-001): green — use cases + token service + sanitizer + exceptions + migration` |
| `7e326948` | Felipe | `feat(TASK-EZ-AVAL-001): controller + background service + handler` |
| `c54578d1` | Felipe | `refactor(TASK-EZ-AVAL-001): integration tests + dotnet format` |
| `a514f65c` | Felipe | `refactor(arch): AvaliacaoCookieStore usa ICacheService — desacopla IMemoryCache` |
| `f192111c` | Felipe | `test(TASK-EZ-APROVAR-001): red - status AguardandoAprovacaoBaba/AprovadoBaba + transicoes storefront` |
| `a92ff27f` | Felipe | `feat(TASK-EZ-APROVAR-001): green - status AguardandoAprovacaoBaba/AprovadoBaba + campos Pedido + migration` |
| `169b05ab` | Felipe | `feat(TASK-EZ-APROVAR-001): green - use cases AprovarPedido + RecusarPedido + SELECT FOR UPDATE` |
| `39a8b875` | Felipe | `test(TASK-EZ-APROVAR-001): green - unit tests AprovarPedido + RecusarPedido use cases` |
| `d882be2f` | Felipe | `feat(TASK-EZ-APROVAR-001): green — controller + integration tests (concorrencia + E2E)` |
| `92c9b10b` | Felipe | `feat(TASK-EZ-PEDIDOS-001): green — use case + endpoint GET /pedidos cliente` |
| `c058da36` | Felipe | `chore(deps): add Microsoft.Extensions.Configuration to Application.Tests` |

Total: 18.416 inserções em 89 arquivos. Disciplina TDD respeitada (commits red→green→refactor separados). Mensagens Conventional Commits OK.

## Por que pode estar OK — interpretação alternativa

R1 v2.1 + R16 prescrevem ETK-NNNN + TDD + worktree por task + lock atômico. Os scripts `claim.sh`/`heartbeat.sh`/`complete.sh` são para **agentes Claude** rodarem em paralelo sem race.

**Felipe sozinho fazendo TDD direto em master** não tem race (ele é solo dev), não precisa lock, e os commits são auto-revisíveis. PR + merge admin squash adiciona ~5min de overhead que não captura sinal extra para um solo dev.

Em outras palavras: R1 v2.1 foi escrita pensando em **agentes Claude paralelos**. Aplicar literalmente ao Felipe solo é overhead sem benefício.

## Impacto operacional desta sessão

O AUDIT-RESULTS.md (mergeado via PR #248) foi escrito sobre `master @ e78dc493` (13:30 UTC). Master saltou para `8996ab86` (19:42 UTC) com features que **invalidam ~50% dos achados** da auditoria:

| ETK auditoria | Status original | Status pós 13 commits |
|---|---|---|
| ETK-AUDIT-001 (checkout storefront) | INDISPONÍVEL P0 | **Provavelmente obsoleto** — `IniciarCheckoutUseCase` + `CheckoutController` agora em master |
| ETK-AUDIT-002 (Pedido→PIX) | PARCIAL P0 | **Mudou contexto** — gateway migrou Efi→MercadoPago (`IMercadoPagoClient`); novo status `AguardandoPagamento` |
| ETK-AUDIT-003 (estorno em cancelar) | FAIL P0 | **Verificar** — `AprovarPedidoStorefrontUseCase` + `RecusarPedidoStorefrontUseCase` existem; precisa confirmar se recusa estorna |
| ETK-AUDIT-004 (SSE status) | INDISPONÍVEL P1 | Sem mudança aparente — não verificado |
| ETK-AUDIT-005..009 | — | Sem mudança aparente |

## Perguntas para Felipe (decisão pendente)

1. **R1 v2.1 deveria ter uma exceção explícita para "solo dev Felipe = commit direto em master autorizado para tasks ETK"?** Atualizar CLAUDE.md ou ADR-0020 se sim.

2. **A regra do PR + admin squash é estritamente para agentes Claude paralelos** ou também para tasks formais ETK-NNNN em geral? Texto atual é ambíguo.

3. **Quem audita a qualidade de PRs como esses 13 commits**, já que pulam o ciclo de PR? Husky pre-commit (architecture tests) rodou em cada um — é a única validação automática.

4. **A próxima Fase 1.5** (re-auditoria) deve ser sessão separada para refletir o estado atual de master. Esta sessão atual encerra com o registro deste drift.

## Próximas ações

1. **Fase 1.5 — Re-auditoria** em nova sessão Claude, considerando os 13 commits novos. Worktree próprio `wt-audit-v1-fase-1.5`. O AUDIT-RESULTS.md vai ganhar uma seção `### Atualização 2026-05-27 fim do dia` com os GPs re-avaliados.
2. **Decisão arquitetural sobre R1 v2.1**: Felipe escolhe se ajusta o texto ou se segue com a prática atual sem ajuste documental.
3. **Aviso para agentes Claude:** este incidente registra que master pode avançar significativamente durante uma sessão longa via commits do próprio Felipe — sempre verificar `git fetch && git log origin/master..HEAD` antes de assumir que master está estável.

## Referências

- PR [#248](https://github.com/michel-az-de/EasyStok/pull/248) — auditoria Fase 1 (potencialmente obsoleta em ~50%)
- PR [#250](https://github.com/michel-az-de/EasyStok/pull/250) — handoff Fase 1
- CLAUDE.md v2.1 R1 (regra)
- ADR-0020 — TDD + worktree por task
- AUDIT-RESULTS.md — adicionada nota de obsolescência apontando para este registro
