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

## 13. Não documentar parâmetros posicionais de record com `///` antes do parâmetro
- **O que aconteceu:** F10/F13 usaram `public sealed record Foo(/// <summary>...</summary> int Bar)`. C#/Roslyn silently ignora — nenhum doc é gerado para o parâmetro. Compilador não emite warning quando `GenerateDocumentationFile` está off (Application layer não gera).
- **Como evitar:** documentar com `<param name="Bar">desc</param>` no doc-comment do tipo:
  ```csharp
  /// <summary>...</summary>
  /// <param name="Bar">desc</param>
  public sealed record Foo(int Bar);
  ```
  Funciona pra positional records em C# 11+ e propaga pra IntelliSense + XML doc.

## 14. Não passar `currentUser.UsuarioId` direto em fluxo invocado de webhook/sistema
- **O que aconteceu:** F14 (commit e29cc61) faz webhook Pix anônimo disparar `HelpdeskTicketService.AbrirAsync` quando há N falhas. `currentUser.UsuarioId` em contexto sem JWT retorna `Guid.Empty` (vide `CurrentUserAccessor.GetGuidClaimOrDefault`). `AdminTicket.CriadoPorId` e `TicketHistorico.AutorId` têm FK em `usuarios.id` com `OnDelete(SetNull)` mas só aceitam `NULL` ou GUID válido — `Guid.Empty` viola FK no insert. O `try/catch` envolvente do `AutoTicketFalhaPagamento` engolia a `DbUpdateException` silenciosamente: o `FaturaEvento` ficava órfão e o ticket nunca era criado. F14 quebrado em prod sem barulho.
- **Como evitar:** qualquer serviço chamado de webhook/job/sistema deve coagir `currentUser.UsuarioId` para `Guid?` antes de gravar em FK nullable: `Guid? autor = currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId`. Padrão aplicado em `HelpdeskTicketService.AbrirAsync`. Auditoria mental: "este service é chamável fora de um controller autenticado? entao FK de usuário nunca pode receber Guid.Empty direto".
