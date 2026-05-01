# Convenções Ativas — EasyStock

## Arquitetura
- Clean Architecture estrita: `Domain` → `Application` → `Infra.*` → `Api`/`Web`/`Admin`
- `Application` é POCO: nada de `IConfiguration`, `HttpContext`, EF. Use `IOptions<T>` e ports/interfaces.
- `Domain` sem dependências externas. Entidades + ValueObjects + Enums + Exceptions.
- Infra tem 3 projetos separados: `Infra.Postgre` (EF Core), `Infra.MongoDb` (legado/feature flag), `Infra.Async` (jobs/HTTP externo).

## Multi-tenant
- Filtro **manual** por `EmpresaId` em toda query. **NÃO** usar EF Global Query Filter (já causou bugs).
- Sempre `WHERE EmpresaId = @empresaId AND ...` no repo. Se esquecer, vaza tenant.
- IDs de FK validados contra `EmpresaId` antes de salvar (cliente, loja, produto).

## Concorrência
- `xmin` (Postgres system column) como RowVersion em entidades-chave: `Pedido`, `Produto`, `ItemEstoque`, `AssinaturaEmpresa`.
- Configuração via Fluent API: `.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin")`. Sem migration (system column).
- `FOR UPDATE` apenas dentro de `BeginTransactionAsync()`.

## Use Cases
- Um use case por arquivo: `XxxUseCase.cs` + `XxxCommand.cs`/`XxxQuery.cs` + `XxxResult.cs` no mesmo folder.
- Primary constructor injetando interfaces de repos + `IUnitOfWork` + `ILogger<T>`.
- Validação de input no início; lança `UseCaseValidationException` (mapeado pra 400).
- Idempotência via `MovimentacaoEstoque.DocumentoReferencia = "{pedidoId}:{itemId}"` + `ExisteReferenciaAsync`.

## Pedido (state machine)
- Status como `string` (não enum, evita migration ao adicionar): `"aguardando" | "preparando" | "pronto" | "entregue" | "cancelado"`.
- Matriz de transições explícita em `AtualizarStatusPedidoUseCase`. Inválida → exception.
- Transições de saída de estoque: ao entrar em `pronto` → desconta. Ao `cancelado` (se já descontou) → devolve.

## Testes
- xUnit + NSubstitute + FluentAssertions.
- Builders estáticos (`Build()`, `NovoPedido()`) no topo da classe de teste.
- Nome do teste em snake_case PT-BR descritivo: `Status_para_pronto_desconta_estoque_e_atualiza_status`.
- 457/457 verdes. Nunca commitar com teste vermelho.

## Estilo de código
- Sem comentário XML em método óbvio. Comentário **só** explica o "porquê" não-óbvio.
- C# 12+: primary constructors, collection expressions `[..]`, file-scoped namespaces.
- `record` pra DTO/Command/Query. `class sealed` pra entidade.
- ValueObject (ex: `Quantidade`) com `From(decimal)` factory + validação interna.

## DI
- Cada projeto Infra tem `ServiceCollectionExtensions.cs` com `AddXxxInfra(this IServiceCollection)`.
- Api/Web compõem em `Program.cs` chamando os Add* + `AddApiServices()` específico.
- Feature flag: registra `NoopXxxService` quando config vazia (ex: `EfiPixService` → `NoopEfiPixService`).

## Erros HTTP
- 400: `UseCaseValidationException` (input inválido)
- 401/403: auth/authz padrão
- 402: `PlanoLimiteAtingidoException` ou tenant suspenso (SubscriptionGate)
- 404: entidade não encontrada
- 409: `ConcurrencyException` (xmin conflict)
- 422: `EstoqueInsuficienteException`
- 500: tudo o resto (logado com correlationId)

## Git
- Commit ao final de cada demanda — sem pedir confirmação.
- Mensagem: `tipo(escopo): descrição em PT-BR`. Ex: `fix(pedidos): corrigir desconto de estoque para qty fracionária`.
- Nunca commitar `appsettings.*.json` com secret real.
