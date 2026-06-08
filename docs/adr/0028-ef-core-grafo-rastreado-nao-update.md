# ADR-0028 — EF Core: PK de filho de agregado = ValueGeneratedNever; nao chamar Update em raiz rastreada

**Status:** Aceito
**Data:** 2026-06-07
**Relacionado:** #512 (BUG-01 registrar pagamento), ADR-0014 (pagamento aditivo), ADR-0023 (estrategia de testes), ADR-0024 (higiene migrations)

## Contexto

No Admin, "Registrar pagamento" falhava **100%** com `DbUpdateConcurrencyException` ("Os dados
foram alterados por outro processo", 409 via `GlobalExceptionHandler`), Pago = R$ 0,00. Cancelar
fatura tinha o mesmo defeito (silencioso). Bloqueava o fluxo financeiro.

Causa-raiz **medida** por teste de integracao + diagnostico contra Postgres real (Testcontainers):

- Ao adicionar um `FaturaEvento`/`FaturaPagamento` novo (PK `Guid.NewGuid()` no factory) a uma
  `Fatura` **rastreada** e dar `SaveChanges`, o `DetectChanges` marca o filho como **`Modified`**
  (nao `Added`). DIAG: `eventoState=Modified | Fatura:Unchanged` -> `UPDATE fatura_eventos WHERE
  Id=@novo` -> **0 linhas** -> `DbUpdateConcurrencyException`.
- **Por que `Modified` e nao `Added`:** a PK `Guid` e *store-generated* (`ValueGeneratedOnAdd`, o
  default do EF Core). Com a PK preenchida, a heuristica do EF infere "entidade ja existente" ao
  encontra-la numa colecao de navegacao de uma raiz rastreada.
- Acontece **mesmo via `SaveChanges` direto**, sem `db.Faturas.Update()`. O `repo.UpdateAsync`
  (`db.Faturas.Update`) apenas **agravava** (o `TrackGraph` do `Update()` tambem rebaixa o filho a
  `Modified` por `IsKeySet`).
- A **emissao funciona** porque usa `repo.AddAsync` -> `db.Faturas.Add` -> grafo inteiro `Added`.
- **Nao era** o `xmin` (DIAG: `Versao=787`, lido OK) **nem** os value converters jsonb (DIAG:
  raiz `Unchanged`, `modifiedProps=[]`).

Precedentes no codebase: `ReportRunConfiguration` ja usa `.ValueGeneratedNever()` para PK `Guid`
gerada no app; `ProdutoConfiguration`/`PedidoConfiguration` usam `Property<uint>("xmin").IsRowVersion()`.

O gap passou porque o `EfiPixWebhookConcurrencyTests` usava `IFaturaRepository` **mockado** + cobrancas
com `FaturaId == null`, e o projeto `EasyStock.Infra.Postgre.IntegrationTests` estava **fora** do
`EasyStok.CI.slnf` e do script local — ou seja, o use case nunca rodou contra Postgres real.

## Decisao

1. **PK de filho de agregado gerada no app (`Guid.NewGuid` no factory) DEVE ser
   `.ValueGeneratedNever()` na configuration.** Faz o `DetectChanges` marcar o filho novo como
   `Added` (INSERT) ao adiciona-lo a uma raiz rastreada. Aplicado a `FaturaPagamento` e
   `FaturaEvento`. (Mudanca de metadado de modelo, **sem DDL**; snapshot atualizado a mao.)

2. **Nao chamar `db.Set.Update()` em raiz ja rastreada** (o `TrackGraph` rebaixa filhos novos a
   `Modified`). Removido de `RegistrarPagamentoFaturaUseCase` e `CancelarFaturaUseCase` — basta
   `CommitAsync`, mantendo o concurrency token `xmin` na raiz. `FaturaRepository.UpdateAsync` faz
   **fail-fast** (`throw InvalidOperationException`) quando a entidade esta `Detached` — grafo
   detached com filho novo nao e suportado.

3. **Barreira = contrato + fail-fast, nao arch test.** `AggregatePersistenceContract` (integracao)
   trava 2 invariantes para qualquer agregado com filhos: "filho novo em raiz rastreada + commit ->
   INSERT" e "concorrencia otimista dispara na raiz". Fatura e a 1a implementacao. NetArchTest opera
   em dependencias/namespaces, nao em data-flow intra-metodo — a regra nao e expressavel nele.

4. **`EasyStock.Infra.Postgre.IntegrationTests` entra no `EasyStok.CI.slnf`** — passa a rodar no CI
   (runner ubuntu, Docker nativo). Sem isso, o teste de regressao continuaria "nunca rodando".

## Consequencias

- (+) BUG-01 corrigido em registrar pagamento E cancelar; `xmin` na raiz preservado.
- (+) Padrao "PK de filho app-generated = `ValueGeneratedNever`" documentado e travado por contrato.
- (+) Testes de integracao do modulo passam a rodar no CI de verdade.
- (-) Custo de CI: +tempo subindo Postgres via Testcontainers.
- (-) `PagamentoOrchestrator` (dormente) repete o anti-padrao do `Update()`; issue separada para
  validar com teste quando P1 o ligar.
