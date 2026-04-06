# 📊 EasyStok — Status do Projeto, Métricas e Roadmap

> Documento gerado em 06/04/2026. Atualizar a cada sprint ou release significativo.

---

## 📈 Métricas de Código

### Visão Geral

| Categoria | Arquivos .cs | Linhas de Código |
|-----------|-------------|-----------------|
| **Código de Produção** (sem migrations) | 247 | **15.991** |
| **Testes** (unit + integração + arch + bench) | 50 | **8.417** |
| **Migrations EF Core** (auto-gerado) | 22 | **16.053** |
| **Total do Projeto** | **380** | **40.461** |

> **Relação Testes/Produção:** 8.417 / 15.991 ≈ **52,6%** (excelente para backend de domínio rico)

---

### Por Projeto (Código de Produção, sem migrations)

| Projeto | Arquivos | Linhas | Propósito |
|---------|----------|--------|-----------|
| `EasyStock.Domain` | 76 | 2.023 | Entidades, Value Objects, Specs, Domain Events |
| `EasyStock.Application` | 113 | 4.553 | Use Cases, DTOs, Validators, Ports |
| `EasyStock.Api` | 36 | 3.685 | Controllers, Middlewares, Background Services |
| `EasyStock.Infra.Postgre` | 63 | 3.312 | Repositórios PostgreSQL, DbContext, Configs |
| `EasyStock.Infra.MongoDb` | 16 | 1.925 | Repositórios MongoDB alternativos |
| `EasyStock.Infra.Async` | 6 | 493 | Redis, S3, SMTP, Claude Streaming |
| **Total Produção** | **310** | **15.991** | |

### Por Projeto (Testes)

| Projeto | Arquivos | Linhas | Tipo de Teste |
|---------|----------|--------|---------------|
| `EasyStock.Domain.Tests` | 10 | 1.017 | Unit tests — entities, value objects, specs |
| `EasyStock.Application.Tests` | 13 | 2.745 | Unit tests — use cases, analytics |
| `EasyStock.Api.UnitTests` | 15 | 2.241 | Unit tests — controllers, services |
| `EasyStock.Api.IntegrationTests` | 2 | 349 | Integration — async infra (Redis, S3, SMTP) |
| `EasyStock.Infra.Postgre.IntegrationTests` | 5 | 1.413 | Integration — repositórios PostgreSQL |
| `EasyStock.Infra.MongoDb.IntegrationTests` | 2 | 497 | Integration — repositórios MongoDB |
| `EasyStock.ArchitectureTests` | 2 | 104 | Architecture — NetArchTest |
| `EasyStock.Benchmarks` | 1 | 51 | Performance — BenchmarkDotNet |
| **Total Testes** | **50** | **8.417** | |

---

## ✅ O Que Está Implementado e Funcional

### 🏗️ Arquitetura e Infraestrutura

| Item | Status | Observações |
|------|--------|-------------|
| Clean Architecture (4 camadas) | ✅ Completo | Domain → Application → Api/Infra, sem violações |
| DDD (Domain Driven Design) | ✅ Completo | Entidades ricas, VOs, specs, domain events |
| CQRS (Commands/Queries) | ✅ Completo | 40+ use cases bem separados |
| Multi-tenancy (EmpresaId) | ✅ Completo | Isolamento em todos os repos e queries |
| Dual Database (PostgreSQL + MongoDB) | ✅ Completo | Intercambiável via config `Database:Provider` |
| EF Core Migrations (PostgreSQL) | ✅ Completo | 11 migrations, 27 DbSets |
| Testes de Arquitetura (NetArchTest) | ✅ Completo | Regras de dependência entre camadas |
| GitHub Actions CI/CD | ✅ Completo | Build + test + benchmarks no push/PR |

### 🔐 Autenticação e Segurança

| Item | Status | Observações |
|------|--------|-------------|
| JWT Bearer Authentication | ✅ Completo | 60min access + 30 dias refresh |
| Refresh Token Rotation | ✅ Completo | Token rotacionado a cada uso |
| Password Reset por Email | ✅ Completo | Token 2h + SMTP |
| BCrypt Password Hashing | ✅ Completo | `BCrypt.Net-Next` com salt automático |
| Account Lockout (5 falhas = 15 min) | ✅ Completo | `FailedLoginAttempts` + `LockoutEnd` |
| Role-Based Auth (4 níveis) | ✅ Completo | SuperAdmin, Admin, Gerente, Operador |
| Rate Limiting (geral + IA) | ✅ Completo | 200 req/min geral, 10 req/min IA |
| Audit Log | ✅ Completo | Entidade `AuditLog` com CREATE/UPDATE/DELETE |
| CORS configurável | ✅ Completo | Permite configurar origens em appsettings |

### 📦 Módulo de Produtos

| Item | Status | Observações |
|------|--------|-------------|
| CRUD completo de Produtos | ✅ Completo | Com paginação, busca, ordenação |
| Variações de Produto (Cor/Tamanho) | ✅ Completo | SKU único por variação, dimensions |
| Características customizadas (chave-valor) | ✅ Completo | `ProdutoCaracteristica` |
| Embalagens/Unidades de medida | ✅ Completo | `ProdutoEmbalagem` (Caixa, Pallet, Kit) |
| Fotos de produto | ✅ Completo | Array de URLs em JSON, upload para S3/local |
| Categorias | ✅ Completo | CRUD, filtragem por empresa |
| Busca por texto (nome, marca, barcode, SKU) | ✅ Completo | Full-text via `ChavePesquisa` |
| Status do produto (Ativo/Inativo/Descontinuado) | ✅ Completo | Soft delete com status |

### 🗃️ Módulo de Estoque

| Item | Status | Observações |
|------|--------|-------------|
| Registro de Entrada de Estoque | ✅ Completo | Com natureza: Compra/Devolução/Ajuste/Transferência |
| Registro de Saída de Estoque | ✅ Completo | Com validações: bloqueado, vencido, insuficiente |
| Reposição de Estoque | ✅ Completo | Atualiza custo, preço, quantidade, validade |
| Busca Inteligente de Estoque | ✅ Completo | Multi-campo via `ChavePesquisa` |
| Histórico de Movimentações | ✅ Completo | Audit trail imutável |
| Status Automático (Ok/Warn/Critical/Slow/Vencido) | ✅ Completo | Recalculado por background service |
| Velocidade de Saída Diária | ✅ Completo | Calculada periodicamente |
| Dias sem Movimentação | ✅ Completo | Calculado automaticamente |
| Previsão de Zeramento (dias) | ✅ Completo | qty_atual / velocidade_diaria |
| Alertas de Estoque Baixo | ✅ Completo | `QuantidadeAtual < QuantidadeMinima` |
| Alertas de Vencimento | ✅ Completo | Items vencendo em 30 dias |
| Alertas de Item Parado (>90 dias) | ✅ Completo | Background job |
| Sugestões de Reposição | ✅ Completo | Baseado em velocidade + mínimo |
| Múltiplos Lotes (CodigoLote) | ✅ Completo | Value Object `CodigoLote` |
| Vinculação com Fornecedor | ✅ Completo | `FornecedorId` + `FornecedorNome` no item |
| Controle por Loja (Multi-store) | ✅ Completo | `LojaId` opcional por item |

### 💰 Módulo de Vendas

| Item | Status | Observações |
|------|--------|-------------|
| Registro de Venda | ✅ Completo | Multicanal: LojaFísica, Online, Marketplace |
| Itens de Venda | ✅ Completo | Cálculo automático de total |
| Gatilho automático de saída de estoque | ✅ Completo | `RegistrarSaidaEstoque` chamado ao vender |
| Nota Fiscal (número) | ✅ Completo | Campo `NumeroNotaFiscal` |
| Listagem de vendas com filtros | ✅ Completo | Por empresa, loja, período |

### 📊 Módulo de Analytics

| Item | Status | Observações |
|------|--------|-------------|
| Receita por período | ✅ Completo | Agrupada por dia/semana/mês/ano |
| Margem por produto | ✅ Completo | (preço - custo) / preço * 100 |
| Velocidade de saída (rotatividade) | ✅ Completo | Unidades/dia por produto |
| Alertas de Vencimento | ✅ Completo | Items vencendo em N dias |
| Items com movimento lento (>90 dias) | ✅ Completo | Valor em risco |
| Estatísticas por Fornecedor | ✅ Completo | Pedidos, valor gasto, média de lead time |
| Top Produtos por Vendas | ✅ Completo | Por quantidade e receita |
| Valor total do inventário | ✅ Completo | Soma(qty * custo) |
| Dashboard Executivo | ✅ Completo | Resumo de todas as métricas |
| Previsão de Ruptura | ✅ Completo | Projeção baseada em velocidade |

### 🤖 Módulo de IA Generativa

| Item | Status | Observações |
|------|--------|-------------|
| Geração de descrição de anúncio (Claude) | ✅ Completo | Streaming SSE via Anthropic API |
| Stub para desenvolvimento (sem API key) | ✅ Completo | `GeradorDescricaoAnuncioStub` |
| Salvar rascunhos de anúncios | ✅ Completo | `AnuncioIa` com status Rascunho/Publicado |
| Listagem e gestão de anúncios | ✅ Completo | CRUD completo |
| Rastreamento de uso da IA | ✅ Completo | `UsoIa`: tokens e requisições por empresa |
| Rate limiting para IA (10 req/min) | ✅ Completo | Via ASP.NET Rate Limiter |

### 🏢 Módulo Empresarial / SaaS

| Item | Status | Observações |
|------|--------|-------------|
| Multi-empresa (multi-tenant) | ✅ Completo | Isolamento por EmpresaId |
| Gestão de Lojas | ✅ Completo | CRUD + configurações |
| Configurações de Loja (tema, cores, logo) | ✅ Completo | `ConfiguracaoLoja` |
| Gestão de Usuários | ✅ Completo | CRUD, perfis, permissões |
| Perfis e Permissões (RBAC) | ✅ Completo | 4 níveis + PerfilPermissao |
| Planos SaaS | ✅ Completo | Entidade `Plano` com limites |
| Assinaturas de Empresa | ✅ Completo | `AssinaturaEmpresa` com status |
| Gestão de Fornecedores | ✅ Completo | CRUD + histórico + estatísticas |
| Pedidos a Fornecedores | ✅ Completo | `PedidoFornecedor` com status |

### 🔔 Módulo de Notificações

| Item | Status | Observações |
|------|--------|-------------|
| Notificações automáticas (background) | ✅ Completo | Gerado por `AnalisadorEstoqueBackgroundService` |
| Notificações de estoque baixo | ✅ Completo | Via domain events |
| Notificações de vencimento | ✅ Completo | Items próximos do vencimento |
| Notificações de item parado | ✅ Completo | > 90 dias sem movimento |
| Marcar como lida / deletar | ✅ Completo | API completa |
| Resumo de notificações (contadores) | ✅ Completo | Por tipo/status |

### 🗄️ Observabilidade e DevOps

| Item | Status | Observações |
|------|--------|-------------|
| OpenTelemetry (traces + metrics) | ✅ Completo | Exporta para OTLP (Jaeger, Grafana OTEL) |
| Serilog (logs estruturados) | ✅ Completo | Com correlation IDs e contexto |
| Health Checks (PostgreSQL + MongoDB) | ✅ Completo | `/health` endpoint |
| Correlation ID Middleware | ✅ Completo | Rastreia cada request |
| Swagger/OpenAPI (PT-BR + EN) | ✅ Completo | Com exemplos e autenticação JWT |
| Benchmarks de Performance | ✅ Completo | BenchmarkDotNet no CI |

---

## ❌ O Que Está Faltando / Pendente

### 🔴 Alta Prioridade (Bloqueia Go-Live)

| Feature | Esforço Estimado | Por quê é crítico |
|---------|-----------------|-------------------|
| **Webhooks outbound** | 3–5 dias | Integrações com ERPs, ecommerce, etc. |
| **Importação em massa (CSV/Excel)** | 3–4 dias | Onboarding de clientes com catálogo existente |
| **Exportação de relatórios (Excel/PDF)** | 2–3 dias | Necessidade básica para usuários não-técnicos |
| **Billing / Pagamento de Assinatura** | 5–8 dias | Integração com Stripe/PagSeguro/Asaas |
| **Limites de plano enforçados** | 2–3 dias | Plano existe mas limites não são validados no código |
| **Transferência entre lojas** | 3–4 dias | Move estoque de uma loja para outra |
| **Docker Compose completo** | 1 dia | Arquivo docker-compose.yml com todos os serviços |

### 🟡 Média Prioridade (Importante para Produto Completo)

| Feature | Esforço Estimado | Descrição |
|---------|-----------------|-----------|
| **Geração de Código de Barras** | 1–2 dias | Gerar EAN/QR Code para produtos sem barcode |
| **Ranking / Avaliação de Fornecedores** | 2–3 dias | Nota de qualidade, pontualidade, devoluções |
| **Processo de Devolução (RMA)** | 3–5 dias | Fluxo formal de retorno/troca de produtos |
| **Multi-moeda** | 2–3 dias | `Dinheiro` VO só suporta BRL atualmente |
| **Localização de Estoque (bin/prateleira)** | 2–3 dias | Posição física dentro do armazém |
| **API Keys para integração** | 2–3 dias | Alternativa ao JWT para integrações máquina-máquina |
| **Previsão por ML** | 5–10 dias | Demanda preditiva além da velocidade atual |
| **Console de Superadmin** | 3–5 dias | Painel para gerenciar todas as empresas/planos |
| **Relatório Mensal (email automático)** | 2 dias | `RelatorioMensalJob` existe mas precisa de template HTML |
| **2FA (Two-Factor Authentication)** | 2–3 dias | TOTP (Google Authenticator) para maior segurança |

### 🟢 Baixa Prioridade (Nice to Have)

| Feature | Esforço Estimado | Descrição |
|---------|-----------------|-----------|
| **App Mobile** | 30–60 dias | React Native ou MAUI consumindo a API existente |
| **Modo Offline / Sync** | 10–20 dias | PWA ou app que funciona sem internet |
| **Consignação de Estoque** | 3–5 dias | Rastrear estoque enviado a terceiros |
| **Multi-idioma (i18n)** | 3–5 dias | Tradução dinâmica da API |
| **Plugin de marketplace** | 5–10 dias | Integração direta com Mercado Livre, Amazon, Shopify |
| **AI Demand Forecasting** | 8–15 dias | Claude ou modelo próprio para previsão de demanda |
| **Dashboard de NPS / Feedback** | 3–5 dias | Coleta de satisfação interna |

---

## ⏱️ Estimativa de Esforço (Dev Sênior Humano)

### Trabalho Já Realizado (Estimativa Retroativa)

| Módulo | Horas Estimadas |
|--------|----------------|
| Arquitetura base (Clean Arch + DDD + CQRS setup) | 16h |
| Domain Layer (27 entidades, 6 VOs, 9 specs, 8 domain events) | 24h |
| Application Layer (40+ use cases, validators, DTOs) | 48h |
| API Layer (17 controllers, middlewares, auth, swagger, rate limit) | 32h |
| Infra PostgreSQL (28 repos, 27 configs, 11 migrations) | 40h |
| Infra MongoDB (3 repos, class maps, migration runner) | 16h |
| Infra Async (Redis, S3, SMTP, Claude streaming, background jobs) | 16h |
| Testes (50 arquivos: unit + integração + arch + bench) | 32h |
| Observabilidade (OpenTelemetry + Serilog + health checks) | 8h |
| Documentação (README + ARCHITECTURE.md) | 8h |
| CI/CD (GitHub Actions) | 4h |
| **TOTAL ESTIMADO (trabalho realizado)** | **~244 horas** ≈ **6–7 semanas** de 1 dev sênior |

### Trabalho Pendente (Features Críticas para Go-Live)

| Feature | Horas Estimadas |
|---------|----------------|
| Webhooks outbound | 24h |
| Importação CSV/Excel | 20h |
| Exportação relatórios (Excel/PDF) | 16h |
| Billing / Integração de Pagamento | 40h |
| Limites de plano enforçados | 8h |
| Transferência entre lojas | 20h |
| Docker Compose completo | 4h |
| **TOTAL PENDENTE CRÍTICO** | **~132 horas** ≈ **3–4 semanas** de 1 dev sênior |

---

## 🎯 Qualidade de Código

### Pontos Fortes

| Aspecto | Nota | Evidência |
|---------|------|-----------|
| **Arquitetura** | 10/10 | Clean Arch estrita, testada por NetArchTest, zero violações |
| **Design de Domínio** | 9/10 | Value Objects com validação, métodos de domínio ricos, specs |
| **Segurança** | 9/10 | BCrypt, JWT, rate limit, lockout, audit log, RBAC |
| **Observabilidade** | 9/10 | OpenTelemetry + Serilog + correlation IDs + health checks |
| **Testabilidade** | 8/10 | 52,6% de cobertura relativa, TestContainers para integração |
| **Performance** | 8/10 | Redis, indexes, paginação, benchmarks no CI |
| **Documentação** | 8/10 | README extenso (1.061 linhas), Swagger bilíngue |
| **Completude** | 7/10 | Faltam webhooks, billing, importação em massa |

### Dívida Técnica Identificada

| Item | Severidade | Descrição |
|------|-----------|-----------|
| `EasyStock.Infra.Async` muito pequeno | Baixa | Só 6 arquivos/493 linhas — serviços podem estar misturados com Postgre |
| `AnuncioIa` em Infra.Postgre | Média | Lógica de Claude em Infra.Postgre em vez de Infra.Async |
| Limites de plano não enforçados | Alta | `Plano.LimiteProdutos` existe mas não é checado nos use cases |
| Docker Compose ausente | Alta | Sem docker-compose.yml, onboarding é manual |
| `RelatorioMensalJob` sem template HTML | Baixa | Job existe mas email pode sair sem formatação |
| `PerfilPermissao` não totalmente usada | Baixa | Entidade existe mas autorização usa enum `NivelAcesso` direto |

---

## 🗺️ Roadmap Sugerido

### Sprint 1 — Go-Live Ready (2 semanas)
- [ ] Docker Compose completo (PostgreSQL + MongoDB + Redis + App)
- [ ] Enforçar limites de plano nos use cases
- [ ] Importação básica de CSV (produtos + estoque)

### Sprint 2 — Monetização (2 semanas)
- [ ] Integração de pagamento (Stripe ou Asaas)
- [ ] Console de SuperAdmin
- [ ] Email de relatório mensal com template HTML

### Sprint 3 — Integrações (2 semanas)
- [ ] Webhooks outbound
- [ ] API Keys para máquina-máquina
- [ ] Exportação Excel/PDF

### Sprint 4 — Funcionalidades Avançadas (3 semanas)
- [ ] Transferência entre lojas
- [ ] Processo formal de devolução (RMA)
- [ ] 2FA (TOTP)
- [ ] Geração de código de barras (EAN/QR)

---

## 📋 Checklist de Prontidão para Produção

- [x] Autenticação e autorização
- [x] Isolamento multi-tenant
- [x] Logging e observabilidade
- [x] Health checks
- [x] Rate limiting
- [x] Validação de entrada (FluentValidation)
- [x] Tratamento global de exceções
- [x] Migrations de banco de dados
- [x] Testes automatizados no CI
- [ ] Docker Compose para deploy
- [ ] Billing e controle de assinatura ativas
- [ ] Limites de plano enforçados
- [ ] Backup de banco de dados configurado
- [ ] Alertas de monitoramento (Grafana/Prometheus)
- [ ] CDN para arquivos estáticos (S3 apenas armazena, não serve via CDN)
- [ ] Política de retenção de dados (LGPD)
- [ ] Termos de uso e privacidade
