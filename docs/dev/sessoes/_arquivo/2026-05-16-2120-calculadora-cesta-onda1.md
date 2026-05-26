# Sessao calculadora-cesta-onda1

Data: 2026-05-16 21:20
Worktree: C:/rep/EasyStok/.claude/worktrees/calculadora-producao
Branch: feat/calculadora-producao
Identidade Git: felipe.azevedo@gmail.com / michel-az-de
Status final: completo — push autorizado feito, PR #135 atualizado, smoke manual pendente

## O que foi feito

Sessao iniciada em outro worktree (`nice-agnesi-c18d23`) com pedido do Felipe via `/engineering:architecture` + `/ux-copy`: planejar UX de uma "calculadora de producao" no PWA (dado pedido de 800g de talharim, calcular farinha + ovos, verificar estoque, ofereser lista de compras). Resultado: identificacao de que ja havia branch `feat/calculadora-producao` com 10 commits originais (motor + endpoint + aba single PWA prontos), entao plano virou "evoluir + Onda 1 cesta in-context read-only".

Plano completo em `~/.claude/plans/ux-copy-navegue-pelo-vectorized-bonbon.md`.

### Decisoes alinhadas com Felipe (4 ondas de perguntas)
1. Evoluir branch existente — sim
2. Recursao BOM (massa → ovos via talharim → pastel) — Onda 2
3. Cesta multi-item — Onda 1 essencial
4. Entrada UX — in-context a partir do Pedido (bottom-sheet)
5. Saida Onda 1 — read-only (sem lista de compras, sem PF nesta onda)
6. Web admin "rigido" — Onda 3
7. Granularidade resultado — agrupado por produto + total consolidado

### Reconciliacao da branch com master
- Branch estava 62 commits atras de master e build falhava (NU1605 no `EasyStock.Infra.MongoDb.IntegrationTests` por downgrade DI 9.0.4→9.0.0).
- Felipe autorizou `merge master into feat` (R9). 5 conflitos resolvidos:
  - `sw.js` PWA + MAUI: cache version v21→v26 (pos-merge)
  - `EasyStok.Mobile.csproj`: ApplicationDisplayVersion 1.0.10/11
  - `EasyStockDbContextModelSnapshot.cs`: mantem TipoEmbalagem (master) + UnidadeMedidaBase (feat)
  - Bundle MAUI `index.html`: regenerado a partir do PWA principal (SHA-256 6904273D…)
- Untracked removidos (admin/, path corrompido com bytes octais — R11/R12).
- Build verde pos-merge (0 erros, 32 warnings pre-existentes).

### Onda 1 implementada (4 commits)

**`7c6347bd` feat(app)**: CalculoProducaoCore + CalcularCestaProducaoUseCase
- `CalculoProducaoCore.cs` (novo): algoritmo puro 1 nivel extraido de `CalcularProducaoUseCase`. Reusado pelo single (refatorado, comportamento inalterado) e pelo cesta. Onda 2 estendera para recursao BOM.
- `CalcularCestaProducaoCommand/Result` (novos): DTOs com Status enum (Ok/SemReceita/Erro) + InsumoConsolidadoResult.
- `CalcularCestaProducaoUseCase` (novo): batch 1 round trip receitas + 1 round trip saldos. Tolerancia por item — RECIPE_NOT_FOUND vira Status.SemReceita; demais erros viram Status.Erro com mensagem.
- Consolidacao: soma insumos por InsumoId entre produtos, converte unidade quando compativel, marca ConversaoFalhou em conflito de grupo.
- `IProdutoComposicaoRepository.GetByProdutosFinaisAsync` (novo): batch com Include(ProdutoFinal+Insumo) e resolucao override-vs-padrao por loja em memoria.
- Testes: 10 novos (cesta vazia, sem receita, consolidacao compativel, consolidacao incompativel, erro tolerado, batch 1x) — 18 totais CalcularProducao* verdes.

**`23924a5a` feat(api)**: endpoint `POST /api/mobile/calculadora/calcular-cesta`
- Validacoes 400: EMPTY_CESTA, UNIT_INCOMPATIBLE.
- Tenant guard via `MobileManagementControllerBase`.

**`b0f6c512` feat(pwa)**: modal in-context
- `#cestaCalcModal` (bottom-sheet padrao modal-sheet): header com kicker + lista cesta read-only + resultado com badge global + custo + cards "Por produto" + bloco "Total consolidado" + CTA Fechar/Calcular.
- Chip "Conferir insumos" no `.order-draft-summary`, sync via `MutationObserver` no `#orderItemsList`.
- Botao "Insumos" no `.k-actions` do `renderKanbanCard`, condicional a `aguardando|preparando`.
- JS: `cestaCalc*` handlers, cache `produtosComReceitaCacheSet` pre-carregado 1x por sessao.
- `sw.js`: cache version v26→v27.

**`833cc4d7` chore(mobile)**: bundle MAUI espelhado
- SHA-256 PWA == MAUI: AE1C6EDE…CC2AA13
- Aplica dual-frontend-policy 1:1 no mesmo commit.

### Estado de testes
- Build: 0 erros, 32 warnings pre-existentes
- `EasyStock.Application.Tests`: 532/532 verdes (8 single + 10 novos cesta)
- `EasyStock.ArchitectureTests`: 6/7 verdes — 1 conhecido em `docs/dev/flaky-tests.md:22` (Exceptions_De_Domain_Devem_Ficar_No_Domain, regressao arquitetural ja documentada, nao introduzida por este PR)

## O que ficou pendente

- **Smoke manual PWA** (esperando Felipe subir Api local OU testar APK MAUI):
  - Pedido com 3 itens (1 sem receita) → modal in-context abre, calcula, renderiza por produto + consolidado.
  - Status `pronto` → botao "Insumos" some no kanban.
  - Offline → toast.
- Multi-tenant DevTools probe (curl ou Network tab garantindo empresaId correto).
- **Onda 2** (recursao BOM + saida ativa lista/PF): PR separado depois de 2-3 semanas de uso da Onda 1.
- **Onda 3** (web admin "rigido"): PR separado depois da Onda 2.

## Decisoes tomadas

- Algoritmo de calculo extraido em static class `CalculoProducaoCore` (vs metodo privado em UseCase) — permite ambos UseCases (single, cesta) compartilharem sem dependencia.
- Tolerancia por item: RECIPE_NOT_FOUND tratado como `Status.SemReceita` (UI mostra placeholder) em vez de erro 400 — sem isso, cesta com 1 produto sem receita derrubaria todo o calculo, inviabilizando uso em maturidade mista de BOM.
- Consolidacao usa unidade do primeiro insumo encontrado como base; tentativa de conversao para somar. Em conflito de grupo (Kg vs L), marca `ConversaoFalhou` e nao soma — UI mostra warning gold.
- Pre-carga do cache `produtosComReceitaCacheSet` 1x no load: evita request por keystroke no draft. Limite 200 produtos cobre clientes reais.
- `MutationObserver` no `#orderItemsList`: padrao mais limpo que polling para sincronizar visibilidade do chip "Conferir insumos" no draft sem invadir o ciclo de update do pedido.
- Botao "Insumos" no kanban so aparece em `aguardando|preparando`: momento natural ("fui produzir, tenho?"). Em `pronto|entregue` nao faz mais sentido.

## Commits criados (nesta sessao)

- `75c25b30` merge: master into feat/calculadora-producao (62 commits absorvidos pre-Onda 1)
- `7c6347bd` feat(app): CalculoProducaoCore + CalcularCestaProducaoUseCase Onda 1 cesta
- `23924a5a` feat(api): endpoint POST /api/mobile/calculadora/calcular-cesta
- `b0f6c512` feat(pwa): aba in-context Calculadora Cesta - modal bottom-sheet + chip + botao kanban
- `833cc4d7` chore(mobile): bundle PWA cesta Onda 1 - SHA conferido com PWA principal

(+ este handoff sera o 6o commit)

## Branches criadas/deletadas

- Nenhuma criada nesta sessao.
- `feat/calculadora-producao` push autorizado: `e94243e8..833cc4d7` (15 commits totais ahead de master).

## Proxima acao recomendada

1. Felipe executa smoke manual PWA: criar pedido com mix de itens com/sem receita, abrir modal in-context, validar calculo + consolidado.
2. Mergear PR #135 com `gh pr merge 135 --admin --squash --delete-branch` (autorizar R9).
3. Bumpar APK MAUI 1.0.10→1.0.11 quando necessario distribuir aos operadores (Felipe via WSL — `feedback_apk_download.md`).
4. Observar 2-3 semanas com uso real. Telemetria sugerida no plano:
   - `calculadora.cesta_opened` `{pedido_id, source: 'novo'|'andamento', items_count, items_com_receita}`
   - `calculadora.cesta_calculated` `{pedido_id, tudo_disponivel, falta_count}`
   - `calculadora.cesta_closed` `{outcome}`
5. Decidir Onda 2 baseado em dados:
   - Se >10 usos/semana e operadores pedem "queria ja gerar lista de compras" → ativa Onda 2 saida ativa
   - Se aparece pedido com receita aninhada (massa → ovos via talharim → pastel) → ativa Onda 2 recursao BOM
   - Caso contrario, deixar como esta — Onda 1 ja resolve o caso primario

## Referencias

- Plano: `~/.claude/plans/ux-copy-navegue-pelo-vectorized-bonbon.md`
- PR: https://github.com/michel-az-de/EasyStok/pull/135
- ADRs relacionados: nenhum dedicado a calculadora; Onda 2 precisara de ADR sobre algoritmo de recursao + deteccao de ciclo
- Flaky test tolerado: `docs/dev/flaky-tests.md:22` (Exceptions_De_Domain_Devem_Ficar_No_Domain)
- Incidente pre-merge: `docs/dev/incidentes/2026-05-16-master-broken-wip-snapshot.md` (motivou o merge para incorporar correcoes)
- Knowledge: `.knowledge/dual-frontend-policy.md` (PWA→MAUI 1:1 no mesmo commit, SHA conferido)
