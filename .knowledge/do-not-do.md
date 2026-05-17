# Não-Faça (mistakes já cometidos)

> Lições caras. Não repita.

## 1. Não estimar custo de cloud sem auditar TUDO da subscription
- **O que aconteceu:** estimei ~$50/mês em Azure → real foi **$474 em 30 dias** (App Service Plan P1v3 + PG Flexible + StandardV2 storage rodando 24/7). Subscription foi desabilitada por spending limit.
- **Por que falhei:** olhei só 1 recurso, não somei plan tier × always-on × storage redundancy.
- **Como evitar:** rodar `az consumption usage list` ou equivalente ANTES de prometer número. Sempre apresentar faixa (mín–máx) e listar premissas.

## 2. Não recomendar `[AllowAnonymous]` em endpoints destrutivos
- **O que aconteceu:** liberei `/diagnostico` inteiro pra debugging e expus `ProxyLimparLogs`, `ProxyEsvaziarLixeira`, `ProxyDeleteContainer`, etc. Qualquer um com a URL apagava dados.
- **Como evitar:** `[AllowAnonymous]` só em GET informativo. Mutações sempre autenticadas, mesmo em diag/health.

## 3. Não criar dependência nova sem checar se o pacote já existe na camada
- **O que aconteceu:** usei `IConfiguration` em `EasyStock.Application` sem `Microsoft.Extensions.Configuration` referenciado → compile error.
- **Como evitar:** `dotnet list package` antes de `using` novo. Application layer é POCO — config vem por `IOptions<T>`.

## 4. Não usar `Math.Ceiling` em quantidade fracionária para descontar estoque
- **O que aconteceu:** pedido 1.2kg virava débito de 2 unidades → saldo negativo silencioso, sem alerta.
- **Como evitar:** usar valor exato (decimal). Se precisar arredondar pra estoque inteiro, é `Math.Round(MidpointRounding.AwayFromZero)` + log warn.

## 5. Não fazer migration EF que duplique tabelas criadas em SQL raw
- **O que aconteceu:** `AddAdminModule` duplicou tabelas que o seed mobile já criava → erro `relation already exists` em deploy.
- **Como evitar:** uma única source of truth pra schema. Se SQL raw existe, registrar como migration "fake" (`__EFMigrationsHistory` insert manual) ou converter pra migration EF de fato.

## 6. Não dizer "já está protegido" sem reler o arquivo
- **O que aconteceu:** afirmei que `DiagnosticoController` estava protegido; estava com 21 endpoints `[AllowAnonymous]`.
- **Como evitar:** quando usuário perguntar "tá seguro?", reler o controller inteiro com `Read`, não confiar em memória.

## 7. Não fazer `GetByIdAsync` quando precisa de filhos
- **O que aconteceu:** `pedido.Itens` vinha vazio porque o repo não fazia `Include` → desconto de estoque era no-op silencioso. Testes mockados passavam.
- **Como evitar:** método com nome explícito (`GetByIdWithDetailsAsync`) quando carrega aggregate completo. Teste de integração com DB real, não só unit com mock.

## 8. Não usar `FOR UPDATE` sem transação explícita
- **O que aconteceu:** lock liberado no fim do statement, não até `SaveChanges`. Race condition em concorrência.
- **Como evitar:** preferir `await unitOfWork.ExecuteInTransactionAsync(async ct => { ... })` — usa `IExecutionStrategy` (compativel com Npgsql retry). `BeginTransactionAsync()` direto perde a retentativa em falha transitoria, ficando para legados.

## 9. Não deixar trabalho pela metade
- **Padrão registrado:** Felipe se irrita com "fiz parte do que pediu". Se pedir 3 features, entregar 3 ou avisar antes que vai entregar 1.

## 11. Não commitar valor real em `appsettings.*.json`
- **O que aconteceu:** `appsettings.Development.json` ficou ~28 dias com PG Azure (host/user/senha), JWT secret literal e Mobile ApiKey literal. Repo é público no GitHub. Senha PG já foi descontinuada com Azure; secrets restantes rotacionados na auditoria 2026-05-06.
- **Como evitar:**
  - `appsettings.*.json` versionado contém **apenas placeholders** (`${VAR}`) ou strings vazias. Valor real vai em env var (Render) ou `dotnet user-secrets` (dev local).
  - Workflow `.github/workflows/secret-scan.yml` roda gitleaks em todo PR/push e falha o build se encontrar token vazado conhecido.
  - `Program.cs` tem fail-fast pra `JWT_SECRET_KEY`, `Mobile:ApiKey` e connection strings com placeholder não substituído ou valor literal vazado conhecido.

## 10. Não floreio em resposta
- Português BR direto, sem "Claro!", sem "Vou agora...", sem travessões enfeitando, sem vírgula sobrando.
- Resposta = ação + resultado.

## 12. Não criar agregação cross-tenant sem parâmetro `Guid? empresaId`
- **O que aconteceu:** dashboard F10 (commit 74a2b08) introduziu `SomarPrecoMensalAtivasAsync()` e `ContarPorStatusAsync()` (assinaturas) sem aceitar `empresaId`. `IgnoreQueryFilters()` somado à ausência de `.Where(EmpresaId == ...)` retornava agregados GLOBAIS. Operacional admin com `VisualizarFaturas` filtrando dashboard por sua empresa via MRR/ARR/contagens de assinaturas vazavam dados de TODOS os tenants. F13 (cache) propagou o bug porque a chave de cache incluía empresaId mas o valor cacheado era global. Fixado em 191f685.
- **Como evitar:** todo método de agregação que use `IgnoreQueryFilters()` em entidade tenant-aware DEVE aceitar `Guid? empresaId = null` e aplicar `.Where(x => x.EmpresaId == empresaId.Value)` quando `empresaId.HasValue && != Guid.Empty`. Auditoria mental antes de commit: "este metodo tem `IgnoreQueryFilters()`? entao precisa de empresaId opcional". Cache key contendo empresaId NÃO substitui o filtro — só evita colisão entre buckets já errados.

## 14. Não criar webhook signature validator sem paridade com os existentes
- **O que aconteceu:** F12 (commit e5a4180) criou `MercadoPagoSignatureValidator` paralelo ao Stripe. Stripe tinha `±5min` de janela de replay; MP parseava `ts` mas só usava no corpo do HMAC, sem comparar com tempo atual. Webhook MP capturado podia ser replayado indefinidamente. Fix em auditoria 2026-05-07.
- **Como evitar:** ao criar novo validator (Stripe/MP/Pix/qualquer), abrir os existentes lado-a-lado e conferir paridade: replay window, `CryptographicOperations.FixedTimeEquals`, allow-unsigned guard, secret obrigatorio quando nao ha allow-unsigned, headers obrigatorios. **Quebra de paridade entre validators = vulnerabilidade silenciosa.** Bonus: criar testes unitarios espelhando os do gateway anterior (sem header, hmac correto/incorreto, ts atual/fora-da-janela, allow-unsigned). MP usa `ts` em **milissegundos** (Stripe usa segundos) — cada gateway tem seu format, conferir doc antes.

## 13. Não documentar parâmetros posicionais de record com `///` antes do parâmetro
- **O que aconteceu:** F10/F13 usaram `public sealed record Foo(/// <summary>...</summary> int Bar)`. C#/Roslyn silently ignora — nenhum doc é gerado para o parâmetro. Compilador não emite warning quando `GenerateDocumentationFile` está off (Application layer não gera).
- **Como evitar:** documentar com `<param name="Bar">desc</param>` no doc-comment do tipo:
  ```csharp
  /// <summary>...</summary>
  /// <param name="Bar">desc</param>
  public sealed record Foo(int Bar);
  ```
  Funciona pra positional records em C# 11+ e propaga pra IntelliSense + XML doc.

## 14. Não confiar em `MemberRenamer = _ => ""` como bloqueio em Scriban; `Template.RenderAsync` ignora `CancellationToken`-via-parametro
- **O que aconteceu:** `ScribanSandbox` usava `MemberRenamer = _ => string.Empty` esperando bloquear acesso a `obj.GetType().Assembly...`. `MemberRenamer` é só pra **renomear** membros (snake_case ↔ CamelCase) — não filtra acesso. O bloqueio real aconteceu por acidente (membro renomeado pra "" virou inacessível por nome) e quebra com qualquer template que use propriedade .NET legitima. Ao mesmo tempo o `ScribanRenderer` aplicava `cts.CancelAfter(500ms)` esperando que `template.RenderAsync(context)` cancelasse — o método **não recebe CT como parâmetro**; o timeout era fake e loop infinito travava o worker até GC.
- **Como evitar:**
  - Bloqueio de membros perigosos: usar `TemplateContext.MemberFilter` (delegate `MemberInfo -> bool`). Falsa = bloqueia. Lista bloquear: `Type`, `Assembly`, `MemberInfo` e derivados, `Delegate`, `IServiceProvider`.
  - Cancelamento de render: setar `context.CancellationToken = ct` e ler `ScriptAbortException` no catch. `RenderAsync(TemplateContext)` honra esse CT (testado: dispara `ScriptAbortException` em `<input>(linha,col) : error : The operation was cancelled`).
  - Limites adicionais que custam pouco e poupam DoS: `LoopLimit` (já tinha 500), `RecursiveLimit`, `ObjectRecursionLimit`, `LimitToString`, `RegexTimeOut` (defesa ReDoS pra `regex.match`).
  - `include`/`import` já são bloqueados por `TemplateLoader = null` (default). Não precisa remover dos builtins.
  - Auto-escape HTML não existe nativo: aplicar `WebUtility.HtmlEncode` em strings antes de injetar quando canal renderiza HTML (Email, InApp). Numeros/booleanos passam direto.
  - Cache de `Template` parseado por hash do source (singleton renderer + ConcurrentDictionary com cap). Reparse a cada render é O(template-size) caro.

## 15. Não passar `currentUser.UsuarioId` direto pra FK quando o caller pode ser anônimo
- **O que aconteceu:** F14 (commit e29cc61) introduziu `AutoTicketFalhaPagamento` que abre ticket admin via `HelpdeskTicketService.AbrirAsync` em resposta a webhook Pix. Webhook é anônimo — `ICurrentUserAccessor.UsuarioId` retorna `Guid.Empty` (não `null`, porque o tipo é `Guid` não-nullable). Esse `Guid.Empty` ia direto pra `AdminTicket.CriadoPorId` (FK p/ `Usuarios` com `OnDelete: SetNull`) e `TicketHistorico.AutorId` (idem). Postgres rejeita o INSERT com FK violation, falhando o fluxo todo de auto-ticket. Bug latente — não disparava nos testes unitários (sem DB) nem no caminho admin (autenticado). Detectado em auditoria pós-merge e corrigido em [HelpdeskTicketService.cs](EasyStock.Api/Services/Helpdesk/HelpdeskTicketService.cs) normalizando `Guid.Empty -> null` antes de passar pra `criadoPorId/autorId`.
- **Como evitar:** sempre que um service injetar `ICurrentUserAccessor` E for invocado em código de fundo (webhook, job, integration event handler), normalizar `currentUser.UsuarioId == Guid.Empty ? (Guid?)null : currentUser.UsuarioId` antes de gravar em FK. Padrão recomendado: criar variável `autorId` no início do método e usar ela em todas as escritas (ticket criador, historico autor, mensagem autor, evento UsuarioId etc). Cobertura: tests unitários do service não pegam — só integration tests com Postgres real. Adicionar smoke E2E quando o caminho for crítico.
