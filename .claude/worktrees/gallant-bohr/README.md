# EasyStok - Sistema de Gestão de Estoque SaaS

EasyStok é uma plataforma SaaS completa para gestão inteligente de estoque, desenvolvida em .NET 9 com Clean Architecture. Oferece controle de produtos, itens de estoque, vendas, movimentações e alertas inteligentes, com suporte a multi-tenancy, autenticação JWT e observabilidade avançada.

## Funcionalidades

### Core
- **Gestão de Produtos**: CRUD completo com variações, embalagens e características.
- **Controle de Estoque**: Entradas, saídas, reposições e movimentações históricas.
- **Vendas**: Registro de vendas com itens e cálculo automático de totais.
- **Inteligência Operacional**: Busca inteligente, alertas de estoque baixo e vencimento, além de sugestões de reposição.

### SaaS
- **Multi-Tenancy**: Isolamento por empresa, com usuários, perfis e planos.
- **Autenticação**: JWT com roles (`Admin`, `Gerente`, `Operador`).
- **Planos e Assinaturas**: Controle de limites por plano.

### Observabilidade
- **Health Checks**: Monitoramento de banco e serviços.
- **Métricas**: OpenTelemetry para tracing e métricas de performance.
- **Logs**: Estruturados com Serilog e correlação de requests.
- **Background Services**: Análise automática de estoque.

### Infraestrutura
- **Bancos de Dados**: Suporte a PostgreSQL e MongoDB.
- **Cache**: In-memory e Redis para consultas frequentes.
- **Paginação**: Obrigatória em todas as listagens.
- **Validações**: FluentValidation para entrada de dados.

## Arquitetura

O projeto segue Clean Architecture, dividido em camadas. Veja [ARCHITECTURE.md](ARCHITECTURE.md) para uma visão geral da organização.

### Diagrama de Dependências

```text
API -> Application -> Domain -> Infrastructure
      \-> Tests
```

- **Domain**: Regras de negócio, independente de frameworks.
- **Application**: Casos de uso e contratos para infraestrutura.
- **Infrastructure**: Implementações concretas de persistência, cache e serviços externos.
- **API**: Exposição via REST, middlewares e composição da aplicação.

### Tecnologias
- **Backend**: .NET 9, ASP.NET Core.
- **DB**: PostgreSQL (primário), MongoDB (alternativo).
- **Testes**: xUnit, FluentAssertions, NetArchTest.
- **Observabilidade**: OpenTelemetry, Serilog.
- **Autenticação**: JWT Bearer.
- **Validação**: FluentValidation.

## Pré-requisitos

- .NET 9 SDK
- PostgreSQL ou MongoDB
- (Opcional) Redis para cache distribuído

## Como Rodar

1. **Clone o repositório**:
   ```bash
   git clone https://github.com/michel-az-de/EasyStok.git
   cd EasyStok
   ```
2. **Configure o banco**:
   - Para PostgreSQL: atualize `appsettings.json` com a connection string.
   - Para MongoDB: defina `Database:Provider` como `MongoDB`.
3. **Rode as migrações** (PostgreSQL):
   ```bash
   dotnet ef database update --project EasyStock.Infra.Postgre
   ```
4. **Execute a aplicação**:
   ```bash
   dotnet run --project EasyStock.Api
   ```
5. **Acesse**:
   - API: `https://localhost:5001/swagger`
   - Health: `https://localhost:5001/health`

## Testes

Execute todos os testes:

```bash
dotnet test EasyStok.sln
```

- **Unitários**: Domain e Application.
- **Integração**: Infraestrutura com banco real.
- **Arquitetura**: Validação de dependências e higiene do projeto.

## Documentação da API

Acesse `/swagger` para documentação interativa.

### Endpoints Principais
- `GET /api/produtos` - Lista produtos paginados.
- `POST /api/produtos` - Cadastra produto.
- `GET /api/estoque` - Lista itens de estoque.
- `POST /api/estoque/entrada` - Registra entrada.
- `GET /api/inteligencia/estoque-baixo` - Alertas de estoque baixo.

## Configuração

### appsettings.json

```json
{
  "Database": {
    "Provider": "PostgreSql",
    "ConnectionString": "Host=localhost;Database=easystock;Username=user;Password=pass"
  },
  "Jwt": {
    "Issuer": "EasyStok",
    "Audience": "EasyStokUsers",
    "SecretKey": "your-secret-key"
  },
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### Variáveis de Ambiente
- `ASPNETCORE_ENVIRONMENT`: `Development` ou `Production`.
- `Database__ConnectionString`: override da connection string.

## Encoding

- Arquivos de texto e código devem ser salvos em UTF-8.
- O repositório usa `.editorconfig` para padronizar `charset = utf-8`.
- Ao revisar mudanças, prefira corrigir caracteres corrompidos imediatamente em vez de manter mojibake em comentários, documentação ou mensagens.

## Contribuição

1. Fork o projeto.
2. Crie uma branch: `git checkout -b feature/nova-feature`.
3. Commit suas mudanças: `git commit -m 'feat: adiciona nova feature'`.
4. Push: `git push origin feature/nova-feature`.
5. Abra um Pull Request.

### Padrões de Commit
- `feat:` - Nova funcionalidade.
- `fix:` - Correção de bug.
- `refactor:` - Refatoração de código.
- `test:` - Adição ou modificação de testes.

## Monitoramento

- **Health Checks**: verifique `/health` para o status de banco e serviços.
- **Métricas**: configure OTLP para coletar métricas de performance.
- **Logs**: estruturados com correlation ID para rastreamento.

## Troubleshooting

- **Erro de DB**: verifique a connection string e se o banco está rodando.
- **Testes falhando**: execute `dotnet clean` e `dotnet restore`.
- **Auth issues**: certifique-se de que o segredo JWT está configurado.

## Licença

Este projeto é licenciado sob MIT. Veja [LICENSE](LICENSE) para detalhes.

---

Desenvolvido para otimizar gestão de estoque.
