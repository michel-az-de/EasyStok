# EasyStok - Sistema de Gestão de Estoque SaaS

EasyStok é uma plataforma SaaS completa para gestão inteligente de estoque, desenvolvida em .NET 9 com Clean Architecture. Oferece controle de produtos, itens de estoque, vendas, movimentações e alertas inteligentes, com suporte a multi-tenancy, autenticação JWT e observabilidade avançada.

## ?? Funcionalidades

### Core
- **Gestão de Produtos**: CRUD completo com variações, embalagens e características.
- **Controle de Estoque**: Entradas, saídas, reposições e movimentações históricas.
- **Vendas**: Registro de vendas com itens, cálculo automático de totais.
- **Inteligência Operacional**: Busca inteligente, alertas de estoque baixo/vencimento, sugestões de reposição.

### SaaS
- **Multi-Tenancy**: Isolamento por empresa, com usuários, perfis e planos.
- **Autenticação**: JWT com roles (Admin, Gerente, Operador).
- **Planos e Assinaturas**: Controle de limites por plano.

### Observabilidade
- **Health Checks**: Monitoramento de DB e serviços.
- **Métricas**: OpenTelemetry para tracing e métricas de performance.
- **Logs**: Estruturados com Serilog e correlação de requests.
- **Background Services**: Análise automática de estoque.

### Infraestrutura
- **Bancos de Dados**: Suporte a PostgreSQL e MongoDB.
- **Cache**: In-memory para queries frequentes.
- **Paginação**: Obrigatória em todas as listagens.
- **Validações**: FluentValidation para entrada de dados.

## ??? Arquitetura

O projeto segue Clean Architecture, dividido em camadas. Veja [ARCHITECTURE.md](ARCHITECTURE.md) para diagramas detalhados.

### Diagrama de Dependências
```
API ? Application ? Domain ? Infrastructure
     ?              ?
     ???? Tests ?????
```

- **Domain**: Regras de negócio, independente de frameworks.
- **Application**: Casos de uso, interfaces para infraestrutura.
- **Infrastructure**: Implementações concretas (DB, external APIs).
- **API**: Exposição via REST, middlewares.

### Tecnologias
- **Backend**: .NET 9, ASP.NET Core.
- **DB**: PostgreSQL (primário), MongoDB (alternativo).
- **Testes**: xUnit, FluentAssertions, NetArchTest.
- **Observabilidade**: OpenTelemetry, Serilog.
- **Autenticação**: JWT Bearer.
- **Validação**: FluentValidation.

## ?? Pré-requisitos

- .NET 9 SDK
- PostgreSQL ou MongoDB
- (Opcional) Redis para cache distribuído

## ?? Como Rodar

1. **Clone o repositório**:
   ```bash
   git clone https://github.com/michel-az-de/EasyStok.git
   cd EasyStok
   ```

2. **Configure o banco**:
   - Para PostgreSQL: Atualize `appsettings.json` com connection string.
   - Para MongoDB: Defina `Database:Provider` como "MongoDB".

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

## ?? Testes

Execute todos os testes:
```bash
dotnet test EasyStok.sln
```

- **Unitários**: Domain e Application.
- **Integração**: Infraestrutura com DB real.
- **Arquitetura**: Validação de dependências.

## ?? Documentação da API

Acesse `/swagger` para documentação interativa.

### Endpoints Principais
- `GET /api/produtos` - Lista produtos paginados.
- `POST /api/produtos` - Cadastra produto.
- `GET /api/estoque` - Lista itens de estoque.
- `POST /api/estoque/entrada` - Registra entrada.
- `GET /api/inteligencia/estoque-baixo` - Alertas de estoque baixo.

## ?? Configuração

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
- `ASPNETCORE_ENVIRONMENT`: Development/Production.
- `Database__ConnectionString`: Override da connection string.

## ?? Contribuição

1. Fork o projeto.
2. Crie uma branch: `git checkout -b feature/nova-feature`.
3. Commit suas mudanças: `git commit -m 'feat: adiciona nova feature'`.
4. Push: `git push origin feature/nova-feature`.
5. Abra um Pull Request.

### Padrões de Commit
- `feat:` - Nova funcionalidade.
- `fix:` - Correção de bug.
- `refactor:` - Refatoração de código.
- `test:` - Adição/modificação de testes.

## ?? Monitoramento

- **Health Checks**: Verifique `/health` para status de DB.
- **Métricas**: Configure OTLP para coletar métricas de performance.
- **Logs**: Estruturados com correlação ID para rastreamento.

## ?? Troubleshooting

- **Erro de DB**: Verifique connection string e se o DB está rodando.
- **Testes falhando**: Execute `dotnet clean` e `dotnet restore`.
- **Auth issues**: Certifique-se de que JWT secret está configurado.

## ?? Licença

Este projeto é licenciado sob MIT. Veja [LICENSE](LICENSE) para detalhes.

---

Desenvolvido com ?? para otimizar gestão de estoque.