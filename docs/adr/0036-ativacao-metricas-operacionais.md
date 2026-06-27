# ADR-0036 — Ativação parcial de métricas operacionais (contador de falhas) + roadmap das métricas de negócio

**Status:** Aceito
**Data:** 2026-06-27
**Refs:** issue #701 (ativa contador de falhas + limpezas de qualidade)

## Contexto

`EasyStock.Api/Observability/MetricsService.cs` definia 5 contadores OpenTelemetry
(`entradas_estoque_total`, `saidas_estoque_total`, `reposicoes_estoque_total`, `vendas_total`,
`falhas_operacao_total`) e estava registrado no DI, **mas nenhum consumidor o injetava** — os
contadores nunca disparavam. Pior: o `WithMetrics` (em `AddEasyStockObservability`) **não chamava
`.AddMeter("EasyStock.Api")`**, então mesmo que disparassem o MeterProvider não inscreveria o meter
e nada exportaria via OTLP. Era código morto que parecia feature.

Ao projetar a ativação, a medição revelou que as **4 métricas de negócio não têm um ponto de
emissão honesto barato**:

1. **Múltiplos caminhos.** A `MovimentacaoEstoque` é criada em ≥6 lugares e persiste por
   `IMovimentacaoEstoqueRepository.InsertAsync`/`InsertRangeAsync`. A baixa de uma venda de
   **pedido** (balcão, storefront, mobile order) ocorre em `AtualizarStatusPedidoUseCase` →
   `PedidoEstoqueIntegrationService.DescontarAsync`, **não** em `RegistrarSaidaEstoqueUseCase`.
   Instrumentar os use cases de estoque contaria errado (vendas de pedido invisíveis; entradas
   net-zero do balcão contadas).
2. **Transação com retry.** `IUnitOfWork.ExecuteInTransactionAsync<T>` usa `IExecutionStrategy`
   (retry). Emitir no momento do `Insert` superconta sob retry/rollback (uma venda com 1 retry =
   2 incrementos).

O contador de **falhas**, por outro lado, tem um chokepoint único e limpo: o
`GlobalExceptionHandler`, fora de transação/retry.

## Decisão

Ativar **apenas** o contador de falhas agora; adiar as métricas de negócio.

1. **Porta na Application.** `IOperationalMetrics` (`Ports/Output/Observability`) expõe só
   `void IncrementFalhasOperacao(string code)` — sem nenhum tipo OTel, mantendo a Application
   independente da infra de observabilidade. O adapter `MetricsService` (Api) a implementa.
   Injeção **obrigatória** (sem `?`/default null) — DI que não registrar a porta falha rápido,
   em vez de virar no-op silencioso.
2. **Inscrição do meter.** `AddEasyStockObservability` passa a chamar
   `.AddMeter(MetricNames.MeterName)`. Sem isso o instrumento não exporta (regressão silenciosa
   coberta por teste no MeterProvider real + InMemoryExporter).
3. **Nomes em fonte única.** `MetricNames` centraliza meter + instrumento, usados por
   `new Meter`/`CreateCounter`, `AddMeter` e testes — sem drift.
4. **Emissão só em 5xx.** O `GlobalExceptionHandler` chama `IncrementFalhasOperacao(code)` apenas
   quando `statusCode >= 500`, rotulando pelo `code` mapeado. Em 5xx o domínio de `code` é
   **fechado**: `{INTERNAL_ERROR, NOT_SUPPORTED}` — sem explosão de cardinalidade.
5. **Remoção de código morto.** Os 4 contadores de negócio inertes foram removidos do
   `MetricsService`; voltarão na feature dedicada (abaixo), já com o seam correto.

### Semântica declarada dos contadores

- `falhas_operacao_total` = **somente** respostas 5xx tratadas pelo `GlobalExceptionHandler`
  (erros 4xx, validações e regras de negócio **não** contam). Tag `code` ∈ {INTERNAL_ERROR, NOT_SUPPORTED}.
- Métricas de negócio (entradas/saídas/reposições/**vendas**): **não existem nesta entrega**.
  Quando voltarem, `saidas_estoque_total` **não** incluirá baixa por venda (`Natureza == Venda`),
  que será contada em `vendas_total`.

## Consequências

- O contador de falhas exporta via OTLP (endpoint `OpenTelemetry:OtlpEndpoint`, default
  `http://localhost:4317`; ConsoleExporter em Development). Visível onde houver um collector
  apontado; sem collector, a métrica é registrada mas não tem destino (entrega = código + teste +
  export verificado em memória).
- A entrega é honesta: nenhum painel passa a mentir. O custo é que vendas/estoque continuam sem
  métrica operacional até a feature dedicada.
- **Follow-up (issue #702):** métricas de negócio com seam **pós-commit** sobre a persistência de
  `MovimentacaoEstoque` (taxonomia por `Natureza`, tratamento de Estorno, captura de todos os
  caminhos).
