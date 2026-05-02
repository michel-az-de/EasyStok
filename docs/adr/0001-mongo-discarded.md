# ADR 0001 — MongoDB descartado como provedor transacional

**Status:** Accepted (2026-05-01)
**Contexto do plano:** Bloco B, item B2 do plano de ação `~/.claude/plans/fa-a-uma-varredura-end-stateless-cat.md`.

## Decisão

PostgreSQL é o único provedor transacional suportado pelo EasyStock. Selecionar `Database:Provider=MongoDB` (ou `Mongo`) faz a API falhar em startup com `NotSupportedException`. O modo `Auto` deixa de considerar MongoDB.

## Por quê

A varredura end-to-end realizada em 2026-05-01 identificou que `EasyStock.Infra.MongoDb` tem **paridade incompleta** com `EasyStock.Infra.Postgre`:

- 41 repositórios em Postgres × ~15-20 classes em Mongo (consolidadas em `MongoCatalogRepositories.cs`, `MongoIdentityRepositories.cs`, `MongoInventoryRepositories.cs`).
- **Faltam repositórios MongoDB** para: `Venda`, `ItemVenda`, `MovimentacaoEstoque`, `Caixa`, `Lote`, `Pedido`. Selecionar Mongo em produção quebraria silenciosamente o fluxo de venda/estoque/caixa.
- Custo de manutenção dual sem benefício comprovado: nenhum cliente real foi identificado usando Mongo como provedor transacional.
- Postgre tem otimizações que Mongo não replica: `FOR UPDATE` em saídas FIFO/FEFO (`RegistrarSaidaEstoqueUseCase` + `ItemEstoqueRepository`), idempotência via constraints, transação explícita.

## Consequências

**Positivas:**
- Elimina vetor de bug silencioso em produção.
- Reduz superfície de manutenção (testes, DI, health checks).
- Foco do time em um único provedor maduro.

**Negativas:**
- Quem rodava ambientes locais com Mongo precisa migrar para Postgres (Docker compose já tem perfil local Postgres).
- Os projetos `EasyStock.Infra.MongoDb` e `EasyStock.Infra.MongoDb.IntegrationTests` permanecem fisicamente no repo nesta fase — apenas o switch de runtime os bloqueia. Remoção física é decisão futura caso fique claro que ninguém mais precisa do código.

## Mudanças aplicadas

- `EasyStock.Api/Program.cs`:
  - `case "mongodb"` no switch de DI lança `NotSupportedException`.
  - `ResolveDatabaseProviderAsync` lança `NotSupportedException` quando `Provider=MongoDB` é configurado e remove Mongo do branch `Auto`.
- `EasyStock.Api/appsettings.json`:
  - `Database.Provider` padrão = `"PostgreSQL"` (era `"Auto"`).
  - `ConnectionStrings.MongoConnection` removido.
  - `Database.MongoDatabase` removido.

## Reversão

Restaurar este commit reverte: o `case "mongodb"` volta a registrar `AddEasyStockMongoInfrastructure`, e `appsettings.json` volta a expor as chaves Mongo. Os projetos Mongo continuam compilando.

## Caminho futuro (opcional)

Se ficar consenso de que Mongo não voltará a ser provedor transacional:
1. `git rm -r EasyStock.Infra.MongoDb EasyStock.Infra.MongoDb.IntegrationTests`.
2. Remover do `EasyStok.sln` e das `ProjectReference` em `EasyStock.Api.csproj`, `EasyStock.Api.IntegrationTests.csproj`.
3. Apagar `MongoDbHealthCheckTests.cs`.
4. Limpar os `using EasyStock.Infra.MongoDb.*` do `Program.cs` e a função `IsMongoAvailableAsync`.
