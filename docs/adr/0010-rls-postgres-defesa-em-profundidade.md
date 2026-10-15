# ADR 0010 — Row-Level Security no Postgres como defesa em profundidade

**Status:** Accepted (2026-05-11)
**Contexto da pendência:** [`pendencias/PEND-RLS.md`](../../../OneDrive/Documents/easy/EasyStok/pendencias/PEND-RLS.md) — replanejamento após perda do trabalho original de 2026-05-08 (branch `arch/audit-2026-05-08` sumida localmente, sem código RLS em master).

## Decisão

Habilitar Row-Level Security (RLS) no Postgres em todas as tabelas tenant-aware como **segunda camada** de isolamento multi-tenant, complementando o Global Query Filter já presente no EF Core. O contexto de tenant é propagado por conexão via `SET app.empresa_id` emitido por um `DbConnectionInterceptor`. Operações cross-tenant deliberadas (migrations, seeds, jobs, login pré-auth) usam `EasyStockDbContext.UseRowLevelSecurityBypass()` que emite `SET app.bypass_rls = 'true'`.

## Por quê

Antes deste ADR, o isolamento multi-tenant dependia 100% do `HasQueryFilter` em EF Core, configurado em `EasyStockDbContext.ApplyTenantQueryFilters`. Quatro caminhos de falha existiam:

1. **SQL crua** (`FromSqlRaw`, Dapper, scripts ad-hoc) não passa pelo filtro.
2. **`IgnoreQueryFilters()` esquecido** em uma branch nova ignora a primeira camada.
3. **Bug em `ApplyTenantQueryFilters`** (regressão futura) vaza silenciosamente.
4. **Use case com erro de digitação no `Where`** seria invisível para revisão.

Em SaaS pool multi-tenant, vazamento entre clientes é a falha mais cara (LGPD: incidente reportável à ANPD; SOC 2 CC6.1: defesa em camadas). Stripe e AWS SaaS Lens recomendam RLS como referência para o padrão pool. `CLAUDE.md` já marca multi-tenant como "RISCO MÁXIMO".

## Como funciona

1. **Migration `20260511120000_AddRowLevelSecurity`** itera `information_schema.columns` em runtime e aplica para cada tabela com coluna `EmpresaId`:
   - `ENABLE ROW LEVEL SECURITY` + `FORCE ROW LEVEL SECURITY` (FORCE garante que o owner também respeita).
   - Policy `tenant_isolation` com `USING` + `WITH CHECK`:
     ```sql
     current_setting('app.bypass_rls', true) = 'true'
       OR "EmpresaId" = NULLIF(current_setting('app.empresa_id', true), '')::uuid
     ```
   - `NULLIF('','')::uuid` trata conexão de pool sem tenant setado: `current_setting` retorna `''` → NULL → comparação UNKNOWN → 0 linhas (fail-closed).

2. **`SetTenantOnConnectionInterceptor`** (`DbConnectionInterceptor`):
   - `ConnectionOpenedAsync`: lê `EasyStockDbContext.CurrentTenantId` + `BypassRowLevelSecurity` e emite `SET app.empresa_id = '...'; SET app.bypass_rls = 'true|false'`.
   - `ConnectionClosingAsync`: emite `RESET app.empresa_id; RESET app.bypass_rls` — defesa contra reentrada de pool. Mesmo se RESET falhar, o `Apply` na próxima abertura sobrescreve antes de qualquer query.

3. **`EasyStockDbContext.BypassRowLevelSecurity` + `UseRowLevelSecurityBypass()`** (escopo `using`): liga a flag por escopo de operação cross-tenant. Reaplicada no próximo `ConnectionOpened`.

## Skip list

Quatro grupos isentos:

| Tabela | Motivo |
|---|---|
| `admin_impersonation_logs` | Auditoria cross-tenant do SuperAdmin — leitura legítima sem tenant. |
| `TenantFeatureFlags` | Toggles globais avaliados sem JWT contextual. |
| `fatura_contador` | PK composta `(EmpresaId, Ano)` acessada via INSERT...ON CONFLICT em background — RLS zerá leituras de fallback. |
| `mobile_*` | Módulo Casa da Babá tem isolamento por loja, não por empresa. |

## Pontos cross-tenant que recebem bypass

| Ponto | Onde |
|---|---|
| Aplicação de migrations | `Program.cs` — bloco `migrator.MigrateAsync` |
| Schema bootstrap | `SeedSchemaBootstrap.EnsureAsync` (defensivo) |
| SuperAdmin seed | `SuperAdminSeed.ExecutarAsync` |
| Seed demo | `SeedData.ExecutarAsync` |
| Notificações globais seed | `NotificacoesGlobaisSeed.ExecutarAsync` |
| Reconciliação de fatura | `FaturaReconciliacaoJob.ProcessarReconciliacaoAsync` |
| Login pré-auth | `UsuarioRepository.GetByEmailAsync` |
| Webhook fiscal (sem JWT) | `ProcessarWebhookFocusNFeUseCase` — fix B-054 |
| Reprocessamento de contingência | `ReprocessarContingenciaUseCase` — fix B-053 |
| **Registro de empresa** | `RegistrarEmpresaUseCase` — issue #1024 |

As duas linhas de fiscal já existiam no código e faltavam nesta tabela; entraram junto com a #1024,
que as encontrou ao medir quem consome o port.

**Sobre o registro de empresa (#1024).** É o único ponto desta tabela que está no *request path*, o
que à primeira vista contradiz a regra "bypass só em job/webhook". Não contradiz: a requisição é
**anônima** e a operação **cria o tenant**, então não existe `app.empresa_id` para o interceptor
emitir — `NULLIF(current_setting('app.empresa_id', true), '')::uuid` resolve para `NULL` e o
`WITH CHECK` reprova todo INSERT em tabela tenant-aware (`42501`, medido em `assinaturas_empresa`).
O critério que separa este caso de um mau uso não é "tem JWT?", e sim **a operação cria ou atravessa
tenants por natureza?**. Se a resposta for não e a query voltou vazia, o defeito está no contexto de
tenant, e o bypass apenas o esconderia.

O escopo cobre o use case inteiro, não só os INSERTs: `perfis` tem `EmpresaId`, logo tem RLS, e sem
bypass o `GetPadroesAsync` volta vazio — o registro criaria um perfil Admin por empresa em vez de
reusar o padrão, mudando a forma do dado **sem erro nenhum**.

**Portão (#1024).** O que o "ArchitectureTests futuro" previsto abaixo virou:
`RlsBypassAllowlistTests` trava a lista de arquivos que podem referenciar
`IRowLevelSecurityBypass` em código. Consumidor novo exige editar a allowlist, o que faz a decisão
aparecer no diff da PR em vez de escorregar num construtor.

## Por que app flag e não role com `BYPASSRLS`

Avaliamos as duas alternativas (mini-prompt da pendência sugeria qualquer uma):

- **Role com `BYPASSRLS`**: acoplado a privilégios do banco. Migrations rodariam com role privilegiada, queries normais com role restrita. Vantagem: dois usuários, controle estrito.
- **App flag (`UseRowLevelSecurityBypass`)**: auditável via grep de código, escopável com `using`, fácil de revisar em PR. **Escolhido**.

Trade-off aceito: um bug que persista o flag (ex.: esquecer o `using`) burla o RLS. Mitigação: o `IDisposable` reverte ao sair do escopo; um `ArchitectureTests` futuro pode detectar resoluções de `EasyStockDbContext` em services cross-tenant sem bypass.

## Consequências

**Positivas:**
- Defesa em profundidade real: SQL crua, `IgnoreQueryFilters`, regressão do filtro EF — todas bloqueadas no banco.
- Sem custo de troca de role/usuário em pool de conexões.
- `WITH CHECK` adicionalmente bloqueia INSERT/UPDATE cross-tenant — mesmo se um use case montar uma entidade com `EmpresaId` de outro tenant por engano, o `INSERT` falha com 42501.

**Negativas:**
- Cada abertura de conexão paga ~1 round-trip extra para o `SET`. No throughput atual (pool de 10), invisível; revisitar se aparecer regressão de latência.
- Jobs cross-tenant precisam do `using` explícito — esquecer significa job que misteriosamente "não vê nada". A PR original já fechou os 6 pontos críticos conhecidos.
- Conexões abertas via `Database.OpenConnectionAsync` sem ir por `Set<T>` entram com `Context = null` no interceptor; ele emite RESET, e o caller precisa aplicar tenant manualmente (raro — só `FaturaReconciliacaoJob` para o advisory lock, que já tem bypass).

## Mudanças aplicadas

- `EasyStock.Infra.Postgre/Migrations/20260511120000_AddRowLevelSecurity.cs` — migration nova.
- `EasyStock.Infra.Postgre/Data/Interceptors/SetTenantOnConnectionInterceptor.cs` — interceptor novo.
- `EasyStock.Infra.Postgre/Data/EasyStockDbContext.cs` — flag `BypassRowLevelSecurity` + `UseRowLevelSecurityBypass()`.
- `EasyStock.Infra.Postgre/DependencyInjection/ServiceCollectionExtensions.cs` — registro do interceptor.
- `EasyStock.Infra.Postgre/Repositories/UsuarioRepository.cs` — bypass no `GetByEmailAsync` (login pré-auth).
- `EasyStock.Api/Program.cs` — bypass em migrations, schema bootstrap, super admin seed, seed demo, notificações globais.
- `EasyStock.Api/BackgroundServices/FaturaReconciliacaoJob.cs` — bypass no `ProcessarReconciliacaoAsync`.
- `EasyStock.Infra.Postgre.IntegrationTests/Tenancy/RowLevelSecurityTests.cs` — 6 testes cobrindo: sem set→0, tenant A→A, tenant B→B, bypass→tudo, WITH CHECK em INSERT cross-tenant, interceptor emite SET correto.
- `EasyStock.Application/Ports/Output/Security/IRowLevelSecurityBypass.cs` — port que expõe o bypass à Application (que não pode referenciar a Infra); implementado por `Infra.Postgre/Security/RowLevelSecurityBypass.cs`.
- `EasyStock.ArchitectureTests/RlsBypassAllowlistTests.cs` — allowlist de quem pode referenciar o port (#1024).
- `EasyStock.Infra.Postgre.IntegrationTests/Tenancy/RegistrarEmpresaRlsIntegrationTests.cs` — signup sob role `NOSUPERUSER NOBYPASSRLS`, com controle negativo que prova que a RLS está viva no ambiente de teste (#1024).

## Validação

- `dotnet build EasyStok.sln`: 0 erros, 0 warnings.
- `dotnet test EasyStock.Api.UnitTests`: 165 passam.
- `dotnet test EasyStock.Infra.Postgre.IntegrationTests --filter RowLevelSecurityTests`: 6 testes definidos; passaram trivialmente nesta máquina (sem Docker, fixture skipa); validar em CI com containers.

## Referências

- [PEND-RLS](../../../OneDrive/Documents/easy/EasyStok/pendencias/PEND-RLS.md) — mini-prompt original.
- [Multi-tenant](../../../OneDrive/Documents/easy/EasyStok/02%20-%20Engenharia/Multi-tenant.md) — nota de arquitetura.
- AWS SaaS Lens — RLS no padrão pool tenant.
- Stripe Engineering — RLS via `current_setting('app.account_id')`.
- ASVS V8 (data protection) — defesa em camadas.
