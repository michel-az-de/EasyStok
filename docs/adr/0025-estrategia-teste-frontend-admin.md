# ADR-0025 — Estratégia de teste do frontend Admin (Alpine.js inline)

**Status:** Proposto
**Data:** 2026-06-04
**Relacionados:** ADR-0023 (estratégia e padrões de teste — base); issue #467 (tracking deste ADR), #463 (auditoria QA Admin — origem), #296/#297 (tiers de teste Azure), #274 (cobertura), #394 (umbrella de teste)

## Contexto

A auditoria QA do EasyStock.Admin (#463, 2026-06-04) confirmou 6 bugs reais. A
análise de causas-raiz expôs um gap estrutural (RC1): **a lógica de UI do Admin
vive em Alpine.js inline dentro dos `.cshtml`, e a suíte de teste não executa
Alpine.** Bugs de runtime JS passam batido por qualquer teste atual.

Dos 6 bugs confirmados:
- BUG-002 (null-deref em `x-text`), BUG-003 (dupla init via `x-data`+`x-init`) e
  BUG-005 (ranking da busca) são **runtime Alpine** — invisíveis aos testes.
- BUG-004 (double-encoding) é **HTML emitido pelo servidor** — esse a infra
  atual JÁ pega (`XssRenderTests` via `WebApplicationFactory`).
- BUG-006 (contraste/tema) é CSS — sem teste automatizado prático.
- BUG-008 (filtro de eventos) e a validação de cupom (INV-001) são **lógica
  testável** se extraída de `.cshtml`/controller para unidades puras.

A ADR-0023 definiu a pirâmide (unit no PR gate, integração com Testcontainers,
arquitetura/meta no pre-commit) e baniu verdes-falsos, mas **não trata o
frontend Admin**: não há camada que execute o JS nem padrão para isso. Este ADR
preenche a lacuna dentro do arcabouço da 0023.

Restrições do Admin (medidas na #463):
- Razor Pages **sem ProjectReference** (proxy HTTP puro): não enxerga
  Domain/Application; lógica de UI tende a virar JS inline.
- Sem harness JS/Node nem runner de browser/E2E no projeto.
- Já existe `EasyStock.Admin.UnitTests` com `WebApplicationFactory` (render
  server-side) + meta-lint de views (`ViewSinkGuardTests`).

## Opções consideradas

### A. Status quo (só manual + render server-side)
| Dimensão | Avaliação |
|---|---|
| Custo | Baixo |
| Cobre runtime JS | Não |
| Risco | Bugs Alpine seguem invisíveis; QA contra sandbox stale gera fantasmas (RC6) |

### B. Leve: extrair lógica + testes de render (recomendada agora)
| Dimensão | Avaliação |
|---|---|
| Custo | Médio (cabe no `EasyStock.Admin.UnitTests` atual) |
| Cobre | Lógica pura (scoring/validação/labels) + HTML server-side |
| Não cobre | Interação Alpine no browser (init, eventos) |

### C. E2E completo (Playwright em CI)
| Dimensão | Avaliação |
|---|---|
| Custo | Alto (novo harness, flakiness, infra) |
| Cobre | Runtime Alpine + detecta deploy stale (RC6) |
| Risco | Manutenção/flaky se virar obrigatório cedo demais |

## Decisão

Estratégia em três frentes, em ondas, **sem harness de browser obrigatório agora**:

1. **Extrair lógica testável do `.cshtml`/controller para unidades puras**
   ("logic out of the template"). Toda regra além de markup (scoring, validação,
   formatação, mapeamento) vira método/classe estático puro, testado por unit no
   PR gate (camada unit da 0023).
   - Já aplicado na #463: `TipoEventoNotificacaoLabels` (BUG-008) e
     `CupomValidacao` (INV-001) extraídos e testados.

2. **Cobrir o HTML emitido pelo servidor com testes de render**
   (`WebApplicationFactory`), no molde de `XssRenderTests`: encoding, estados
   vazios, presença de includes de script, paridade de dropdowns.
   - Já aplicado na #463: `EmptyStateEncodingTests` (BUG-004).

3. **Browser/E2E (Playwright) como onda futura, NÃO obrigatória até estabilizar**,
   escopada no Tier 2 de Azure (#296/#297). Cobre o que só o runtime pega:
   registro de componentes Alpine, dupla-init, interações. Entra como suíte
   separada (fora do PR gate) e segue o staging da 0023 (não-obrigatório →
   obrigatório quando confiável).

Itens que **conscientemente NÃO** ganham teste automatizado nesta fase:
contraste/tema (BUG-006) e layout responsivo (BUG-009) — verificação visual;
custo de automação > benefício agora.

## Trade-off

B entrega a maior parte do valor (a maioria dos bugs reais era lógica extraível
ou HTML server-side) a custo baixo, reusando a infra existente. C cobre o resto
(runtime Alpine), mas só compensa com o Tier 2 Azure de pé (#296/#297) e
seguindo o staging da 0023 para não virar fonte de flaky. Portanto: **B agora,
C escopado e adiado**.

## Consequências

### Positivas
- Bugs de lógica e de HTML do Admin passam a ser cobertos no PR gate (unit +
  render), fechando a parte barata do RC1.
- Reaproveita `EasyStock.Admin.UnitTests` — sem novo harness obrigatório.
- Cria o hábito "lógica fora do template", reduzindo JS inline não-testável.

### Negativas / aceitas
- Runtime Alpine (init/eventos) só fica coberto na onda C — aceito até o Tier 2.
- "Extrair lógica" exige disciplina; sem meta-teste que force, pode regredir
  (candidato a detector `ArchitectureDebt` no estilo da 0023, futuro).
- Contraste/tema e layout seguem sem rede automatizada.

## Action items
1. [ ] Padrão B no code review: PR no Admin com lógica não-trivial em `.cshtml`
   deve extrair + testar.
2. [ ] Backfill: extrair o scoring do command palette (BUG-005) para módulo
   testável quando tocá-lo de novo.
3. [ ] Avaliar meta-lint de paridade dropdown↔fonte (ex.: eventos).
4. [ ] Escopar a onda C (Playwright) junto de #296/#297; não obrigatório até
   estabilizar (staging 0023).
5. [ ] Re-deploy do sandbox com CI/CD para eliminar bugs-fantasma (RC6, #463).

## Referências
- Issue #463 (auditoria QA Admin, causas-raiz RC1–RC6).
- ADR-0023 (estratégia e padrões de teste — base/pirâmide/staging).
- #296/#297 (tiers de teste Azure), #274 (cobertura), #394 (umbrella de teste).
- Exemplos no repo: `EasyStock.Admin.UnitTests/TipoEventoNotificacaoLabelsTests.cs`,
  `EasyStock.Admin.UnitTests/EmptyStateEncodingTests.cs`,
  `EasyStock.Api.UnitTests/Validation/CupomValidacaoTests.cs`.
