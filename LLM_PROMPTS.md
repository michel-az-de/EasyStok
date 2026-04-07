# 🤖 EasyStok — Catálogo de Features e Prompts para LLMs

> Este documento lista todas as features do sistema, o que está implementado, o que está pendente,  
> e fornece **prompts prontos** para que LLMs (como Claude, GPT-4, Gemini) possam implementar  
> cada feature seguindo os padrões do projeto.

---

## 📌 Como Usar Este Documento

1. Copie o bloco `### Contexto do Projeto` abaixo e cole no início de qualquer prompt
2. Escolha a feature desejada na seção correspondente
3. Cole o prompt completo no seu LLM favorito
4. O código gerado seguirá os padrões já estabelecidos no projeto

---

## 🧱 Contexto do Projeto (Cole sempre no início dos prompts)

```
Você está trabalhando no projeto EasyStok — um sistema SaaS multi-tenant de gestão de estoque
construído em .NET 9 com Clean Architecture + DDD + CQRS.

ESTRUTURA DE PROJETOS:
- EasyStock.Domain: Entidades ricas, Value Objects, Specifications, Domain Events, Exceptions
  (ZERO dependências externas — apenas C# puro)
- EasyStock.Application: Use Cases (Commands/Queries), Validators (FluentValidation), DTOs,
  Ports/Interfaces (IRepository, IService), Domain Event Handlers
- EasyStock.Api: Controllers (ASP.NET Core), Middlewares, JWT Auth, Background Services, Swagger
- EasyStock.Infra.Postgre: Repositórios EF Core (PostgreSQL), DbContext, EntityTypeConfigurations,
  EF Migrations
- EasyStock.Infra.MongoDb: Repositórios MongoDB (alternativa ao Postgre)
- EasyStock.Infra.Async: Redis, S3, SMTP, Claude (Anthropic) streaming, Background Jobs

PADRÕES OBRIGATÓRIOS:
1. Domain Layer: NUNCA adicione dependências externas. Entidades com métodos de domínio ricos.
   Value Objects imutáveis com validação no construtor. Exceptions específicas por regra de negócio.
2. Use Cases: Classe com método ExecuteAsync(Command/Query), injeta repositórios e serviços via
   construtor. Lança exceções de domínio tipadas (nunca retorna null para erro).
3. Repositories: Interface em Application/Ports/Output/Persistence/, implementação em Infra.
   Métodos sempre aceitam empresaId para multi-tenant. Usa IUnitOfWork para commit.
4. Controllers: Injetam Use Cases. Retornam IActionResult/Results<T>.
   Usam [Authorize(Policy = "...")] para RBAC.
5. Multi-tenancy: TODA entidade tem EmpresaId. TODA query filtra por EmpresaId.
6. Testes: xUnit + NSubstitute (mocking) + FluentAssertions. TestContainers para integração.

TECNOLOGIAS:
- .NET 9, Entity Framework Core 9, Npgsql, MongoDB.Driver
- FluentValidation, BCrypt.Net-Next, JWT Bearer, Swashbuckle
- Serilog, OpenTelemetry, StackExchange.Redis, AWSSDK.S3
- xUnit, NSubstitute, FluentAssertions, NetArchTest, Testcontainers, BenchmarkDotNet

TARGET FRAMEWORK: net9.0
NULLABLE: enable
IMPLICIT USINGS: enable
LINGUAGEM: C#, código e comentários em inglês, mensagens de erro ao usuário em português BR
```

---

## 🔴 Features Críticas (Bloqueiam Go-Live)

---

### FEATURE 1 — Docker Compose Completo

**Status:** ❌ Não implementado  
**Esforço:** ~4 horas (dev sênior)

**O que precisa ser criado:**
- `docker-compose.yml` na raiz com todos os serviços
- `docker-compose.override.yml` para desenvolvimento local
- `Dockerfile` otimizado para a API
- `.dockerignore`

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Criar configuração completa de Docker para o EasyStok.

Crie os seguintes arquivos na raiz do projeto:

1. `Dockerfile` para EasyStock.Api:
   - Multi-stage build: sdk:9.0 para build, aspnet:9.0 para runtime
   - WORKDIR /app
   - Copiar .csproj de todos os projetos necessários e fazer dotnet restore
   - Copiar tudo e fazer dotnet publish EasyStock.Api/EasyStock.Api.csproj
   - Expose 8080
   - ENTRYPOINT ["dotnet", "EasyStock.Api.dll"]

2. `docker-compose.yml`:
   Services necessários:
   - `api`: build do Dockerfile, porta 8080:8080, depends_on: [postgres, redis, mongo]
   - `postgres`: postgres:16-alpine, porta 5432:5432, volume para persistência
   - `mongo`: mongo:7, porta 27017:27017, volume para persistência
   - `redis`: redis:7-alpine, porta 6379:6379
   - `jaeger`: jaegertracing/all-in-one, porta 16686:16686 (UI) e 4317:4317 (OTLP)
   
   Environment variables para a `api` service:
   - ConnectionStrings__DefaultConnection (postgres)
   - ConnectionStrings__MongoConnection (mongo)
   - Redis__ConnectionString
   - Jwt__SecretKey (usar placeholder)
   - OpenTelemetry__OtlpEndpoint (jaeger)
   - Anthropic__Enabled=false
   - FileStorage__Provider=Local
   
   Networks: easystock-net (bridge)
   Volumes: postgres-data, mongo-data

3. `docker-compose.override.yml`:
   - Overrides de desenvolvimento com variáveis de ambiente mais simples
   - Hot reload via volume mount do código

4. `.dockerignore`:
   - Excluir **/bin, **/obj, **/.git, **/Migrations/*.Designer.cs, **/node_modules

IMPORTANTE: A connection string do PostgreSQL deve usar o hostname do service docker (`postgres`),
não `localhost`. O mesmo para MongoDB (`mongo`) e Redis (`redis`).

Gere o conteúdo completo de cada arquivo.
```

---

### FEATURE 2 — Enforçar Limites de Plano

**Status:** ❌ Não implementado (entidade `Plano` existe com limites, mas não são checados)  
**Esforço:** ~8 horas (dev sênior)

**O que precisa ser criado:**
- `IPlanoLimiteService` em Application/Ports
- `PlanoLimiteService` implementação
- Verificações nos use cases de `CadastrarProduto`, `CadastrarUsuario`, `CriarLoja`

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar enforçamento dos limites de plano SaaS no EasyStok.

A entidade Plano existe em EasyStock.Domain/Entities/Plano.cs com:
- LimiteProdutos (int)
- LimiteUsuarios (int)  
- LimiteArmazenamento (int)
A entidade AssinaturaEmpresa liga Empresa ao Plano com status (Ativa, Suspensa, Cancelada, Expirada).

IMPLEMENTAR:

1. Em EasyStock.Application/Ports/Output/IPlanoLimiteService.cs:
   Interface com métodos:
   - Task<bool> PodeAdicionarProdutoAsync(Guid empresaId)
   - Task<bool> PodeAdicionarUsuarioAsync(Guid empresaId)
   - Task<bool> PodeAdicionarLojaAsync(Guid empresaId)
   - Task<PlanoLimiteInfo> ObterLimitesAtuaisAsync(Guid empresaId)
   
   PlanoLimiteInfo record:
   - PlanoNome, LimiteProdutos, LimiteUsuarios, ProdutosUsados, UsuariosUsados

2. Em EasyStock.Infra.Postgre/Services/PlanoLimiteService.cs:
   Implementação que:
   - Busca AssinaturaEmpresa ativa (Status == Ativa && DataFim > DateTime.UtcNow)
   - Busca Plano associado
   - Conta Produtos ativos da empresa (Status != Descontinuado)
   - Compara com LimiteProdutos
   - Se sem assinatura ativa: usar plano gratuito com LimiteProdutos = 50 (configurável)
   
3. Exception em EasyStock.Domain/Exceptions/LimitePlanoExcedidoException.cs:
   - Mensagem: "Limite do plano excedido. Atualize seu plano para continuar."
   - Inclui: TipoLimite (enum: Produto, Usuario, Loja, Armazenamento), LimiteMaximo, LimiteAtual

4. Modificar EasyStock.Application/UseCases/CadastrarProduto/CadastrarProdutoUseCase.cs:
   - Injetar IPlanoLimiteService
   - No início do ExecuteAsync: verificar PodeAdicionarProdutoAsync(empresaId)
   - Se false: throw LimitePlanoExcedidoException(...)

5. Modificar EasyStock.Application/UseCases/CriarUsuario/CriarUsuarioUseCase.cs:
   - Mesma lógica com PodeAdicionarUsuarioAsync

6. Registrar PlanoLimiteService no DI em EasyStock.Infra.Postgre/Extensions/ServiceCollectionExtensions.cs

7. Testes em EasyStock.Application.Tests/UseCases/PlanoLimiteTests.cs:
   - Teste: CadastrarProduto_QuandoLimiteExcedido_DeveLancarException
   - Teste: CadastrarProduto_QuandoAbaixoDoLimite_DevePermitir
   - Teste: CadastrarProduto_SemAssinaturaAtiva_DeveUsarPlanoPadrao

Siga os padrões do projeto: use NSubstitute para mocks, FluentAssertions para asserts.
```

---

### FEATURE 3 — Importação em Massa via CSV

**Status:** ❌ Não implementado  
**Esforço:** ~20 horas (dev sênior)

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar importação em massa de produtos e itens de estoque via CSV.

IMPLEMENTAR:

1. Command em EasyStock.Application/UseCases/ImportarProdutosCSV/:
   - ImportarProdutosCSVCommand: { EmpresaId, CsvContent (string), LojaId? }
   - ImportarProdutosCSVResult: { TotalProcessados, TotalSucesso, TotalErros, Erros[] }
   - Erro record: { Linha, Campo, Mensagem }

2. Formato do CSV esperado (cabeçalho obrigatório):
   Nome,Marca,Tipo,SKU,CodigoBarras,Categoria,CustoUnitario,PrecoVenda,
   Quantidade,QuantidadeMinima,FornecedorNome,Validade,CodigoLote,Observacoes

3. Use Case ImportarProdutosCSVUseCase:
   - Parse do CSV linha a linha
   - Para cada linha:
     a. Validar campos obrigatórios (Nome, Tipo, Quantidade, CustoUnitario)
     b. Buscar ou criar Categoria pelo nome
     c. Chamar CadastrarProdutoUseCase se produto não existe (por SKU ou Nome+Marca)
     d. Chamar RegistrarEntradaEstoqueUseCase para criar o item de estoque
     e. Capturar erros por linha sem abortar todo o import
   - Processar em lotes de 50 linhas
   - Retornar relatório de sucesso/erro

4. Controller em EasyStock.Api/Controllers/ImportsController.cs:
   - POST /api/imports/produtos/csv
     - Aceita multipart/form-data com arquivo .csv
     - Limite: 5MB, 10.000 linhas
     - Retorna ImportarProdutosCSVResult
   - POST /api/imports/estoque/csv
     - Similar mas para atualizar estoque de produtos existentes
   - GET /api/imports/template/produtos
     - Retorna CSV de exemplo para download
   - GET /api/imports/template/estoque
     - Retorna CSV de exemplo para estoque

5. Validações:
   - Arquivo deve ser .csv (verificar content-type E extensão)
   - Máximo 10.000 linhas por import
   - CustoUnitario e PrecoVenda: decimais positivos
   - Tipo: deve ser FISICO, ALIMENTO ou SERVICO
   - Validade: formato yyyy-MM-dd
   - Quantidade: inteiro positivo

6. Testes em EasyStock.Application.Tests/UseCases/ImportarProdutosCSVUseCaseTests.cs:
   - CSV válido com 3 produtos → deve criar 3 produtos + 3 itens estoque
   - CSV com linha inválida (campo faltando) → deve registrar erro e continuar
   - Produto já existente por SKU → deve apenas criar novo lote de estoque
   - CSV vazio → deve retornar erro de validação

NÃO use bibliotecas externas para CSV — implemente parser simples com string.Split e
tratamento de aspas duplas para campos com vírgulas.
```

---

### FEATURE 4 — Webhooks Outbound

**Status:** ❌ Não implementado  
**Esforço:** ~24 horas (dev sênior)

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar sistema de webhooks outbound para notificar sistemas externos
de eventos relevantes do EasyStok.

IMPLEMENTAR:

1. Entidade em EasyStock.Domain/Entities/WebhookSubscription.cs:
   - Id (Guid)
   - EmpresaId (Guid)
   - Url (string) [validar formato URL]
   - Eventos (string[]) [lista de eventos subscritos]
   - SecretKey (string) [para assinar payload com HMAC-SHA256]
   - Ativo (bool)
   - CriadoEm, AlteradoEm (DateTime)
   
   Eventos suportados (enum WebhookEvent):
   - estoque.entrada, estoque.saida, estoque.baixo, estoque.vencimento
   - produto.criado, produto.atualizado
   - venda.registrada, pedido.criado

2. Interface em EasyStock.Application/Ports/Output/IWebhookDispatcher.cs:
   - Task DispatchAsync(Guid empresaId, WebhookEvent evento, object payload)

3. Implementação em EasyStock.Infra.Async/Services/WebhookDispatcher.cs:
   - Buscar WebhookSubscription ativos para empresa com o evento
   - Para cada subscription:
     a. Serializar payload como JSON
     b. Calcular assinatura: HMAC-SHA256(secretKey, jsonPayload) → header X-EasyStok-Signature
     c. POST para subscription.Url com timeout de 10 segundos
     d. Retry: 3 tentativas com backoff exponencial (1s, 4s, 16s)
     e. Registrar resultado (sucesso/falha) em WebhookDeliveryLog
   - Executar chamadas em paralelo (Parallel.ForEachAsync)

4. Entidade WebhookDeliveryLog em Domain:
   - Id (Guid), SubscriptionId (Guid), Evento (string)
   - RequestPayload (string), ResponseStatus (int?)
   - Sucesso (bool), TentativasRealizadas (int)
   - CriadoEm (DateTime)

5. Repository interfaces em Application/Ports/Output/Persistence/:
   - IWebhookSubscriptionRepository (CRUD + GetByEmpresaAndEventoAsync)
   - IWebhookDeliveryLogRepository (Create + GetBySubscriptionAsync paginado)

6. Use Cases em Application/UseCases/Webhooks/:
   - CriarWebhookUseCase: validar URL, verificar limite (máx 10 por empresa)
   - AtualizarWebhookUseCase, DesativarWebhookUseCase, ListarWebhooksUseCase
   - TestWebhookUseCase: dispara evento de teste para validar endpoint

7. Controller em EasyStock.Api/Controllers/WebhooksController.cs:
   - POST /api/webhooks
   - GET /api/webhooks
   - PUT /api/webhooks/{id}
   - DELETE /api/webhooks/{id}
   - POST /api/webhooks/{id}/test
   - GET /api/webhooks/{id}/logs

8. Integrar IWebhookDispatcher nos use cases existentes:
   - RegistrarEntradaEstoqueUseCase: disparar estoque.entrada
   - RegistrarSaidaEstoqueUseCase: disparar estoque.saida
   - AlertasEstoqueJob: disparar estoque.baixo e estoque.vencimento
   - RegistrarVendaUseCase: disparar venda.registrada

9. Migration EF Core para WebhookSubscription e WebhookDeliveryLog

10. Testes unitários para WebhookDispatcher:
    - Mock IHttpClientFactory
    - Testar que assinatura HMAC está correta
    - Testar retry em caso de falha HTTP
```

---

### FEATURE 5 — Exportação de Relatórios (Excel/PDF)

**Status:** ❌ Não implementado  
**Esforço:** ~16 horas (dev sênior)

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar exportação de relatórios em Excel (XLSX) e PDF.

BIBLIOTECAS PERMITIDAS (adicionar aos csproj):
- ClosedXML (Excel): ClosedXML, versão 0.102.x
- Relatórios PDF: QuestPDF, versão 2024.x

IMPLEMENTAR:

1. Interface em EasyStock.Application/Ports/Output/IRelatorioExporter.cs:
   - Task<byte[]> ExportarEstoqueExcelAsync(Guid empresaId, FiltroEstoque filtro)
   - Task<byte[]> ExportarVendasExcelAsync(Guid empresaId, DateTime inicio, DateTime fim)
   - Task<byte[]> ExportarAnaliticoPDFAsync(Guid empresaId, DateTime inicio, DateTime fim)

2. Implementação em EasyStock.Infra.Async/Services/RelatorioExporter.cs:
   
   ExportarEstoqueExcel:
   - Planilha 1 "Estoque Atual": Id, Produto, Variação, SKU, Lote, Fornecedor,
     Quantidade, Mínimo, Status, Custo, Preço, Valor Total, Validade, Dias Sem Movimento
   - Planilha 2 "Alertas": apenas items Status != Ok
   - Formatação: cabeçalhos em negrito, linhas coloridas por status
     (vermelho=Critical/Vencido, amarelo=Warn, cinza=Slow)
   
   ExportarVendasExcel:
   - Planilha 1 "Vendas": Data, Canal, NF, Items, Total
   - Planilha 2 "Produtos Vendidos": Produto, Qtd, Receita, Margem%
   
   ExportarAnaliticoPDF:
   - Usar QuestPDF
   - Cabeçalho com logo (se existir), nome da empresa, período
   - Seções: Receita Total, Top 10 Produtos, Margem por Categoria,
     Items em Alerta, Sugestões de Reposição
   - Gráfico de barras simples (ASCII ou imagem gerada)

3. Controller em EasyStock.Api/Controllers/ExportController.cs:
   - GET /api/export/estoque/excel → retorna application/vnd.openxmlformats-officedocument...
   - GET /api/export/vendas/excel?inicio=&fim= → XLSX
   - GET /api/export/analitico/pdf?inicio=&fim= → application/pdf
   
   Headers de response:
   - Content-Disposition: attachment; filename="estoque_2026-04.xlsx"
   - Cache-Control: no-cache

4. Rate limiting específico para exports:
   - Máximo 5 exports por hora por empresa (evitar geração excessiva)

5. Testes unitários básicos:
   - Verificar que ExportarEstoqueExcel retorna bytes não-vazios
   - Verificar Content-Disposition header
```

---

## 🟡 Features de Média Prioridade

---

### FEATURE 6 — Transferência de Estoque Entre Lojas

**Status:** ❌ Não implementado  
**Esforço:** ~20 horas (dev sênior)

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar transferência de itens de estoque entre lojas da mesma empresa.

IMPLEMENTAR:

1. Entidade em EasyStock.Domain/Entities/TransferenciaEstoque.cs:
   - Id (Guid)
   - EmpresaId (Guid)
   - LojaOrigemId (Guid)
   - LojaDestinoId (Guid)
   - ItemEstoqueOrigemId (Guid)
   - ItemEstoqueDestinoId? (Guid) [criado na transferência se não existir]
   - Quantidade (Quantidade) [Value Object]
   - Status (enum StatusTransferencia): Solicitada, EmTransito, Concluida, Cancelada
   - Observacoes (string?)
   - SolicitadaEm, ConcluidaEm? (DateTime)
   - CriadoPorUsuarioId (Guid)
   
   Domain Methods:
   - Concluir(): Status = Concluida, ConcluidaEm = now
   - Cancelar(motivo): Status = Cancelada
   - EstaEmAndamento(): Status == Solicitada || Status == EmTransito

2. Domain Event:
   - TransferenciaEstoqueConcluida: { TransferenciaId, LojaOrigemId, LojaDestinoId, Quantidade }

3. Use Case SolicitarTransferenciaEstoque:
   - Validar: ambas as lojas pertencem à empresa
   - Validar: item origem tem quantidade suficiente (>= Quantidade da transferência)
   - Validar: item origem não está bloqueado ou vencido
   - Criar TransferenciaEstoque com Status = Solicitada
   - NÃO debitar estoque ainda (só ao concluir)

4. Use Case ConcluirTransferenciaEstoque:
   - Buscar Transferencia (deve ser Solicitada ou EmTransito)
   - Debitar do ItemEstoque origem via RegistrarSaida(natureza: Transferencia)
   - Creditar no ItemEstoque destino:
     a. Se existe ItemEstoque para produto na loja destino: RegistrarReposicao
     b. Se não existe: criar novo ItemEstoque na loja destino com mesmos dados de custo/preço
   - Criar MovimentacaoEstoque em ambos os itens
   - Publicar TransferenciaEstoqueConcluida
   - Atualizar status para Concluida

5. Use Case CancelarTransferenciaEstoque:
   - Só pode cancelar se Status == Solicitada
   - Alterar status para Cancelada com motivo

6. Repository interface e implementação EF Core:
   - ITransferenciaEstoqueRepository
   - GetByEmpresaAsync, GetByLojaAsync, GetByStatusAsync

7. Controller em EasyStock.Api/Controllers/TransferenciaController.cs:
   - POST /api/transferencias → Solicitar
   - PUT /api/transferencias/{id}/concluir → Concluir
   - PUT /api/transferencias/{id}/cancelar → Cancelar
   - GET /api/transferencias → Listar por empresa
   - GET /api/transferencias/{id} → Detalhe

8. Migration EF Core para a nova tabela TransferenciasEstoque

9. Testes:
   - SolicitarTransferencia_SemEstoqueSuficiente_DeveLancarException
   - ConcluirTransferencia_DeveDebitarOrigem_ECreditarDestino
   - CancelarTransferencia_QuandoJaConcluida_DeveLancarException
```

---

### FEATURE 7 — Two-Factor Authentication (2FA/TOTP)

**Status:** ❌ Não implementado  
**Esforço:** ~16 horas (dev sênior)

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar Two-Factor Authentication via TOTP (Google Authenticator compatível).

BIBLIOTECA: Adicionar Otp.NET (nuget: Otp.NET, versão 1.4.x) ao EasyStock.Infra.Async.csproj

IMPLEMENTAR:

1. Campos na entidade Usuario (EasyStock.Domain/Entities/Usuario.cs):
   - TwoFactorEnabled (bool) = false por padrão
   - TwoFactorSecretKey (string?) = chave TOTP em Base32
   
   Domain Methods:
   - HabilitarDoisFatores(secretKey): TwoFactorEnabled = true
   - DesabilitarDoisFatores(): TwoFactorEnabled = false, SecretKey = null
   - GerarChaveSecretaTotp(): retorna string Base32 aleatória de 20 bytes

2. Use Case HabilitarDoisFatoresUseCase:
   Input: { UsuarioId }
   Output: { QrCodeUri (string), SecretKey (string) }
   Lógica:
   - Gerar secretKey com OtpNet
   - Gerar QR Code URI no formato: otpauth://totp/EasyStok:{email}?secret={key}&issuer=EasyStok
   - Salvar secretKey no usuário (sem habilitar ainda — requer confirmação)
   - Retornar URI para o frontend gerar QR Code via biblioteca JS

3. Use Case ConfirmarDoisFatoresUseCase:
   Input: { UsuarioId, Codigo (6 dígitos) }
   Output: { Sucesso, CodigosRecuperacao[] }
   Lógica:
   - Verificar TOTP com OtpNet (tolerância de 1 período = 30s antes/depois)
   - Se válido: chamar usuario.HabilitarDoisFatores(secretKey)
   - Gerar 8 códigos de recuperação únicos (UUID truncado) e armazenar hasheados

4. Modificar AutenticarUsuarioUseCase:
   - Se usuario.TwoFactorEnabled: não retornar token JWT ainda
   - Em vez disso: retornar { RequiresTwoFactor: true, TwoFactorSessionToken: tempToken }
   - TempToken: JWT de 5 minutos com claim "2fa_pending: true" e userId

5. Use Case ValidarDoisFatoresUseCase:
   Input: { TwoFactorSessionToken, Codigo }
   Output: { Token (JWT completo), RefreshToken }
   Lógica:
   - Validar TwoFactorSessionToken (must have claim "2fa_pending")
   - Validar TOTP code com OtpNet OU verificar código de recuperação
   - Se válido: gerar JWT completo + refresh token (igual ao fluxo normal de login)
   - Invalidar TwoFactorSessionToken

6. Use Case DesabilitarDoisFatoresUseCase:
   Input: { UsuarioId, SenhaAtual }
   Output: { Sucesso }
   - Verificar senha atual com BCrypt
   - Chamar usuario.DesabilitarDoisFatores()

7. Endpoints em AuthController:
   - POST /api/auth/2fa/habilitar → retorna QR Code URI
   - POST /api/auth/2fa/confirmar → confirma e ativa 2FA
   - POST /api/auth/2fa/validar → segunda etapa do login
   - DELETE /api/auth/2fa → desabilitar 2FA

8. Migration EF Core para os novos campos em Usuarios

9. Testes:
   - Login_Com2FAHabilitado_DeveRetornarRequiresTwoFactor
   - ValidarCodigo_CodigoCorreto_DeveRetornarJWT
   - ValidarCodigo_CodigoErrado_DeveLancarException
   - ValidarCodigo_CodigoDeRecuperacao_DevePermitirAcesso
```

---

### FEATURE 8 — API Keys para Integrações Máquina-a-Máquina

**Status:** ❌ Não implementado  
**Esforço:** ~16 horas (dev sênior)

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar sistema de API Keys como alternativa ao JWT para integrações M2M.

IMPLEMENTAR:

1. Entidade EasyStock.Domain/Entities/ApiKey.cs:
   - Id (Guid)
   - EmpresaId (Guid)
   - Nome (string) [descrição da integração: "ERP SAP", "Shopify Plugin"]
   - KeyHash (string) [SHA-256 da key, nunca armazenar em texto plano]
   - KeyPrefix (string) [primeiros 8 chars para identificação: "esk_live_abc12345..."]
   - Permissoes (string[]) [lista de endpoints/operações permitidas]
   - UltimoUsoEm (DateTime?)
   - ExpiracaoEm (DateTime?) [null = sem expiração]
   - Ativa (bool)
   - CriadaEm, RevogarEm? (DateTime)
   - CriadaPorUsuarioId (Guid)

2. Use Case CriarApiKeyUseCase:
   - Gerar key no formato: esk_live_{Guid.NewGuid():N} (32 chars aleatórios)
   - Salvar hash SHA-256 no banco
   - Retornar key completa UMA ÚNICA VEZ (não é recuperável depois)
   - Máximo 10 API keys ativas por empresa

3. Middleware ApiKeyAuthenticationMiddleware:
   - Ler header: X-API-Key: esk_live_...
   - Extrair prefix (primeiros 8 chars após esk_live_)
   - Buscar ApiKey pelo prefix no banco
   - Verificar hash SHA-256 da key fornecida bate com KeyHash
   - Verificar: Ativa, não expirada
   - Se válida: criar ClaimsPrincipal com EmpresaId e permissões da key
   - Atualizar UltimoUsoEm assincronamente (fire-and-forget)

4. Suporte dual no AuthController (JWT OU API Key):
   - Endpoints que já aceitam JWT devem também aceitar API Key via middleware
   - Adicionar esquema de autenticação: builder.Services.AddAuthentication()
     .AddJwtBearer("Bearer", ...) 
     .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>("ApiKey", ...)

5. Use Cases: CriarApiKey, RevogarApiKey, ListarApiKeys, RotacionarApiKey

6. Controller em EasyStock.Api/Controllers/ApiKeysController.cs:
   - POST /api/api-keys → Criar (retorna key completa ÚNICA vez)
   - GET /api/api-keys → Listar (apenas prefix + metadata, nunca a key)
   - DELETE /api/api-keys/{id} → Revogar

7. Migration EF Core para tabela ApiKeys

8. Swagger: Adicionar suporte para X-API-Key além de Bearer token

9. Testes:
   - Request_ComApiKeyValida_DeveAutorizar
   - Request_ComApiKeyRevogada_DeveRetornar401
   - Request_ComApiKeyExpirada_DeveRetornar401
   - CriarApiKey_AcimaDe10_DeveLancarException
```

---

### FEATURE 9 — Processo de Devolução (RMA)

**Status:** ❌ Não implementado  
**Esforço:** ~24 horas (dev sênior)

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar processo formal de devolução/retorno de mercadorias (RMA).

IMPLEMENTAR:

1. Entidade EasyStock.Domain/Entities/Devolucao.cs:
   - Id (Guid)
   - EmpresaId, LojaId? (Guid)
   - VendaOrigemId? (Guid) [venda original, se houver]
   - TipoDevolucao (enum): ClienteParaLoja, LojaParaFornecedor
   - Motivo (enum): DefeitoFabrica, DanificadoTransporte, PedidoErrado, 
                    Vencido, NaoConformidade, OutroMotivo
   - MotivoDescricao (string?) [detalhes livres]
   - Status (enum): Solicitada, RecebidoFisicamente, Inspecionado, 
                    Reintegrado, Descartado, RemetidoFornecedor, Concluida
   - ItensDevolvidos (List<ItemDevolucao>) [navigation property]
   - FornecedorId? (Guid) [para devolução a fornecedor]
   - NumeroAutorizacao (string?) [RA/RMA code]
   - CriadaEm, ConcluidaEm? (DateTime)
   
   Domain methods:
   - Receber(): Status = RecebidoFisicamente
   - Inspecionar(resultado): Status = Inspecionado
   - Reintegrar(): Status = Reintegrado [volta ao estoque disponível]
   - Descartar(): Status = Descartado
   - RemeterFornecedor(): Status = RemetidoFornecedor

2. Entidade ItemDevolucao:
   - Id (Guid), DevolucaoId (Guid)
   - ItemEstoqueId (Guid)
   - Quantidade (Quantidade)
   - ResultadoInspecao (enum)?: Reintegrar, Descartar, AguardandoFornecedor

3. Use Cases:
   a. SolicitarDevolucaoUseCase: criar devolução com status Solicitada
   b. ReceberDevolucaoFisicaUseCase: marcar como recebida fisicamente
   c. InspecionarDevolucaoUseCase: para cada item: definir ResultadoInspecao
   d. ProcessarResultadoInspecaoUseCase:
      - Para itens Reintegrar: RegistrarEntradaEstoque (natureza: Devolucao)
      - Para itens Descartar: RegistrarSaida (natureza: Descarte)
      - Atualizar status da devolução

4. Controller EasyStock.Api/Controllers/DevolucaoController.cs com CRUD completo

5. Migration EF Core para as novas tabelas

6. Domain Events: DevolucaoRecebida, ItemReintegradoAoEstoque, ItemDescartadoApósDevolucao

7. Notificações automáticas via GeradorNotificacoesAutomaticas:
   - Quando devolução chega: notificar Gerente

8. Testes cobrindo o fluxo completo:
   Devolucao criada → recebida → inspecionada → reintegrada ao estoque
```

---

## 🟢 Features de Baixa Prioridade (Nice to Have)

---

### FEATURE 10 — Geração de Código de Barras

**Status:** ❌ Não implementado  
**Esforço:** ~8 horas

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar geração de códigos de barras (EAN-13) e QR Code para produtos.

BIBLIOTECAS: 
- Adicionar ZXing.Net.Bindings.Core (versão 0.16.x) ao EasyStock.Infra.Async.csproj
  (mantém compatibilidade cross-platform)

IMPLEMENTAR:

1. Interface em EasyStock.Application/Ports/Output/ICodigoBarrasGenerator.cs:
   - Task<byte[]> GerarEAN13Async(string codigo, CodigoBarrasFormat formato)
   - Task<byte[]> GerarQrCodeAsync(string conteudo, int tamanho = 200)
   - string GerarCodigoEAN13Aleatorio(Guid empresaId) [gera código com dígito verificador]
   
   CodigoBarrasFormat enum: PNG, SVG, PDF417

2. Implementação em EasyStock.Infra.Async/Services/CodigoBarrasGenerator.cs:
   - Usar ZXing.Net para gerar barcode em PNG
   - Para EAN-13: validar 12 dígitos de entrada, calcular dígito verificador
   - Retornar bytes da imagem PNG

3. Use Case GerarCodigoBarrasProdutoUseCase:
   - Se produto já tem CodigoBarras: usar o existente
   - Se não tem: gerar EAN-13 único (prefixo da empresa + sequencial)
   - Salvar CodigoBarras no produto
   - Retornar imagem PNG

4. Endpoints em ProdutoController:
   - GET /api/produtos/{id}/barcode?formato=png|svg → retorna imagem
   - POST /api/produtos/{id}/barcode/gerar → gera e salva código
   - GET /api/produtos/{id}/qrcode → QR Code com URL do produto

5. Testes: verificar que PNG retornado é não-vazio e tem content-type correto
```

---

### FEATURE 11 — Billing / Integração de Pagamento

**Status:** ❌ Não implementado  
**Esforço:** ~40 horas

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar billing com integração ao Stripe para cobrar assinaturas SaaS.

BIBLIOTECA: Stripe.net (nuget: Stripe.net, versão 46.x) em EasyStock.Infra.Async.csproj

IMPLEMENTAR:

1. Campos adicionais em AssinaturaEmpresa:
   - StripeSubscriptionId (string?)
   - StripeCustomerId (string?)
   - ProximaCobrancaEm (DateTime?)
   - UltimoFalhaPagamentoEm (DateTime?)

2. Interface IStripeService em Application/Ports/Output/:
   - Task<string> CriarClienteAsync(Empresa empresa)
   - Task<StripeCheckoutSession> CriarCheckoutSessionAsync(Guid empresaId, Guid planoId)
   - Task ProcessarWebhookAsync(string payload, string signature)

3. Use Cases em Application/UseCases/Billing/:
   - IniciarAssinaturaUseCase: redirecionar para Stripe Checkout
   - CancelarAssinaturaUseCase: cancelar no Stripe + atualizar status
   - ProcessarWebhookStripeUseCase:
     - checkout.session.completed → ativar assinatura
     - invoice.payment_failed → suspender após 3 falhas
     - customer.subscription.deleted → cancelar

4. Controller EasyStock.Api/Controllers/BillingController.cs:
   - POST /api/billing/checkout?planoId= → Criar sessão Stripe
   - DELETE /api/billing/assinatura → Cancelar
   - POST /api/billing/webhook → Receber eventos Stripe (sem auth JWT)
   - GET /api/billing/portal → Portal de gerenciamento Stripe

5. Verificar assinatura ativa em middleware ou filter:
   - Se AssinaturaEmpresa.Status != Ativa: retornar 402 Payment Required
   - Exceções: rotas de billing, auth, planos

6. Testes com Stripe mock (StripeClient mockado via IStripeClient interface)
```

---

## 🔧 Features de Melhoria de Qualidade

---

### FEATURE 12 — Enforçar Multi-tenancy via Middleware Global

**Status:** ⚠️ Parcialmente implementado (cada repositório filtra manualmente)  
**Esforço:** ~8 horas

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar enforçamento automático de multi-tenancy via EF Core Global Query Filters,
eliminando a necessidade de cada repositório filtrar manualmente por EmpresaId.

IMPLEMENTAR:

1. Interface IEntidadeMultiTenant em EasyStock.Domain/:
   - Guid EmpresaId { get; }
   Aplicar em todas as entidades que têm EmpresaId

2. ICurrentEmpresaAccessor em EasyStock.Application/Ports/:
   - Guid? ObterEmpresaAtual()
   
3. Modificar EasyStockDbContext para aplicar Global Query Filters automaticamente:
   ```csharp
   protected override void OnModelCreating(ModelBuilder builder)
   {
       // Aplicar automaticamente para todas as entidades que implementam IEntidadeMultiTenant
       foreach (var entityType in builder.Model.GetEntityTypes())
       {
           if (typeof(IEntidadeMultiTenant).IsAssignableFrom(entityType.ClrType))
           {
               var method = typeof(EasyStockDbContext)
                   .GetMethod(nameof(SetGlobalQueryFilter), 
                              BindingFlags.NonPublic | BindingFlags.Instance)!
                   .MakeGenericMethod(entityType.ClrType);
               method.Invoke(this, [builder]);
           }
       }
   }
   
   private void SetGlobalQueryFilter<T>(ModelBuilder builder) where T : class, IEntidadeMultiTenant
   {
       var empresaId = _currentEmpresaAccessor.ObterEmpresaAtual();
       if (empresaId.HasValue)
           builder.Entity<T>().HasQueryFilter(e => e.EmpresaId == empresaId.Value);
   }
   ```

4. Implementar CurrentEmpresaAccessor usando IHttpContextAccessor:
   - Extrair EmpresaId do JWT claim "empresaId"
   - Retornar null para SuperAdmin (acesso cross-tenant)

5. Manter compatibilidade nos repositórios existentes:
   - Repositórios podem continuar recebendo empresaId como parâmetro
   - O filter do EF Core adiciona uma camada extra de segurança (defense in depth)

6. Testes de arquitetura: verificar que nenhuma query retorna dados cross-tenant
```

---

### FEATURE 13 — Console de SuperAdmin

**Status:** ❌ Não implementado  
**Esforço:** ~24 horas

---

#### 🤖 Prompt para LLM

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Implementar área administrativa para SuperAdmin gerenciar todas as empresas/planos.

IMPLEMENTAR:

1. Endpoints protegidos por Policy "SuperAdmin" (NivelAcesso.SuperAdmin):

2. Use Cases em Application/UseCases/Admin/:
   a. ListarTodasEmpresasUseCase: paginado, com stats (usuários, produtos, assinatura)
   b. SuspenderEmpresaUseCase: mudar status da assinatura para Suspensa
   c. ReativarEmpresaUseCase: reativar assinatura
   d. ImpersonarEmpresaUseCase: gerar JWT válido para a empresa (auditar ação!)
   e. ListarMetricasGlobaisUseCase: total empresas, MRR, churn rate, etc.
   f. CriarPlanoUseCase: criar novos planos de assinatura
   g. AtualizarPlanoUseCase: atualizar preços e limites

3. Controller AdminController:
   - GET /api/admin/empresas → Listar todas
   - GET /api/admin/empresas/{id} → Detalhes com assinatura e stats
   - POST /api/admin/empresas/{id}/suspender
   - POST /api/admin/empresas/{id}/reativar
   - POST /api/admin/empresas/{id}/impersonar → Retorna JWT temporário (1h)
   - GET /api/admin/metricas → MRR, total users, churn
   - CRUD /api/admin/planos

4. Auditoria obrigatória: TODA ação de Admin deve criar AuditLog com usuário superadmin

5. Rate limiting mais restrito para área admin: 30 req/min

6. Endpoint de health report:
   - GET /api/admin/health/report → Status de todas as dependências por empresa
```

---

## 📊 Features Já Implementadas (Para Referência)

Estas features **já existem** no projeto. Use como referência de padrão ao implementar novas.

### Referência: Implementação de Use Case Completa

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

ESTUDO DE CASO: Analise como RegistrarEntradaEstoqueUseCase está implementado em
EasyStock.Application/UseCases/RegistrarEntradaEstoque/ e use como modelo para
qualquer novo use case que precise:
- Validar entidade pai (produto, empresa)
- Criar ou atualizar entidade filha (item de estoque)
- Criar audit trail (movimentação)
- Publicar domain event
- Chamar serviço externo opcional (IA)
- Usar IUnitOfWork para commit transacional
```

### Referência: Implementação de Repositório com EF Core

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

ESTUDO DE CASO: Analise ItemEstoqueRepository em
EasyStock.Infra.Postgre/Repositories/ItemEstoqueRepository.cs
como modelo para implementar repositórios que precisam:
- Filtros complexos com múltiplos parâmetros
- Queries com múltiplos Include() para eager loading
- Paginação (Skip/Take)
- Ordenação dinâmica
- Queries de agregação (Sum, Average, Count)
- Índices para performance (reference: migration AdicionarIndexes)
```

### Referência: Domain Entity com Métodos Ricos

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

ESTUDO DE CASO: Analise EasyStock.Domain/Entities/ItemEstoque.cs
como modelo para criar entidades de domínio que:
- Têm construtor privado + factory method estático (CriarParaEntrada)
- Encapsulam state changes (RegistrarSaida, RegistrarReposicao)
- Lançam domain exceptions tipadas (EstoqueInsuficienteException)
- Calculam métricas derivadas (RecalcularIndicadores)
- Constroem campos desnormalizados para performance (MontarChavePesquisa)
```

---

## 🛠️ Prompts de Correção de Bugs / Melhorias

### Mover Claude Streaming para Infra.Async

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

PROBLEMA: A implementação GeradorDescricaoAnuncioClaudeStreaming está em
EasyStock.Infra.Postgre (infraestrutura de banco de dados), mas deveria estar em
EasyStock.Infra.Async (serviços externos e async).

TAREFA: Mover a classe GeradorDescricaoAnuncioClaudeStreaming e sua interface
IGeradorDescricaoAnuncio de EasyStock.Infra.Postgre para EasyStock.Infra.Async.

Passos:
1. Mover IGeradorDescricaoAnuncio de Application/Ports para manter em Application
   (já que é uma abstração que o Application usa)
2. Mover implementação Claude para EasyStock.Infra.Async/Services/
3. Mover GeradorDescricaoAnuncioStub para EasyStock.Infra.Async/Services/
4. Atualizar referências de projeto nos .csproj afetados
5. Atualizar registros DI em ServiceCollectionExtensions de cada projeto
6. Verificar que EasyStock.Infra.Postgre não referencia mais EasyStock.Infra.Async
   (preservar sentido de dependência correto)
```

### Adicionar Cobertura de Testes para Controllers Faltantes

```
[CONTEXTO DO PROJETO - cole aqui o contexto acima]

TAREFA: Adicionar testes unitários para os controllers que ainda não têm cobertura.

Controllers sem testes identificados:
- AuthController (8 endpoints)
- VendaController (4 endpoints)
- FornecedorController (10 endpoints)
- NotificacaoController (5 endpoints)

Para cada controller, criar arquivo de teste em EasyStock.Api.UnitTests/Controllers/:
- Padrão: xUnit + NSubstitute + FluentAssertions
- Mockar todos os Use Cases injetados
- Um teste por endpoint (happy path)
- Um teste por endpoint de erro (ex: ID não encontrado → 404)
- Referência: ver AnalyticsControllerTests.cs como modelo (452 linhas)
```

---

## 📚 Apêndice: Padrões do Projeto

### Nomenclatura

| Tipo | Padrão | Exemplo |
|------|--------|---------|
| Use Case | `[Verbo][Substantivo]UseCase` | `CadastrarProdutoUseCase` |
| Command | `[Verbo][Substantivo]Command` | `CadastrarProdutoCommand` |
| Command Result | `[Verbo][Substantivo]Result` | `CadastrarProdutoResult` |
| Repository Interface | `I[Entidade]Repository` | `IProdutoRepository` |
| Domain Exception | `[Regra]Exception` | `EstoqueInsuficienteException` |
| Domain Event | `[Entidade][Fato]` | `EstoqueBaixoIdentificado` |
| Value Object | Substantivo simples | `Dinheiro`, `Quantidade`, `Validade` |
| Specification | `[Condição]Specification` | `ProdutoAtivoSpecification` |

### Estrutura de Pasta para Nova Feature

```
EasyStock.Application/UseCases/[NomeDaFeature]/
  ├── [NomeDaFeature]UseCase.cs
  ├── [NomeDaFeature]Command.cs       (ou Query.cs)
  ├── [NomeDaFeature]Result.cs
  └── [NomeDaFeature]CommandValidator.cs

EasyStock.Application/Ports/Output/Persistence/
  └── I[Entidade]Repository.cs        (se nova entidade)

EasyStock.Domain/Entities/
  └── [Entidade].cs                   (se nova entidade)

EasyStock.Infra.Postgre/Repositories/
  └── [Entidade]Repository.cs

EasyStock.Infra.Postgre/Configurations/
  └── [Entidade]Configuration.cs

EasyStock.Api/Controllers/
  └── [Feature]Controller.cs

EasyStock.Application.Tests/UseCases/
  └── [NomeDaFeature]UseCaseTests.cs
```
