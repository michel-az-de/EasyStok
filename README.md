# 📦 EasyStok — Sistema SaaS de Gestão de Estoque

> Plataforma SaaS completa para gestão inteligente de estoque, construída em **.NET 9** com **Clean Architecture**, **DDD** e **CQRS**. Suporta **PostgreSQL** e **MongoDB** de forma intercambiável, com observabilidade avançada, multi-tenancy, autenticação JWT, analytics em tempo real e geração de anúncios via IA.

[![CI/CD Pipeline](https://github.com/michel-az-de/EasyStok/actions/workflows/ci.yml/badge.svg)](https://github.com/michel-az-de/EasyStok/actions/workflows/ci.yml)
![.NET 9](https://img.shields.io/badge/.NET-9.0-purple)
![PostgreSQL](https://img.shields.io/badge/DB-PostgreSQL-blue)
![MongoDB](https://img.shields.io/badge/DB-MongoDB-green)
![Redis](https://img.shields.io/badge/Cache-Redis-red)

---

## 📋 Índice

1. [Visão Geral](#-visão-geral)
2. [Arquitetura](#-arquitetura)
3. [Tecnologias](#-tecnologias)
4. [Qualidade de Código](#-qualidade-de-código)
5. [Estrutura de Projetos](#-estrutura-de-projetos)
6. [Como Rodar](#-como-rodar)
   - [Com PostgreSQL](#com-postgresql)
   - [Com MongoDB](#com-mongodb)
7. [Configuração Completa](#-configuração-completa)
8. [API — Endpoints Completos](#-api--endpoints-completos)
9. [Domínio — Entidades e Value Objects](#-domínio--entidades-e-value-objects)
10. [Testes Unitários e de Integração](#-testes-unitários-e-de-integração)
11. [Testes de Arquitetura](#-testes-de-arquitetura)
12. [Benchmarks de Performance](#-benchmarks-de-performance)
13. [Observabilidade](#-observabilidade)
14. [Como Implementar uma Nova Feature](#-como-implementar-uma-nova-feature)
15. [Padrões Obrigatórios do Projeto](#-padrões-obrigatórios-do-projeto)
16. [Arquivos Importantes](#-arquivos-importantes)
17. [CI/CD](#-cicd)
18. [Troubleshooting](#-troubleshooting)

---

## 🚀 Visão Geral

O **EasyStok** é um sistema **SaaS multi-tenant** para gestão de estoque com:

| Módulo | O que faz |
|--------|-----------|
| **Produtos** | CRUD completo com variações (cor, tamanho), embalagens, características, fotos e histórico |
| **Estoque** | Entradas, saídas, reposições, movimentações históricas e busca inteligente |
| **Vendas** | Registro de vendas multicanal (Loja Física, Online, Marketplace) com cálculo automático de totais |
| **Inteligência Operacional** | Alertas de estoque baixo/vencimento/parado, sugestões de reposição, projeção de ruptura |
| **Analytics** | Dashboard com receita, margem por produto, sazonalidade, rotatividade e movimentações |
| **IA Generativa** | Geração de descrições de anúncios via Claude (Anthropic) com streaming SSE |
| **Notificações** | Sistema de notificações push gerado por background service automático |
| **Multi-Tenancy** | Isolamento completo por empresa (EmpresaId em todas as queries) |
| **Autenticação** | JWT com refresh tokens, 4 níveis de acesso (SuperAdmin, Admin, Gerente, Operador) |
| **Fornecedores** | CRUD de fornecedores com histórico e estatísticas de compras |
| **Planos SaaS** | Controle de limites por plano de assinatura |
| **Uploads** | Fotos de produtos, avatares de usuários, logos de lojas (local ou S3-compatible) |

---

## 🏗 Arquitetura

O projeto segue **Clean Architecture** com separação rigorosa de camadas, reforçada por **testes de arquitetura automáticos** (NetArchTest).

```
┌─────────────────────────────────────────────────────────────────┐
│                        EasyStock.Api                            │
│  Controllers · Middlewares · JWT Auth · Swagger · HealthChecks  │
│  Background Services · Rate Limiting · Global Exception Handler │
└───────────────────────────┬─────────────────────────────────────┘
                            │ depende de
┌───────────────────────────▼─────────────────────────────────────┐
│                    EasyStock.Application                        │
│   Use Cases (Commands/Queries) · Validators · Ports/Interfaces  │
│         DTOs · Mapping Extensions · Domain Event Handlers       │
└───────────────────────────┬─────────────────────────────────────┘
                            │ depende de
┌───────────────────────────▼─────────────────────────────────────┐
│                      EasyStock.Domain                           │
│    Entities · Value Objects · Domain Exceptions · Enums         │
│            Specifications · Domain Events                       │
│          ⚠️  ZERO dependências externas (puro C#)               │
└────────────────────────────────────────────────────────────────-┘
         ▲                    ▲                   ▲
         │ implementa         │ implementa         │ implementa
┌────────┴────────┐  ┌────────┴────────┐  ┌───────┴──────────────┐
│  Infra.Postgre  │  │  Infra.MongoDb  │  │    Infra.Async       │
│  EF Core + NPGSQL│  │  MongoDB Driver │  │  Redis · SMTP · S3   │
│  28 Repositórios│  │  Collections    │  │  Queue · FileStorage  │
│  Migrations EF  │  │  ClassMaps      │  │                       │
└─────────────────┘  └─────────────────┘  └──────────────────────┘
```

### Princípios Aplicados

- **Clean Architecture** — Dependências apontam sempre para dentro (Domain é o núcleo)
- **DDD (Domain-Driven Design)** — Entidades ricas, Value Objects, Specifications, Domain Exceptions
- **CQRS** — Commands (escrita) separados de Queries (leitura) em use cases distintos
- **Repository Pattern** — Abstração de acesso a dados via interfaces definidas no Application
- **Port & Adapter (Hexagonal)** — Application define portas; Infrastructure fornece adapters
- **Specification Pattern** — Regras de negócio encapsuladas em objetos reutilizáveis
- **Multi-Tenancy** — EmpresaId filtra todos os dados; nenhuma entidade escapa disso

---

## 🛠 Tecnologias

| Categoria | Tecnologia | Versão |
|-----------|-----------|--------|
| Runtime | .NET / ASP.NET Core | 9.0 |
| ORM | Entity Framework Core + Npgsql | 9.0 |
| NoSQL | MongoDB.Driver | 2.19 |
| Cache | Redis (StackExchange.Redis) | 9.0 |
| Mensageria | In-Memory Queue (dev) / Redis Queue (prod) | — |
| Validação | FluentValidation | 11.3 |
| Auth | JWT Bearer | 9.0 |
| Logs | Serilog + enrichers | 10.0 |
| Observabilidade | OpenTelemetry (OTEL) | 1.15 |
| Testes | xUnit + FluentAssertions + NetArchTest | — |
| Benchmarks | BenchmarkDotNet | 0.14 |
| IA | Anthropic Claude (SSE streaming) | — |
| Armazenamento | Local / AWS S3 / MinIO | — |
| Senhas | BCrypt.Net-Next | 4.0 |
| Documentação | Swagger / OpenAPI | 6.4 |

---

## ✅ Qualidade de Código

### O que garante a qualidade

| Mecanismo | O que valida |
|-----------|-------------|
| **Testes de Arquitetura** (NetArchTest) | Dependências de camada; Value Objects no Domain; Exceptions no Domain |
| **Testes Unitários** | Domain (Value Objects, Entidades, Specifications) + Application (Use Cases, Validators, Analytics) |
| **Testes de Integração** | Repositórios PostgreSQL e MongoDB contra banco real; fluxos completos de estoque |
| **FluentValidation** | Entrada de dados validada antes de chegar nos use cases |
| **Domain Exceptions** | 11 exceções de domínio tipadas mapeadas para HTTP pelo GlobalExceptionHandler |
| **Value Objects** | Primitivos substituídos por tipos ricos (`Dinheiro`, `Quantidade`, `Validade`, etc.) |
| **Rate Limiting** | Endpoints de IA: 10/min; Endpoints gerais: 200/min |
| **CI Automático** | Build + testes + benchmarks em cada push/PR via GitHub Actions |
| **BenchmarkDotNet** | Mede performance e alocação de memória dos repositórios mais críticos |

### Domain Exceptions (mapeadas para HTTP)

```
RegraDeDominioVioladaException     → 422 Unprocessable Entity
EstoqueInsuficienteException       → 422 Unprocessable Entity
ProdutoInativoException            → 422 Unprocessable Entity
ItemEstoqueBloqueadoException      → 422 Unprocessable Entity
ItemEstoqueVencidoException        → 422 Unprocessable Entity
QuantidadeInvalidaException        → 400 Bad Request
CredenciaisInvalidasException      → 401 Unauthorized
UsuarioNaoAutorizadoException      → 403 Forbidden
PlanoLimiteAtingidoException       → 402 Payment Required
VendaSemItensException             → 400 Bad Request
ConflitoConcorrenciaException      → 409 Conflict
```

---

## 📁 Estrutura de Projetos

```
EasyStok/
├── EasyStock.Api/                      # API REST (ASP.NET Core)
│   ├── Controllers/                    # 17 controllers REST
│   ├── Middlewares/                    # Exception handler, correlation ID
│   ├── BackgroundServices/             # AnalisadorEstoqueBackgroundService
│   ├── Configuration/                  # Extension methods de DI
│   └── Program.cs                      # Composition root
│
├── EasyStock.Application/              # Casos de Uso (Use Cases)
│   ├── UseCases/                       # 40+ use cases organizados por módulo
│   ├── Validators/                     # FluentValidation validators
│   ├── Ports/                          # Interfaces (IRepository, IEmailService, etc.)
│   ├── DTOs/                           # Data Transfer Objects
│   └── Extensions/                     # Mapping extensions
│
├── EasyStock.Domain/                   # Núcleo de negócio (sem dependências)
│   ├── Entities/                       # 14 entidades de domínio
│   ├── ValueObjects/                   # Dinheiro, Quantidade, Validade, CodigoSku, etc.
│   ├── Exceptions/                     # 11 domain exceptions tipadas
│   ├── Enums/                          # Enumerações de domínio
│   └── Specifications/                 # Especificações de regras de negócio
│
├── EasyStock.Infra.Postgre/            # PostgreSQL + EF Core
│   ├── Context/                        # EasyStockDbContext (42 DbSets)
│   ├── Configurations/                 # 27 EntityTypeConfiguration fluent API
│   ├── Repositories/                   # 28 repositórios concretos
│   └── Migrations/                     # Migrations EF Core
│
├── EasyStock.Infra.MongoDb/            # MongoDB alternativo
│   ├── Context/                        # MongoEasyStockContext
│   ├── ClassMaps/                      # BsonSerializer registrations
│   ├── Repositories/                   # MongoDB repositories
│   └── Migrations/                     # MongoMigrationHostedService
│
├── EasyStock.Infra.Async/              # Serviços assíncronos/externos
│   ├── Cache/                          # RedisCacheService
│   ├── Email/                          # SmtpEmailService
│   ├── Queue/                          # BackgroundQueueService (in-memory)
│   └── Storage/                        # LocalFileStorage / S3CompatibleFileStorage
│
├── EasyStock.Domain.Tests/             # Testes unitários de domínio
├── EasyStock.Application.Tests/        # Testes unitários de application
├── EasyStock.Api.UnitTests/            # Testes unitários da API
├── EasyStock.Api.IntegrationTests/     # Testes de integração da API
├── EasyStock.Infra.Postgre.IntegrationTests/   # Testes de integração PostgreSQL
├── EasyStock.Infra.MongoDb.IntegrationTests/   # Testes de integração MongoDB
├── EasyStock.ArchitectureTests/        # Testes de arquitetura (NetArchTest)
├── EasyStock.Benchmarks/              # Benchmarks BenchmarkDotNet
└── EasyStok.sln                       # Solution .NET
```

---

## 🚀 Como Rodar

### Pré-requisitos

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- PostgreSQL 14+ **ou** MongoDB 6+
- Redis (opcional — cache distribuído)
- `dotnet-ef` (para migrations PostgreSQL): `dotnet tool install --global dotnet-ef`

### Com PostgreSQL

**1. Clone o repositório**
```bash
git clone https://github.com/michel-az-de/EasyStok.git
cd EasyStok
```

**2. Configure a connection string** em `EasyStock.Api/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=EasyStockDb;Username=postgres;Password=postgres"
  },
  "Database": {
    "Provider": "PostgreSql"
  }
}
```

**3. Execute as migrations**
```bash
dotnet ef database update \
  --project EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj \
  --startup-project EasyStock.Api/EasyStock.Api.csproj
```

**4. Suba a API**
```bash
dotnet run --project EasyStock.Api/EasyStock.Api.csproj
```

**5. Acesse**
- Swagger UI: `https://localhost:5001/swagger`
- Health Check: `https://localhost:5001/health`

---

### Com MongoDB

**1. Configure** em `EasyStock.Api/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "MongoConnection": "mongodb://localhost:27017"
  },
  "Database": {
    "Provider": "MongoDB",
    "MongoDatabase": "EasyStockDbMongo"
  }
}
```

**2. Suba a API** (sem necessidade de migrations — o `MongoMigrationHostedService` cria coleções e índices automaticamente):
```bash
dotnet run --project EasyStock.Api/EasyStock.Api.csproj
```

> ℹ️ Ao trocar de provider, **nenhum código** precisa mudar — apenas a configuração. O DI registra automaticamente os repositórios corretos via `Database:Provider`.

---

### Com Redis (Cache Distribuído)

Adicione ao `appsettings.json`:
```json
{
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "EasyStock"
  }
}
```

---

### Via Variáveis de Ambiente

Todas as chaves do `appsettings.json` podem ser sobrescritas via variáveis de ambiente usando `__` como separador:

```bash
export ASPNETCORE_ENVIRONMENT=Production
export Database__Provider=PostgreSql
export ConnectionStrings__DefaultConnection="Host=db;Database=easystock;Username=app;Password=secret"
export Jwt__SecretKey="minha-chave-super-secreta-com-32-chars!!"
export Anthropic__Enabled=true
export Anthropic__ApiKey="sk-ant-..."
```

---

## ⚙️ Configuração Completa

```json
// EasyStock.Api/appsettings.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=EasyStockDb;Username=postgres;Password=postgres",
    "MongoConnection": "mongodb://localhost:27017"
  },
  "Database": {
    "Provider": "PostgreSql",           // "PostgreSql" ou "MongoDB"
    "MongoDatabase": "EasyStockDbMongo"
  },
  "Jwt": {
    "Issuer": "EasyStock",
    "Audience": "EasyStock",
    "SecretKey": "EasyStock-SuperSecretKey-Min32Chars!!",  // Mínimo 32 chars
    "ExpirationMinutes": 60
  },
  "Anthropic": {
    "Enabled": false,                   // true para habilitar IA
    "ApiKey": ""                        // Chave da API Anthropic
  },
  "FileStorage": {
    "Provider": "Local",                // "Local" ou "S3"
    "LocalRootPath": "uploaded-files",
    "PublicBaseUrl": "/files",
    "S3": {
      "BucketName": "",
      "ServiceUrl": "",                 // MinIO endpoint ou vazio para AWS
      "Region": "us-east-1",
      "AccessKey": "",
      "SecretKey": "",
      "ForcePathStyle": true,
      "PublicBaseUrl": ""
    }
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "InstanceName": "EasyStock"
  },
  "Smtp": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "",
    "Password": "",
    "FromEmail": "noreply@easystock.com",
    "FromName": "EasyStock",
    "EnableSsl": true
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:3000", "http://localhost:5173"]
  },
  "OpenTelemetry": {
    "OtlpEndpoint": "http://localhost:4317"   // Jaeger, Tempo, etc.
  },
  "EasyStock": {
    "LimiteEstoqueBaixoDefault": 5,
    "DiasAlertaVencimento": 30,
    "DiasItemParado": 90
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "System": "Warning" }
    }
  }
}
```

---

## �� API — Endpoints Completos

Todos os endpoints exigem `Authorization: Bearer {token}` exceto `/api/auth/login`, `/api/auth/register` e `/api/empresas/registrar`.

A paginação é **obrigatória** em todas as listagens (`?page=1&pageSize=20`).

### 🔐 Autenticação — `/api/auth`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `POST` | `/api/auth/login` | Público | Login com email/senha → `{token, refreshToken, expiresIn}` |
| `POST` | `/api/auth/register` | Público | Cadastro de usuário |
| `POST` | `/api/auth/refresh` | Público | Renovar token via refresh token |
| `POST` | `/api/auth/logout` | Autenticado | Invalida refresh token |
| `POST` | `/api/auth/forgot-password` | Público | Envia email de reset de senha |
| `POST` | `/api/auth/reset-password` | Público | Redefine senha com token de reset |
| `GET` | `/api/auth/me` | Autenticado | Retorna dados do usuário atual |
| `PATCH` | `/api/auth/me` | Autenticado | Atualiza perfil (nome, avatar) |
| `PATCH` | `/api/auth/me/password` | Autenticado | Altera própria senha |

---

### 📦 Produtos — `/api/produtos`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/produtos?empresaId&page&pageSize&sort&order` | Operador+ | Lista paginada de produtos |
| `GET` | `/api/produtos/{id}?empresaId` | Operador+ | Detalhes do produto (com variações, fotos) |
| `GET` | `/api/produtos/search?empresaId&termo` | Operador+ | Busca inteligente por nome/SKU/código de barras |
| `POST` | `/api/produtos` | Gerente+ | Cadastra produto com variações e características |
| `PATCH` | `/api/produtos/{id}` | Gerente+ | Atualiza produto |
| `DELETE` | `/api/produtos/{id}?empresaId` | Admin | Remove produto |
| `GET` | `/api/produtos/{id}/historico?empresaId` | Operador+ | Histórico de movimentações do produto |
| `GET` | `/api/produtos/{id}/estatisticas?empresaId` | Gerente+ | Estatísticas de vendas e estoque |
| `POST` | `/api/produtos/{id}/variacoes` | Gerente+ | Adiciona variação (cor, tamanho, etc.) |
| `PATCH` | `/api/produtos/{id}/variacoes/{vid}` | Gerente+ | Atualiza variação |
| `DELETE` | `/api/produtos/{id}/variacoes/{vid}?empresaId` | Admin | Remove variação |
| `POST` | `/api/produtos/{id}/fotos` | Gerente+ | Upload de foto do produto |
| `DELETE` | `/api/produtos/{id}/fotos/{fotoId}?empresaId` | Gerente+ | Remove foto do produto |

---

### 🏪 Estoque — `/api/estoque`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/estoque?empresaId&page&pageSize` | Operador+ | Lista itens de estoque paginados |
| `GET` | `/api/estoque/buscar?empresaId&termo&limite` | Operador+ | Busca inteligente por item de estoque |
| `GET` | `/api/estoque/{id}?empresaId` | Operador+ | Detalhes do item de estoque |
| `POST` | `/api/estoque/entrada` | Operador+ | Registra entrada de estoque (quantidade, custo, lote, validade) |
| `POST` | `/api/estoque/saida` | Operador+ | Registra saída de estoque |
| `GET` | `/api/estoque/{id}/para-reposicao?empresaId` | Gerente+ | Dados do item para pedido de reposição |
| `POST` | `/api/estoque/reposicao` | Gerente+ | Repõe estoque de item |

---

### 🛒 Vendas — `/api/vendas`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/vendas?empresaId&page&pageSize` | Operador+ | Lista vendas paginadas |
| `GET` | `/api/vendas/{id}?empresaId` | Operador+ | Detalhes da venda com itens |

Canais de venda: `MarketplaceOrigen`, `LojaFisica`, `Online`

---

### 🧠 Inteligência Operacional — `/api/inteligencia`

> Rate limit: **10 requests/min**

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/inteligencia/estoque-baixo?empresaId&lojaId&limite&page&pageSize` | Gerente+ | Itens com estoque abaixo do limite |
| `GET` | `/api/inteligencia/proximo-vencimento?empresaId&dias&page&pageSize` | Gerente+ | Itens próximos do vencimento |
| `GET` | `/api/inteligencia/parados?empresaId&diasSemMovimento&page&pageSize` | Gerente+ | Itens sem movimentação |
| `GET` | `/api/inteligencia/sugestao-reposicao?empresaId&limiteQuantidade&page` | Gerente+ | Sugestões automáticas de reposição |
| `GET` | `/api/inteligencia/rotatividade?empresaId&produtoId&diasHistorico` | Gerente+ | Taxa de rotatividade (diária/semanal/mensal) |
| `GET` | `/api/inteligencia/sazonalidade?empresaId&produtoId&meses` | Gerente+ | Análise de sazonalidade mensal |
| `GET` | `/api/inteligencia/projecao-ruptura?empresaId&page&pageSize` | Gerente+ | Projeção de quando o estoque vai zerar |
| `GET` | `/api/inteligencia/board?empresaId&periodo` | Gerente+ | Dashboard resumo da inteligência |

---

### 📊 Analytics — `/api/analytics`

> Rate limit: **200 requests/min**

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/analytics/dashboard?empresaId&periodo` | Operador+ | Resumo do dashboard |
| `GET` | `/api/analytics/projecoes?empresaId&diasHistorico&page` | Gerente+ | Projeções de ruptura detalhadas |
| `GET` | `/api/analytics/reposicao?empresaId&diasHistorico&page` | Gerente+ | Sugestões de reposição detalhadas |
| `GET` | `/api/analytics/sazonalidade?empresaId&produtoId&meses` | Gerente+ | Sazonalidade de vendas |
| `GET` | `/api/analytics/alertas?empresaId&lojaId&dias&page` | Operador+ | Alertas de validade |
| `GET` | `/api/analytics/receita?empresaId&meses` | Gerente+ | Receita por período |
| `GET` | `/api/analytics/margem?empresaId&dias&page` | Gerente+ | Margem por produto |
| `GET` | `/api/analytics/movimentacoes?empresaId&de&ate&tipo&diasPadrao` | Operador+ | Resumo de movimentações |
| `GET` | `/api/analytics/validade?empresaId&lojaId&dias&page` | Operador+ | Análise de validade de itens |
| `GET` | `/api/analytics/parados?empresaId&diasSemMovimento&page` | Gerente+ | Itens parados detalhados |
| `GET` | `/api/analytics/vendas-por-canal?empresaId&de&ate` | Gerente+ | Vendas por canal (Online/Física/Marketplace) |

---

### 📋 Movimentações — `/api/movimentacoes`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/movimentacoes?empresaId&de&ate&tipo&page` | Operador+ | Histórico de movimentações com filtros |
| `GET` | `/api/movimentacoes/item/{itemEstoqueId}` | Operador+ | Movimentações de um item específico |

Tipos: `Entrada`, `Saida`, `Ajuste`, `Devolucao`

---

### 🔔 Notificações — `/api/notificacoes`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/notificacoes?empresaId&lida&tipo&page` | Operador+ | Lista notificações com filtros |
| `GET` | `/api/notificacoes/badge?empresaId` | Operador+ | Contador de não lidas |
| `PUT` | `/api/notificacoes/{id}/marcar-lida` | Operador+ | Marca notificação como lida |
| `PUT` | `/api/notificacoes/marcar-todas-lidas?empresaId` | Operador+ | Marca todas como lidas |
| `DELETE` | `/api/notificacoes/{id}` | Operador+ | Remove notificação |

Tipos de alerta: `EstoqueBaixo`, `ProximoVencimento`, `Parado`

---

### 👥 Usuários — `/api/usuarios`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/usuarios?empresaId&page&pageSize` | Admin | Lista usuários da empresa |
| `POST` | `/api/usuarios` | Admin | Cria novo usuário |
| `PATCH` | `/api/usuarios/{id}` | Admin | Atualiza dados do usuário |
| `PATCH` | `/api/usuarios/{id}/password` | Admin | Altera senha do usuário |
| `PUT` | `/api/usuarios/{id}/ativar` | Admin | Ativa usuário |
| `PUT` | `/api/usuarios/{id}/desativar` | Admin | Desativa usuário |
| `POST` | `/api/usuarios/{id}/perfis` | Admin | Atribui perfil/role ao usuário |

Níveis de acesso: `SuperAdmin > Admin > Gerente > Operador`

---

### 🏬 Lojas — `/api/lojas`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/lojas?empresaId` | Gerente+ | Lista lojas da empresa |
| `POST` | `/api/lojas` | Admin | Cria nova loja |
| `PUT` | `/api/lojas/{id}` | Gerente+ | Atualiza loja |
| `DELETE` | `/api/lojas/{id}?empresaId` | Admin | Desativa loja |

---

### 🚚 Fornecedores — `/api/fornecedores`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/fornecedores?empresaId&page&search&sort&order&ativo` | Operador+ | Lista fornecedores com busca e filtros |
| `GET` | `/api/fornecedores/{id}?empresaId` | Operador+ | Detalhes do fornecedor |
| `POST` | `/api/fornecedores` | Gerente+ | Cria fornecedor |
| `PUT` | `/api/fornecedores/{id}` | Gerente+ | Atualiza fornecedor |
| `DELETE` | `/api/fornecedores/{id}?empresaId` | Admin | Desativa fornecedor |
| `GET` | `/api/fornecedores/{id}/historico` | Operador+ | Histórico de pedidos do fornecedor |
| `GET` | `/api/fornecedores/{id}/estatisticas` | Gerente+ | Estatísticas de compras |

---

### 🗂 Categorias — `/api/categorias`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/categorias?empresaId` | Operador+ | Lista categorias |
| `GET` | `/api/categorias/{id}?empresaId` | Operador+ | Detalhes da categoria |
| `POST` | `/api/categorias` | Gerente+ | Cria categoria |
| `PUT` | `/api/categorias/{id}` | Gerente+ | Atualiza categoria |
| `DELETE` | `/api/categorias/{id}?empresaId` | Admin | Remove categoria |

---

### 💼 Planos — `/api/planos`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/planos` | Público | Lista planos de assinatura disponíveis |

---

### 🏢 Empresas — `/api/empresas`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `POST` | `/api/empresas/registrar` | Público | Registra nova empresa (onboarding SaaS) |

---

### ⚙️ Configurações de Loja — `/api/configuracoes`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `GET` | `/api/configuracoes?empresaId&lojaId` | Gerente+ | Obtém configurações da loja |
| `PATCH` | `/api/configuracoes` | Gerente+ | Atualiza configurações (limites de alerta, dias, etc.) |
| `POST` | `/api/configuracoes/reset` | Admin | Redefine configurações para padrão |

---

### 🤖 IA / Anúncios — `/api/ia`

> Rate limit: **10 requests/min**. Requer `Anthropic:Enabled = true`.

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `POST` | `/api/ia/anuncio` | Gerente+ | Gera descrição de anúncio via Claude **(SSE streaming)** |
| `POST` | `/api/ia/anuncio/rascunho` | Gerente+ | Salva rascunho de anúncio gerado |
| `GET` | `/api/ia/anuncios?empresaId&page` | Gerente+ | Lista anúncios gerados |
| `DELETE` | `/api/ia/anuncios/{id}` | Gerente+ | Remove anúncio |
| `GET` | `/api/ia/uso?empresaId` | Admin | Estatísticas de uso da IA |

**Streaming de anúncio:**
```http
POST /api/ia/anuncio
Content-Type: application/json
Accept: text/event-stream

{
  "produtoId": "guid",
  "produtoVariacaoId": "guid",       // opcional
  "instrucoesComplementares": "..."   // opcional
}

// Response (SSE):
data: {"chunk": "Produto incrível..."}
data: {"chunk": " ideal para..."}
data: [DONE]
```

---

### 📎 Uploads — `/api/uploads`

| Método | Rota | Acesso | Descrição |
|--------|------|--------|-----------|
| `POST` | `/api/uploads/produto/{id}/foto?empresaId` | Gerente+ | Upload de foto do produto |
| `POST` | `/api/uploads/usuario/avatar` | Autenticado | Upload de avatar do usuário |
| `POST` | `/api/uploads/loja/logo?lojaId` | Admin | Upload de logo da loja |

---

## 🏛 Domínio — Entidades e Value Objects

### Entidades Principais (14)

| Entidade | Agregado | Propriedades Chave |
|----------|----------|-------------------|
| `Produto` | Raiz | Id, Nome, Tipo(Fisico/Alimento/Servico), Status, SKU, CategoriaId, FotosJson |
| `ProdutoVariacao` | Produto | Nome, Cor, Tamanho, Dimensoes |
| `ProdutoEmbalagem` | Produto | Tipo, Quantidade, Dimensoes |
| `ProdutoCaracteristica` | Produto | Chave, Valor |
| `ItemEstoque` | Raiz | Id, ProdutoId, QuantidadeAtual, CodigoLote, Validade, Status |
| `Venda` | Raiz | Id, CanalVenda, DataVenda, ValorTotal, EmpresaId |
| `ItemVenda` | Venda | ItemEstoqueId, Quantidade, PrecoTotal |
| `MovimentacaoEstoque` | Raiz | Tipo, Natureza, Quantidade, Data |
| `Usuario` | Raiz | Nome, Email, SenhaHash, Ativo, FailedLoginAttempts, LockoutEnd |
| `Empresa` | Raiz | Nome, CNPJ, Ativa (tenant do SaaS) |
| `Loja` | Empresa | Nome, Endereco, EmpresaId |
| `Categoria` | Empresa | Nome, EmpresaId |
| `Fornecedor` | Empresa | Nome, Contato, CNPJ |
| `Perfil` | RBAC | Nome, NivelAcesso (SuperAdmin/Admin/Gerente/Operador) |

### Value Objects (6)

```csharp
Dinheiro    // decimal ≥ 0, suporta +, -, *, comparação
Quantidade  // int ≥ 0, suporta +, -, comparação
Validade    // DateOnly com validação de vencimento
CodigoSku   // formato SKU validado
CodigoLote  // código de lote validado
Dimensoes   // Largura, Altura, Profundidade (decimais positivos)
```

### Especificações

```csharp
ProdutoAtivoSpecification  // produto.Status == StatusProduto.Ativo
```

---

## 🧪 Testes Unitários e de Integração

### Executar todos os testes

```bash
dotnet test EasyStok.sln
```

### Executar por categoria

```bash
# Apenas testes unitários de domínio
dotnet test EasyStock.Domain.Tests/EasyStock.Domain.Tests.csproj

# Apenas testes unitários de application
dotnet test EasyStock.Application.Tests/EasyStock.Application.Tests.csproj

# Testes de integração PostgreSQL (requer banco rodando)
dotnet test EasyStock.Infra.Postgre.IntegrationTests/EasyStock.Infra.Postgre.IntegrationTests.csproj

# Testes de integração MongoDB (requer MongoDB rodando)
dotnet test EasyStock.Infra.MongoDb.IntegrationTests/EasyStock.Infra.MongoDb.IntegrationTests.csproj
```

### Testes com cobertura de código

```bash
dotnet test EasyStok.sln /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

### O que cada projeto testa

**`EasyStock.Domain.Tests`** — Value Objects e Entidades:
```
ValueObjects/QuantidadeTests.cs       → From(), Add(), Subtract(), negativo inválido
ValueObjects/DinheiroTests.cs         → precisão decimal, operações, invariante negativo
ValueObjects/ValidadeTests.cs         → criação, vencimento, validação
ValueObjects/CodigoSkuTests.cs        → formato SKU
ValueObjects/CodigoLoteTests.cs       → formato de lote
ValueObjects/DimensoesTests.cs        → largura/altura/profundidade positivos
Entities/ItemEstoqueTests.cs          → CriarParaEntrada(), lógica de domínio
Entities/VendaTests.cs                → AdicionarItem(), RecalcularValorTotal()
Specifications/ProdutoAtivoTests.cs   → specification pattern
```

**`EasyStock.Application.Tests`** — Use Cases e Lógica de Negócio:
```
UseCases/CadastrarProdutoUseCaseTests.cs
UseCases/GerenciarProdutoUseCaseTests.cs
UseCases/RegistrarEntradaEstoqueUseCaseTests.cs
UseCases/RegistrarSaidaEstoqueUseCaseTests.cs
UseCases/ReporEstoqueUseCaseTests.cs
UseCases/BuscarEstoqueInteligenteUseCaseTests.cs
UseCases/AutenticarUsuarioUseCaseTests.cs
UseCases/FornecedorUseCasesTests.cs
UseCases/RegistrarEmpresaUseCaseTests.cs
Analytics/AnalyticsCalculationTests.cs
```

**`EasyStock.Infra.Postgre.IntegrationTests`** — PostgreSQL real:
```
Repositories/ProdutoRepositoryIntegrationTests.cs
Repositories/ItemEstoqueRepositoryIntegrationTests.cs
Repositories/ProdutoVariacaoRepositoryIntegrationTests.cs
Workflows/EstoqueWorkflowsIntegrationTests.cs  → fluxo completo entrada→saída→notificação
Infrastructure/PostgreSqlDatabaseFixture.cs    → setup do banco de teste
```

**`EasyStock.Api.IntegrationTests`** — Serviços assíncronos:
```
Infrastructure/AsyncInfrastructureTests.cs → Email, Queue, Cache services
```

---

## 🏛 Testes de Arquitetura

O projeto contém testes que **forçam as regras de dependência de camada** via NetArchTest:

```bash
dotnet test EasyStock.ArchitectureTests/EasyStock.ArchitectureTests.csproj
```

**Regras validadas:**

```csharp
// Domain NÃO pode depender de nada externo
[Fact] Domain_Nao_Deve_Depender_De_Application_Infrastructure_Ou_Api()

// Application NÃO pode referenciar Infrastructure ou API
[Fact] Application_Nao_Deve_Depender_De_Infrastructure_Ou_Api()

// Infrastructure PODE depender de Domain e Application
[Fact] Infrastructure_Pode_Depender_De_Domain_E_Application()

// Value Objects DEVEM ficar no namespace Domain.ValueObjects
[Fact] ValueObjects_Devem_Ficar_No_Domain()

// Exceptions DEVEM ficar no namespace Domain.Exceptions
[Fact] Exceptions_De_Domain_Devem_Ficar_No_Domain()
```

Se você quebrar qualquer uma dessas regras, o CI falha automaticamente.

---

## ⚡ Benchmarks de Performance

```bash
dotnet run --project EasyStock.Benchmarks/EasyStock.Benchmarks.csproj --configuration Release
```

O projeto mede a performance de `ProdutoRepository.GetProdutosPaginadosAsync` com **1.000 produtos em memória**, usando `BenchmarkDotNet` com `[MemoryDiagnoser]`:

- Tempo médio de execução
- Alocação de memória por chamada
- Eficácia do cache in-memory

---

## 📡 Observabilidade

### Health Check

```http
GET /health
```

Retorna status de PostgreSQL/MongoDB e demais serviços em JSON.

### Logs Estruturados (Serilog)

Todos os logs incluem: `CorrelationId`, `ThreadId`, `ProcessId`, `MachineName`, `Application`.

O header `X-Correlation-Id` é propagado em todas as requisições para rastreamento.

### OpenTelemetry (Traces e Métricas)

Configure o collector OTLP em `appsettings.json`:

```json
"OpenTelemetry": {
  "OtlpEndpoint": "http://localhost:4317"
}
```

Instrumentações ativas: ASP.NET Core, HTTP Client, .NET Runtime.

Compatible com **Jaeger**, **Grafana Tempo**, **Zipkin**, **Datadog**, etc.

### Background Service de Análise

O `AnalisadorEstoqueBackgroundService` roda continuamente e:
- Detecta itens com estoque baixo → gera `Notificacao` com `TipoAlertaEstoque.EstoqueBaixo`
- Detecta itens próximos do vencimento → gera `Notificacao` com `TipoAlertaEstoque.ProximoVencimento`
- Detecta itens parados → gera `Notificacao` com `TipoAlertaEstoque.Parado`

---

## 🔨 Como Implementar uma Nova Feature

Siga rigorosamente este fluxo para manter a consistência do projeto:

### Exemplo: Adicionar módulo de "Pedidos de Compra"

**Passo 1 — Domain** (se necessidade de nova entidade ou value object)
```
EasyStock.Domain/
  Entities/PedidoCompra.cs         ← Entidade com regras de negócio
  Enums/StatusPedidoCompra.cs      ← Enum no domain
  Exceptions/PedidoCompraException.cs  ← Exception tipada (se necessário)
```

**Passo 2 — Application** (interfaces e use cases)
```
EasyStock.Application/
  Ports/IPedidoCompraRepository.cs         ← Interface do repositório
  UseCases/PedidoCompra/
    CriarPedidoCompraUseCase.cs            ← Use case + command
    ListarPedidosCompraUseCase.cs          ← Use case + query
  Validators/
    CriarPedidoCompraCommandValidator.cs   ← FluentValidation
```

**Passo 3 — Infrastructure** (implementação concreta)
```
EasyStock.Infra.Postgre/
  Configurations/PedidoCompraConfiguration.cs   ← Fluent API do EF
  Repositories/PedidoCompraRepository.cs         ← Implementa IPedidoCompraRepository
```

**Passo 4 — Migration** (PostgreSQL)
```bash
dotnet ef migrations add AddPedidosCompra \
  --project EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj \
  --startup-project EasyStock.Api/EasyStock.Api.csproj
```

**Passo 5 — API** (controller REST)
```
EasyStock.Api/
  Controllers/PedidoCompraController.cs   ← Herda de EasyStockControllerBase
```

**Passo 6 — DI Registration**
```csharp
// EasyStock.Infra.Postgre/Extensions ou DependencyInjection.cs
services.AddScoped<IPedidoCompraRepository, PedidoCompraRepository>();

// EasyStock.Api/Configuration ou Program.cs
services.AddScoped<CriarPedidoCompraUseCase>();
services.AddScoped<ListarPedidosCompraUseCase>();
```

**Passo 7 — Testes** (obrigatório)
```
EasyStock.Domain.Tests/Entities/PedidoCompraTests.cs
EasyStock.Application.Tests/UseCases/CriarPedidoCompraUseCaseTests.cs
EasyStock.Infra.Postgre.IntegrationTests/Repositories/PedidoCompraRepositoryTests.cs
```

---

## 📐 Padrões Obrigatórios do Projeto

### 1. Convenções de Nomenclatura

| Tipo | Padrão | Exemplo |
|------|--------|---------|
| Entidades | PascalCase, substantivo | `Produto`, `ItemEstoque` |
| Value Objects | PascalCase, sealed record | `Dinheiro`, `Quantidade` |
| Exceptions | `{Nome}Exception` | `EstoqueInsuficienteException` |
| Use Cases | `{Acao}{Entidade}UseCase` | `CadastrarProdutoUseCase` |
| Commands | `{Acao}{Entidade}Command` | `CadastrarProdutoCommand` |
| Queries | `{Obter/Listar}{Entidade}Query` | `ListarProdutosQuery` |
| Repositories | `I{Entidade}Repository` | `IProdutoRepository` |
| Validators | `{Command}Validator` | `CadastrarProdutoCommandValidator` |
| Controllers | `{Modulo}Controller` | `ProdutoController` |

### 2. Regras do Domain

- **Zero dependências externas** no Domain — sem NuGet, sem referências a Application/Infra/API
- Value Objects devem ser `sealed record` com validações no construtor
- Entidades expõem comportamento (métodos ricos), não apenas propriedades
- Regras de negócio = Specification ou método na entidade, nunca na API/Application
- Sempre lançar uma `DomainException` tipada, nunca `Exception` genérica
- Enums ficam em `EasyStock.Domain/Enums/`

### 3. Regras do Application

- Use cases implementam a interface `IUseCase<TCommand, TResult>`
- Sem acesso a HTTP, banco de dados ou qualquer infraestrutura diretamente
- Toda entrada validada com FluentValidation antes de executar o use case
- Repositórios acessados apenas via interface definida em `Application/Ports/`
- Sem chamadas estáticas; injeção de dependência via construtor

### 4. Regras da API

- Controllers herdam de `EasyStockControllerBase`
- Use os helpers de resposta: `DataOk()`, `DataPaged()`, `DataCreated()`, `DataNotFound()`, `DataBadRequest()`
- Toda listagem usa paginação obrigatória (`page`, `pageSize`)
- Autorização por role em todos os endpoints (nunca endpoint público sem `[AllowAnonymous]` explícito)
- Retornos seguem `ProblemDetails` para erros (gerenciado pelo `GlobalExceptionHandler`)

### 5. Regras de Testes

- Testes unitários: sem acesso a banco ou rede — use mocks para repositórios
- Testes de integração: usar a `PostgreSqlDatabaseFixture` para setup/teardown automático
- Testes de arquitetura: **não modifique** o `EasyStock.ArchitectureTests` sem justificativa
- Toda nova entidade/use case deve ter ao menos um teste unitário
- Use `FluentAssertions` para asserções (não `Assert.Equal`)

### 6. Padrões de Commit

```
feat:     Nova funcionalidade
fix:      Correção de bug
refactor: Refatoração sem mudança de comportamento
test:     Adiciona ou modifica testes
docs:     Documentação
chore:    Mudanças de build, CI, dependências
```

---

## 📂 Arquivos Importantes

| Arquivo | Descrição |
|---------|-----------|
| `EasyStok.sln` | Solution principal — inclui todos os projetos |
| `EasyStock.Api/Program.cs` | Composition root — configura DI, middlewares, roteamento |
| `EasyStock.Api/appsettings.json` | Configuração principal (DB, JWT, Redis, SMTP, etc.) |
| `EasyStock.Infra.Postgre/Context/EasyStockDbContext.cs` | DbContext com 42 DbSets e lógica UoW |
| `EasyStock.Infra.Postgre/Configurations/` | 27 EntityTypeConfiguration com mapeamento fluent |
| `EasyStock.Infra.Postgre/Migrations/` | Histórico de migrations do banco PostgreSQL |
| `EasyStock.Domain/Entities/` | Entidades de domínio ricas |
| `EasyStock.Domain/ValueObjects/` | Value Objects tipados (Dinheiro, Quantidade, etc.) |
| `EasyStock.Domain/Exceptions/` | 11 exceções de domínio tipadas |
| `EasyStock.Application/Ports/` | Interfaces de repositórios e serviços externos |
| `EasyStock.Application/UseCases/` | 40+ use cases organizados por módulo |
| `EasyStock.ArchitectureTests/ArchitectureTests.cs` | Regras de dependência de camada (NetArchTest) |
| `EasyStock.Benchmarks/ProdutoRepositoryBenchmarks.cs` | Benchmark de performance de repositório |
| `.github/workflows/ci.yml` | Pipeline CI/CD (build + testes + benchmarks) |
| `ARCHITECTURE.md` | Diagrama de arquitetura em ASCII |

---

## ⚙️ CI/CD

O pipeline `.github/workflows/ci.yml` é acionado em **push para `master`** e em **Pull Requests**:

```
1. Checkout do código
2. Setup .NET 9 SDK
3. dotnet restore
4. dotnet build --no-restore
5. dotnet test --no-build (com PostgreSQL 15 em service container)
6. dotnet run --project EasyStock.Benchmarks --configuration Release
```

O banco PostgreSQL de teste é provisionado automaticamente via `services.postgres` do GitHub Actions. A connection string de teste é injetada via variável de ambiente `ConnectionStrings__DefaultConnection`.

---

## 🔧 Troubleshooting

### Erro de conexão com PostgreSQL
```
Verifique se o serviço está rodando: pg_isready -h localhost -p 5432
Verifique a connection string em appsettings.json
Execute: dotnet ef database update para aplicar migrations pendentes
```

### Erro de conexão com MongoDB
```
Verifique: mongosh --eval "db.adminCommand({ping: 1})"
Verifique a MongoConnection string em appsettings.json
O MongoMigrationHostedService cria as coleções automaticamente
```

### Testes falhando localmente
```bash
dotnet clean
dotnet restore
dotnet build
dotnet test
```

### JWT inválido / 401 em todos os endpoints
```
Verifique se Jwt:SecretKey tem pelo menos 32 caracteres
Verifique se Jwt:Issuer e Jwt:Audience batem com o token gerado
Use o endpoint POST /api/auth/login para obter um token válido
```

### Migrations pendentes
```bash
dotnet ef migrations list \
  --project EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj \
  --startup-project EasyStock.Api/EasyStock.Api.csproj

dotnet ef database update \
  --project EasyStock.Infra.Postgre/EasyStock.Infra.Postgre.csproj \
  --startup-project EasyStock.Api/EasyStock.Api.csproj
```

### IA não funcionando (404/stub response)
```
Certifique-se que Anthropic:Enabled = true no appsettings.json
Certifique-se que Anthropic:ApiKey está preenchido com uma chave válida
Sem a chave, o sistema usa o stub (retorna resposta vazia sem erro)
```

---

## 📄 Licença

Distribuído sob licença MIT. Consulte [LICENSE](LICENSE) para mais detalhes.

---

<div align="center">
  Desenvolvido com ❤️ para otimizar a gestão de estoque
</div>
