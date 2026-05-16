# Plano: Caixa Conciliado + Pagamentos Múltiplos por Pedido

> Plano de implementação dos módulos **A — Caixa Conciliado com Pedidos** e
> **B — Pagamentos Múltiplos e Parciais por Pedido** no EasyStock. Origem do
> plano: sessão Claude Code de 2026-05-16 (arquivo de plano consolidado em
> `~/.claude/plans/design-critique-analyze-voc-zazzy-kazoo.md`).

## Índice

| # | Arquivo | Conteúdo |
|---|---|---|
| 00 | [00-reconhecimento.md](00-reconhecimento.md) | Stack, camadas, RLS, convenções, estado atual de Pedido/Caixa/Pagamento, acoplamentos perigosos, componentes reusáveis, lacunas |
| 01 | [01-dominio.md](01-dominio.md) | Schemas completos (`SessaoCaixa`, expansões aditivas em `PedidoPagamento`/`MovimentoCaixa`/`FechamentoCaixa`), índices, RLS, cardinalidades, ER em Mermaid, nomenclatura |
| 02 | [02-estados-e-eventos.md](02-estados-e-eventos.md) | Estados financeiros do pedido, state machine de `SessaoCaixa`, eventos de domínio inline vs outbox, payloads, handlers |
| 03 | [03-api.md](03-api.md) | Endpoints HTTP (`/api/pedidos/{id}/pagamentos`, `/api/pagamentos/{id}/estornar`, `/api/caixa/*`, verificação pública), códigos de erro, idempotency, concorrência por endpoint |
| 04 | [04-ux.md](04-ux.md) | Wireframes ASCII, fluxos clique-a-clique, microcopy PT-BR, wizard de fechamento em 3 passos, layout do PDF, acessibilidade WCAG AA |
| 05 | [05-migracao.md](05-migracao.md) | 6 migrations sequenciais, scripts de backfill, cutover plan via feature flag, validação SQL pós-migração |
| 06 | [06-testes.md](06-testes.md) | Unit/integration/snapshot/concurrency/E2E, validação contábil, cobertura mínima |
| 07 | [07-faseamento.md](07-faseamento.md) | 9 fases (F0–F8), critérios de "pronto" objetivos, cronograma realista, marcos com cliente, go/no-go, rollback |
| 08 | [08-riscos.md](08-riscos.md) | 14 riscos com probabilidade/impacto/mitigação/gatilho, 8 decisões abertas, pontos de não-volta |

## Ordem de leitura recomendada

1. **Este README** — visão geral, decisões críticas, cronograma.
2. **[00-reconhecimento.md](00-reconhecimento.md)** — entender o que existe antes de propor o que muda.
3. **[01-dominio.md](01-dominio.md)** — entender o modelo final.
4. Depois, leitura por interesse: API, UX, migrações, testes.
5. **[08-riscos.md](08-riscos.md)** antes de iniciar qualquer fase — refresh dos gatilhos de plano B.

## Contexto

O EasyStock hoje suporta múltiplos pagamentos por pedido via tabela
`pedido_pagamentos`, e tem entidades `MovimentoCaixa` + `FechamentoCaixa`
funcionais. Porém faltam três coisas críticas para o caso de uso real da
Casa da Babá e qualquer cliente regulado:

1. **Estorno auditado de pagamento** — hoje `RemoverPagamentoPedidoUseCase`
   faz `DELETE` físico, sem motivo, sem rastro, sem movimento de caixa
   reverso. Pagamento sumido da tabela ⇒ extrato fiscal incoerente.
2. **Sessão de caixa com fluxo de conferência e fechamento imutável** —
   hoje "abertura" e "fechamento" são tipos de `MovimentoCaixa` ad-hoc.
   Não há estado `em_conferencia`, não há conferência física esperado×contado,
   não há hash SHA-256, não há PDF, não há QR de verificação pública. Fiscal
   pede extrato de 2022 → não tem nada além do snapshot bruto.
3. **Conciliação Pedido↔Caixa via eventos** — `RegistrarPagamentoPedidoUseCase`
   já abre caixa automaticamente (best-effort, com `try/catch` engolido),
   mas não cria `MovimentoCaixa` por pagamento individual. Cancelamento de
   pedido pago não toca em caixa nem em pagamento. Pedido cancelado com
   pagamento existente fica em estado contábil incoerente.

Resultado: Thatiane (operadora) hoje fecha o caixa manualmente comparando
planilha, refaz extrato à mão quando há divergência, e o EasyStock perde
contra "voltar pra planilha" no primeiro fiscal sério.

Este plano entrega os dois módulos acoplados na ordem **B antes de A**
(pagamento robusto primeiro, caixa consome eventos depois), preservando
schema existente via princípio aditivo (`ADD COLUMN` + tabelas novas + FK
opcional), com migração de dados backfilling estado válido para todo
histórico.

## Sumário Executivo

**Módulo B — Pagamentos Múltiplos e Parciais**: reaproveita a tabela
`pedido_pagamentos` (`PedidoPagamento` entity) ampliando com colunas
aditivas: `Status` (`confirmado`/`estornado`/`falhou`), `EstornadoEm`,
`EstornadoPorUserId`, `MotivoEstorno`, `PagamentoOriginalId` (auto-ref para
estornos), `ConciliacaoTipo` (`fisico`/`adquirente`/`nao_conciliavel`),
`MovimentoCaixaId` (FK opcional). UseCases NOVOS criados ao lado do legacy
(sem rename/substituição): `ConfirmarPagamentoUseCase` (novo, paralelo a
`RegistrarPagamentoPedidoUseCase` legacy), `EstornarPagamentoUseCase` (novo).
Endpoint `POST /api/pedidos/{id}/pagamentos` no controller faz **dispatch
por feature flag** `CaixaConciliadoV2` (via `TenantFeatureFlag` existente):
flag ON → UseCase novo, flag OFF → UseCase legacy intacto, sem efeitos no
banco para empresas com flag OFF. `RemoverPagamentoPedidoUseCase` legacy
delega para `EstornarPagamentoUseCase` quando flag ON. `CancelarPedidoUseCase`
ganha estorno em cascata via handler quando flag ON. Backfill: pagamentos
existentes → `Status = confirmado`, `ConciliacaoTipo` derivado do `Metodo`.

**Módulo A — Caixa Conciliado**: cria nova entidade `SessaoCaixa` (tabela
`sessoes_caixa`) com ciclo `aberta → em_conferencia → fechada`. Pares
`abertura`/`fechamento` em `MovimentoCaixa` existentes são backfilled em
`SessaoCaixa` retroativas. `MovimentoCaixa` ganha FK opcional
`SessaoCaixaId`. `FechamentoCaixa` ganha colunas: `SessaoCaixaId`,
`HashSha256`, `PdfStorageKey`, `Snapshot` (jsonb), `ConferenciaItens`
(jsonb com esperado/contado/divergencia/justificativa por método),
`VerificacaoCodigo` (slug opaco para QR público). PDF gerado via QuestPDF
(template novo `FechamentoCaixaPdfRenderer`, reusando padrão do
`FaturaPdfRenderer`). Wizard de fechamento em 3 passos no PWA.

**Integração**: pagamentos confirmados criam `MovimentoCaixa` linkado à
`SessaoCaixa` aberta **dentro da mesma transação** (não via outbox — race
fatal). Outbox é reservado para integrações externas (email contador,
webhook). Idempotency via middleware existente (`IdempotencyMiddleware`)
expandido para cobrir os novos endpoints. Cronograma: **5 semanas
part-time** (2h/dia + fins de semana, com buffer 25%).

## Top 6 Decisões Críticas (em destaque)

1. **Estender `PedidoPagamento` (ADD COLUMN) em vez de criar tabela
   `pagamentos` nova.** Justificativa: 80% do schema já existe; tabela física
   `pedido_pagamentos` é usada pelo mobile MAUI sync e pelo PWA atual —
   substituí-la criaria fragmentação grande e exigiria sync code dual. ADD
   COLUMN com defaults respeita o princípio aditivo do escopo.
2. **Criar `SessaoCaixa` como entidade nova explícita** (não apenas
   agregação). Justificativa: state machine
   `aberta → em_conferencia → fechada` precisa de estado persistido; locks
   pessimistas (`pg_advisory_xact_lock`) precisam de aggregate root claro;
   FechamentoCaixa precisa de FK 1:1.
3. **Handlers críticos de invariante são INLINE (mesma TX), não via
   outbox.** Justificativa: `PagamentoConfirmado → MovimentoCaixa` precisa ser
   atômico ou viola invariante "todo pagamento confirmado tem movimento
   linkado". Outbox é reservado para eventos externos (email contador,
   webhook). **PDF + hash do FechamentoCaixa também são pré-commit** (ver
   [03-api.md](03-api.md) D.2.6 sequência de 15 passos).
4. **D1.3 híbrida**: mantém abertura automática (UX atual da Casa da Babá)
   + bloqueia quando sessão `em_conferencia`. Justificativa: opção pura (a)
   gera fricção desnecessária; opção pura (b) cria fantasma confuso. Híbrido
   protege o ponto crítico real (snapshot durante conferência).
5. **PT-BR conforme ADR-0011 já existente (não criar regra paralela) +
   `decimal` puro (não `Dinheiro` VO).** Justificativa: ADR-0011 (Accepted)
   já define a regra "PT-BR de negócio + sufixos EN consagrados" para
   qualquer módulo novo; este plano referencia, não recria. `decimal` puro é
   o que `MovimentoCaixa`, `FechamentoCaixa`, `PedidoPagamento` já usam —
   introduzir `Dinheiro` aqui criaria conversão em agregações já testadas.
6. **UseCases NOVOS ao lado do legacy + dispatch por feature flag
   `CaixaConciliadoV2`.** Justificativa: rollback de verdade exige que o
   código legacy continue intacto. Não há rename. Empresa com flag OFF NUNCA
   grava em `sessoes_caixa` nem cria movimento linkado — sem fantasmas no
   banco. Custo: ~20% de código duplicado, mas testes ficam isolados, e o
   teste de rollback (F4 critério de pronto) é executável literalmente.
   `TenantFeatureFlag` (entidade JÁ EXISTENTE, migration
   `20260430205554_AddGovernancaFeatures`) é reusada sem migration nova.

## Cronograma Final (semanas calendar)

- **Semana 1** (2026-05-25 → 05-31): F0 + início F1
- **Semana 2** (06-01 → 06-07): conclui F1 + começa F2
- **Semana 3** (06-08 → 06-14): conclui F2 + F3
- **Semana 4** (06-15 → 06-21): F4 + começa F5
- **Semana 5** (06-22 → 06-28): conclui F5 + começa F6
- **Semana 6** (06-29 → 07-05): F6 PDF + verificação pública
- **Semana 7** (07-06 → 07-12): F7 + começa F8
- **Semana 8** (07-13 → 07-19): F8 polimento + go-live segunda metade

**Marcos cliente**:
- **M1**: 2026-06-12 (Casa da Babá testa pagamento+estorno em staging)
- **M2**: 2026-07-06 (Casa da Babá testa fechamento completo em staging)
- **M3**: 2026-07-15 (go-live em produção com flag, suporte ao vivo)

Total: **~5 semanas trabalhadas part-time em janela de 8 semanas calendar**.

Detalhes por fase: [07-faseamento.md](07-faseamento.md).

## Verificação E2E (resumo)

**Como verificar que os módulos funcionam, ponta-a-ponta:**

1. **Local dev**:
   ```powershell
   cd C:\rep\EasyStok
   dotnet build
   dotnet test
   dotnet ef database update --project EasyStock.Infra.Postgre --startup-project EasyStock.Api
   dotnet run --project EasyStock.Api
   # Abre PWA em http://localhost:5000
   ```

2. **Validação contábil**: PDF gerado em staging enviado por email ao
   contador. Aprovação registrada em `docs/plan/validacao-contabil.md`
   (a criar antes de F6).

3. **Race tests**: `dotnet test EasyStock.Api.IntegrationTests --filter Concurrency`
   — 100% verde. Detalhes em [06-testes.md](06-testes.md) G.4.

4. **Roteiro Thatiane**: executado em staging com captura de tela por
   passo. Detalhes em [06-testes.md](06-testes.md) G.5.

5. **Validação SQL**: script `scripts/sql/validacao_caixa_conciliado.sql`
   rodado em produção pós-deploy — todas as 7 queries retornam 0. Detalhes
   em [05-migracao.md](05-migracao.md) F.5.

6. **Smoke pós-deploy**:
   ```bash
   fly status
   curl -s https://easystok.fly.dev/health | jq
   ```

## Premissas Explícitas que Precisam Validação Humana ANTES de F1

Antes de codificar qualquer linha de F1, Felipe deve confirmar:

1. **A tabela `TenantFeatureFlags` está em uso** (entidade
   `EasyStock.Domain/Entities/TenantFeatureFlag.cs`) e Felipe pode criar
   registros para Casa da Babá via SQL/admin.
2. **`Permissao` enum em Domain pode receber novas entradas** sem migration
   complexa de roles existentes (`caixa.abrir`, `caixa.fechar`,
   `caixa.movimentos.registrar`, `caixa.historico.ver`,
   `pedidos.pagamentos.estornar`).
3. **Casa da Babá aceita janela de manutenção de 30 minutos** num domingo à
   noite para deploy + backfill F3.
4. **Contador da Casa da Babá está disponível** para 1 reunião em F0
   (validar layout do PDF) + 1 revisão em F6 (PDF real). Sem isso, F6
   trava.
5. **Política de privacidade não impede QR code público** apontando para
   `/caixa/verificar/{codigo}` (página mostra apenas: empresa, data, hash,
   nome operador — não expõe valores ou clientes).
6. **Mobile MAUI v1.0.7 atual continua compatível** com `pedido_pagamentos`
   após ADD COLUMN. **Quem valida**: Felipe roda 5 smokes em emulador
   Android API 34 **no fim de F1** (antes de aplicar M03 em staging):
   (a) login + sync, (b) criar pedido offline, (c) pagar pedido offline +
   sync, (d) listar pagamentos, (e) cancelar pedido. **Critério de pronto
   de F1**: 5/5 smokes verde + screenshots commitados em
   `docs/plan/smoke-mobile-pos-m03.md`.
7. **Fly.io tem espaço para PDFs** — estimativa: 365 PDFs/ano × ~50KB =
   ~18MB/ano por empresa. Trivial. Confirmar disco do volume Fly.
8. **`QRCoder` 1.6+ pacote NuGet é aceito** pelo padrão de licença do
   projeto (MIT). Confirmar.
9. **Backfill em produção pode rodar em janela noturna** — Felipe agenda com
   Casa da Babá.
10. **`SHA256.HashData` do PDF + canonical JSON é suficiente** como prova
    fiscal (não precisa eIDAS/ICP-Brasil) — confirmado com contador.
11. **Endpoint de emergência `desligar-emergencia` + treinamento da
    Thatiane** são bloqueadores de F8. Casa da Babá precisa concordar em
    treinar a operadora (15min de demo). Doc:
    [`../operacao/janela-suporte-felipe.md`](../operacao/janela-suporte-felipe.md).

## ADRs relacionados

- [ADR-0011](../adr/0011-nomenclatura-pt-br-rotulagem.md) — Nomenclatura
  PT-BR + sufixos EN (regra herdada deste plano).
- [ADR-0014](../adr/0014-pagamento-aditivo-em-pedido-pagamento.md) — Decisão
  de estender `PedidoPagamento` vs criar tabela nova.
- [ADR-0015](../adr/0015-sessao-caixa-como-entidade-explicita.md) — Decisão
  de criar `SessaoCaixa` como entidade.
- [ADR-0016](../adr/0016-fechamento-hash-retencao-cinco-anos.md) — Decisão
  de hash SHA-256 + retenção via `EntityAlteracao` 1825 dias.

## Próximos passos

1. Felipe valida premissas 1–11 acima (1 sessão de perguntas + 1 reunião
   com contador da Casa da Babá).
2. Felipe envia [`../operacao/janela-suporte-felipe.md`](../operacao/janela-suporte-felipe.md)
   para a Casa da Babá assinar.
3. Criar branch `feat/caixa-conciliado-v2` localmente.
4. Iniciar F0 (ver [07-faseamento.md](07-faseamento.md)).
