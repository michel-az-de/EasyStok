# Onda 7 — Poda (P01–P06)

Objetivo: tirar do repositório o que é SaaS de mercado, fiscal, MAUI e alvos de deploy paralelos.
Nada aqui bloqueia as ondas 1 a 6; P05 depende de S18 e S19. Números medidos em 22/09/2026 (doc 04).

Regra geral: **primeiro código, depois tabelas.** Cada P remove controllers, use cases, jobs, páginas e
projetos; as tabelas ficam até uma migration única de limpeza (`P06`), para que qualquer rollback seja
`git revert` sem `Down` de dados. Antes do primeiro corte: `git tag v-pre-poda` no `master` e push da tag.

Aceite comum a todos os P: `gate.ps1` verde; `dotnet test EasyStok.CI.slnf` verde; contagem de controllers
(`ls EasyStock.Api/Controllers | wc -l`) e de endpoints (`git grep -c "\[Http" EasyStock.Api/Controllers`)
menor que antes, registrada no PR; smoke manual: `GET /api/storefront/{slug}/menu` responde 200 e o
webhook da Huggy (S03) responde ao handshake. Tier: `chore` = baixo, exceto quando remove migration ou
toca `Program.cs` (alto).

---

### P01 · Remover `EasyStock.Admin` e os controllers `Admin*`

**Medido.** Admin: 145 arquivos, ~30 k linhas; API: 25 controllers `Admin*`, 146 endpoints.
**Pré-condição.** As quatro funções que o Admin ainda entrega precisam de endpoint tenant (policy `Admin`, não `SuperAdmin`) para o console novo: templates e rotinas de notificação (`AdminNotificacoesController`, 20 endpoints → mover os de template/rotina/canal para `NotificacoesController`), configuração de storefront e janelas/frete (`AdminStorefrontController` → `TenantVitrineCardapioController` já cobre cardápio; criar `StorefrontConfiguracaoController` com `GET|PUT` de storefront, janelas, bloqueios, zonas), diagnóstico de WhatsApp (cai: a Huggy tem o dela), feature flags (`FeatureFlagsController` já é tenant).
**Escopo.** Remover projeto `EasyStock.Admin`, `EasyStock.Admin.UnitTests`, `fly.admin.toml`, `Dockerfile.cloudrun.admin`, serviço `admin` em `render.yaml`/`docker-compose.azure.yml`, os 25 controllers `Admin*`, `App/UseCases/Admin/**` (1.549 linhas) exceto o que os endpoints tenant novos reusam, entidades `AdminAuditLog`, `AdminImpersonationLog`, `AdminAcessoPiiLog`, `AdminNotaTenant` (código; tabelas em P06), `SubscriptionGateMiddleware`? Não: é P02. Atualizar `EasyStok.sln` e `EasyStok.CI.slnf`.
**Aceite.** Além do comum: os endpoints tenant novos têm teste de autorização (empresa A não altera storefront de B).
**Rollback.** `git revert` do PR.
**Leitura mínima.** `Api/Controllers/AdminNotificacoesController.cs`, `AdminStorefrontController.cs` (só rotas e use cases chamados); `EasyStok.CI.slnf`.
**Tamanho.** G. **Tier.** alto (auth, Program.cs).

---

### P02 · Remover billing SaaS e a gate de assinatura

**Medido.** 1.155 linhas de use cases; entidades `Plano`, `AssinaturaEmpresa`, `CobrancaAssinatura`, `Fatura`, `FaturaItem`, `FaturaEvento`, `FaturaPagamento`, `FaturaContador`, `Cupom`; jobs `CobrancaAssinaturaJob`, `FaturaReconciliacaoJob`, `FaturaBackfillJob`; `FaturaPdfRenderer`; `SubscriptionGateMiddleware`; controllers `AssinaturaClienteController`, `PlanoController`, `FaturasController`, `FaturasPdfController`, `OnboardingController`; Web: `SiteController` (landing, `/precos`), `OnboardingController`, `AssinaturaController`; MRR (ADR-0037, portas `IRevenueMetricsQueries`, `IMetricasFinanceirasQueries` na parte de MRR).
**Cuidado.** `WebhookPixController.ProcessarPagamentoAsync` roteia txid sem prefixo para cobrança de assinatura: após P02, txid desconhecido deve responder 200 com `WebhookRecebido.Sucesso=false, Erro="txid_desconhecido"`. `EfiPixService` e `EfiPixWebhookProcessor` **ficam** (S11 e contas a receber usam). `RegistrarEmpresa`/`CompletarOnboarding`: manter só um seed/endpoint interno de criar empresa (a FMA e a Casa da Baba já existem).
**Escopo.** Remover o listado; `AssinaturaExpirando`/`AssinaturaExpirada`/`FaturaCriada`/`FaturaVencendo`/`FaturaPaga`/`FaturaVencida` saem de `TipoEventoNotificacao` junto com templates e coletor `ColetorAssinaturasExpirando`; `AssinaturaCacheInvalidationInterceptor` sai; `#628` e `#700` fecham como "removido".
**Aceite.** Além do comum: login e dashboard funcionam sem assinatura; webhook Pix com txid de assinatura antigo responde 200 sem exceção.
**Rollback.** `git revert`.
**Leitura mínima.** `Api/Middleware/SubscriptionGateMiddleware.cs` (nome via `git grep -l SubscriptionGate`); `Api/Controllers/WebhookPixController.cs`; `Api/BackgroundServices/BackgroundJobServiceCollectionExtensions.cs`.
**Tamanho.** G. **Tier.** alto.

---

### P03 · Remover helpdesk, FAQ, banners, leads públicos e anúncios com IA

**Medido.** ~3 k linhas de use cases (`TicketSuporte`, `Faq`, `Banners`, `AnuncioIa`, `GerarSugestaoDescricaoAnuncio`); entidades `AdminTicket`, `AdminTicketMensagem`, `AdminTicketTecnicoMeta`, `TicketAnexo`, `TicketHistorico`, `SlaConfiguracao`, `FaqItem`, `FaqCategoria`, `FaqFeedback`, `FaqVisualizacao`, `Banner`, `BannerConteudo`, `BannerConfirmacao`, `LeadPublico`, `AnuncioIa`, `UsoIa`? **Não**: `UsoIa` fica (S06 registra o agente); controllers `HelpdeskController`, `TicketsController`, `FaqController`, `FaqAdminController`, `BannersController`, `IaAnuncioController`, `Controllers/Public/LeadsPublicosController.cs`, `Controllers/Ci/*` (tickets de CI); Worker: `SlaMonitorService`, `BannerNotificacaoService`; eventos de notificação `Ticket*`, `Sla*`, `BugFixCriado`, `BroadcastSuperAdmin`, `ConviteCsat`; Web: `/faq`, `/anuncios`, `Views/Shared/_WhatsAppFloat.cshtml`.
**Cuidado.** `Infra.Postgre/Services/GeradorDescricaoAnuncio*.cs` saem; `GeradorAutoPreenchimento*` (usado por entrada de estoque) fica. Rate limit policies `tickets-post`, `ai` (se só anúncio usava) saem.
**Aceite.** Comum; `#547`, `#842`, `#843`, `#839`, `#845`, `#840` fecham como "removido".
**Rollback.** `git revert`.
**Leitura mínima.** `Worker/Program.cs`; `Domain/Enums/Notifications/TipoEventoNotificacao.cs`.
**Tamanho.** G. **Tier.** baixo (chore) salvo se tocar `Program.cs`.

---

### P04 · Remover fiscal (NF-e/NFC-e)

**Medido.** 2.845 linhas (Domain/Fiscal 683, `App/UseCases/Fiscal`, `App/Services/Fiscal`, `Integrations/Fiscal/FocusNFe` e `Mock`); controllers `NotasFiscaisController`, `ConfiguracaoFiscalController`, `WebhookFocusNFeController`; Worker `RenovacaoCertificadoA1BackgroundService`, `ReprocessarContingenciaBackgroundService`; `Storefront.NfeAutomaticaHabilitada`/`ModeloFiscal`; `EmpresaConfiguracaoFiscal`; Web aba Fiscal e `/notas-fiscais`; PWA `caixa/danfe.css`; rate limit `nfe-emitir`; issues #1037, #584, #558, #559, #770.
**Decisão registrada.** ADR-0048: nenhuma das empresas fatura pelo sistema. RN-28: canhoto, não cupom. Tag `v-pre-poda` preserva o código para a hipótese "contadora pede".
**Escopo.** Remover o listado; `CredencialIntegracao` com `provider_key="sefaz"` deixa de ser lida (tabela fica).
**Aceite.** Comum; issues fiscais fechadas como "fora de escopo (ADR-0048)".
**Rollback.** `git revert` ou checkout da tag.
**Leitura mínima.** `Integrations/Fiscal/GatewayFiscalFactory.cs`; `Worker/Program.cs`.
**Tamanho.** G. **Tier.** alto (migration em P06; aqui só código).

---

### P05 · Remover MAUI e o espólio mobile (após S18 e S19)

**Medido.** `EasyStok.Mobile` 134 arquivos / 6 k linhas (aponta para `easystok.azurewebsites.net`, fora do `CI.slnf`, último commit 20/07); `Domain/Entities/Mobile/*` (9 entidades espelho: `Batch`, `CashEntry`, `Client`, `Order`, `Product`, `MobileDevice`, `DeviceCommand`, `DeviceBackup`, `ApkRelease`), `MobileProcessedMutation`; `Api/Mobile/**` (22 controllers, 66 endpoints) **exceto** o que S18/S19 já moveram (broker, SSE, KDS); `App/Operacao/**` (sync); `casa-da-baba-mobile/`; `api/` (Azure Function de OTA); workflows `mobile-e2e.yml` e de APK; `Mobile__*` em configs; `X-Mobile-Api-Key`; Web `MobileDevicesController`, `/dispositivos`, `OperacaoMobileController`, `*MobileController`.
**Cuidado.** A PWA em `Api/wwwroot/pwa/` é a superfície diária atual da Casa da Baba (caixa). **Não remover** até o console novo ter caixa e produção em paridade; abrir issue própria para a PWA com esse critério. Bugs #950, #954, #934 (consolidação pedido→Venda do mobile) fecham como "removido" quando o sync sair.
**Aceite.** Comum; `git grep -l "Entities.Mobile"` vazio fora de migrations; SSE e KDS novos continuam com testes verdes.
**Rollback.** `git revert`.
**Leitura mínima.** `Api/Mobile/` (listagem de arquivos); `EasyStok.sln`.
**Tamanho.** G. **Tier.** alto.

---

### P06 · Limpeza final: projetos, providers, deploy, docs e tabelas

**Escopo.**
- Projetos: `EasyStock.Contracts` (4 arquivos, só `Infra.Integrations` referencia), `Infra.MongoDb.IntegrationTests` vazio (#780).
- Providers: `StripeGatewayAdapter` (stub declarado), `ZenviaSmsProvider`, `TwilioWhatsAppProvider`, `MetaCloudWhatsAppProvider`, `StubWhatsAppOtpSender`? (OTP do site continua: manter), `MercadoPagoGatewayAdapter`/`PagamentoGatewayRouter`/`GatewayRoutingRule`/`GatewayHealthSnapshot`/`PaymentAttempt*` — **decisão do Felipe** (D2 do doc 04): se o site migrar para Pix (S11 no checkout), o Mercado Pago sai inteiro; senão fica só `MercadoPagoClient` + webhook, sem roteador. Fecha #783 como "Huggy".
- Compras: `Fornecedor`, `PedidoFornecedor`, `DocumentoEntrada`, `SugestaoCompra` (1.410 linhas, 16 endpoints) saem; `ListasCompras` e reposição (ADR-0039) ficam como "lista do dia de produção".
- Rotulagem P-02: `ProdutoFichaTecnica` nutricional, `EtiquetaTemplateSistema`, editor de etiqueta e ADR-0021 ficam **fora** do roadmap; etiqueta simples de lote (produto, validade, instrução) permanece. Marcar ADR-0021 como Superseded por ADR-0049.
- Deploy: manter **um** alvo (medir qual serve produção: `docker-compose.azure.yml` + Caddy na VM é o único que serve `casadababa.com`); remover `fly.toml`, `fly.web.toml`, `render.yaml`, `k8s/`, `azure-pipelines.yml`, `docker-compose.azure.images.yml`/`.noedge.yml` se não usados, workflows `deploy-render.yml`, `preview-render.yml`; `README.md` (Azure App Service, .NET 9) e `CLAUDE.md` (.NET 8, Fly) corrigidos para a realidade.
- Web: páginas e controllers das áreas removidas (`Site`, `Onboarding`, `Assinatura`, `Faq`, `Anuncios`, `Fornecedores`, `Notas fiscais`, `Dispositivos`), `MenuDefinition.cs` sem os itens.
- Migration única `RemoverTabelasSaasFiscalMobile` derrubando as tabelas de P01 a P05 (lista explícita no PR, `Down` recria vazias), **só após** uma release em produção sem o código.
- Issues: fechar #831, #832 (Hiram), #1013 fica (FMA continua), #585 fecha por S13, #886 por S22, #772 confirmado compatível, #704 fica.
**Aceite.** Comum; `dotnet build` sem warnings novos; `ProgramSize_Api` (ratchet) menor; um único caminho de deploy documentado no README com o comando real.
**Rollback.** `git revert`; a migration de tabelas só entra depois do resto estar estável.
**Leitura mínima.** `render.yaml`, `fly.toml`, `docker-compose.azure.yml`, `Caddyfile` (só serviços); `README.md`; `CLAUDE.md` (bloco OVERRIDE).
**Tamanho.** G. **Tier.** alto.
