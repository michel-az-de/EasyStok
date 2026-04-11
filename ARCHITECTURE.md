# Diagrama de Arquitetura - EasyStok

## Visão Geral

```text
+---------------------+    +------------------------+    +-------------------+
| API Layer           |    | Application Layer      |    | Domain Layer      |
|                     |    |                        |    |                   |
| - Controllers       |--->| - Use Cases            |--->| - Entities        |
| - Middlewares       |    | - Validators           |    | - Value Objects   |
| - Swagger           |    | - Ports/Adapters       |    | - Specifications  |
| - Auth/JWT          |    |                        |    | - Domain Events   |
+---------------------+    +------------------------+    +-------------------+
          |                            |                            |
          v                            v                            v
+---------------------+    +------------------------+    +-------------------+
| Infrastructure      |    | External Services      |    | Tests             |
| Layer               |    |                        |    |                   |
|                     |    | - PostgreSQL           |    | - Unit Tests      |
| - Repositories      |    | - MongoDB              |    | - Integration     |
| - DB Context        |    | - Redis (Cache)        |    | - Architecture    |
| - Migrations        |    | - OpenTelemetry        |    |                   |
| - External APIs     |    |                        |    |                   |
+---------------------+    +------------------------+    +-------------------+
```

## Fluxo de Dados

1. **Request**: Controller (API) -> Use Case (Application) -> Repository (Infrastructure) -> Banco.
2. **Response**: Banco -> Repository -> Use Case -> Controller.
3. **Domain Events**: Domain/Application -> Handlers -> External Services.

## Dependências

- **API** depende de **Application** e **Infrastructure**.
- **Application** depende de **Domain**.
- **Infrastructure** depende de **Domain** e **Application**.
- **Domain** é independente.
- **Tests** dependem das camadas necessárias para validação.

## Componentes-Chave

- **Entities**: Produto, ItemEstoque, Venda e agregados relacionados.
- **Value Objects**: Dinheiro, Quantidade, Validade e outros tipos de domínio.
- **Specifications**: Regras de negócio reutilizáveis.
- **Use Cases**: Lógica de aplicação e orquestração.
- **Repositories**: Abstração de persistência.
- **Middlewares**: Auth, logging, health checks e concerns transversais.
