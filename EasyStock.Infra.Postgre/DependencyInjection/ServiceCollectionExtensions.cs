using EasyStock.Application.Events.Storefront.Handlers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Infra.Postgre.Configuration;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Events;
using EasyStock.Infra.Postgre.Hosting;
using EasyStock.Infra.Postgre.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddEasyStockPostgreInfrastructure(
            this IServiceCollection services,
            string connectionString,
            IConfiguration configuration)
        {
            services.Configure<CacheOptions>(configuration.GetSection("Cache"));

            // Garante limites de pool sãos quando a connection string vem sem
            // controle (ex: Cloud SQL f1-micro aceita ~25 conexões totais; com
            // múltiplas réplicas, pool default 100 estoura imediatamente).
            connectionString = EnsurePoolLimits(connectionString, configuration);

            services.AddSingleton<AuditTimestampsInterceptor>();
            services.AddSingleton<SetTenantOnConnectionInterceptor>();
            services.AddScoped<EntityChangeInterceptor>();
            // BUG-009 (#517): invalida o cache de saldo (produto-detalhe) em QUALQUER
            // mutacao de ItemEstoque via SaveChanges. Chokepoint — pega os 8 mutadores
            // de saldo e futuros, sem cada call site lembrar de invalidar.
            services.AddSingleton<IProdutoCacheInvalidator, ProdutoCacheInvalidator>();
            services.AddSingleton<EstoqueSaldoCacheInvalidationInterceptor>();
            services.AddDbContext<EasyStockDbContext>((sp, options) =>
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("EasyStock.Infra.Postgre");
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    npgsql.CommandTimeout(30);
                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                })
                .AddInterceptors(
                    sp.GetRequiredService<AuditTimestampsInterceptor>(),
                    sp.GetRequiredService<SetTenantOnConnectionInterceptor>(),
                    sp.GetRequiredService<EntityChangeInterceptor>(),
                    // BUG-009 (#517): por ULTIMO — captura o estado final da entidade.
                    sp.GetRequiredService<EstoqueSaldoCacheInvalidationInterceptor>()));

            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EasyStockDbContext>());
            services.AddScoped<ICategoriaRepository, CategoriaRepository>();
            services.AddScoped<IEmpresaRepository, EmpresaRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.ITenantContextAccessor, EasyStock.Infra.Postgre.TenantContextAccessor>();
            services.AddScoped<IProdutoRepository, ProdutoRepository>();
            services.AddScoped<IProdutoVariacaoRepository, ProdutoVariacaoRepository>();
            services.AddScoped<IProdutoCaracteristicaRepository, ProdutoCaracteristicaRepository>();
            services.AddScoped<IProdutoEmbalagemRepository, ProdutoEmbalagemRepository>();
            services.AddScoped<IItemEstoqueRepository, ItemEstoqueRepository>();
            services.AddScoped<IContagemRepository, ContagemRepository>();
            services.AddScoped<IItemVendaRepository, ItemVendaRepository>();
            services.AddScoped<IMovimentacaoEstoqueRepository, MovimentacaoEstoqueRepository>();
            services.AddScoped<IVendaRepository, VendaRepository>();
            services.AddScoped<INotificacaoRepository, NotificacaoRepository>();
            services.AddScoped<ILojaRepository, LojaRepository>();
            services.AddScoped<IConfiguracaoLojaRepository, ConfiguracaoLojaRepository>();
            services.AddScoped<IConfiguracaoAtendimentoRepository, EasyStock.Infra.Postgre.Repositories.Atendimento.ConfiguracaoAtendimentoRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.IConsentimentoContatoRepository, EasyStock.Infra.Postgre.Repositories.Atendimento.ConsentimentoContatoRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.IMensagemProgramadaRepository, EasyStock.Infra.Postgre.Repositories.Atendimento.MensagemProgramadaRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Campanhas.IInteresseItemRepository, EasyStock.Infra.Postgre.Repositories.Campanhas.InteresseItemRepository>(); // S31
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.ILembreteRepository, EasyStock.Infra.Postgre.Repositories.Atendimento.LembreteRepository>();
            // S44: entregadores, viagens e chamado.
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.IEntregadorRepository, EasyStock.Infra.Postgre.Repositories.Atendimento.EntregadorRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.IViagemRepository, EasyStock.Infra.Postgre.Repositories.Atendimento.ViagemRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.IChamadoEntregadorRepository, EasyStock.Infra.Postgre.Repositories.Atendimento.ChamadoEntregadorRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.IEntregasPorBairroQuery, EasyStock.Infra.Postgre.Repositories.Atendimento.EntregasPorBairroQuery>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.ICandidatosLembreteQuery, EasyStock.Infra.Postgre.Repositories.Atendimento.CandidatosLembreteQuery>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IExpedienteLojaRepository, EasyStock.Infra.Postgre.Repositories.Storefront.ExpedienteLojaRepository>();
            services.AddScoped<IPreferenciaMenuRepository, PreferenciaMenuRepository>();
            services.AddScoped<IFornecedorRepository, FornecedorRepository>();
            services.AddScoped<IClienteRepository, ClienteRepository>();
            services.AddScoped<IClienteCrmRepository, ClienteCrmRepository>(); // S24
            services.AddScoped<IHistoricoPedidosClienteQueries, EasyStock.Infra.Postgre.Queries.HistoricoPedidosClienteQueries>(); // S25
            services.AddScoped<IDomicilioQueries, EasyStock.Infra.Postgre.Queries.DomicilioQueries>(); // S25
            services.AddScoped<IPedidoRepository, PedidoRepository>();
            services.AddScoped<ICaixaRepository, CaixaRepository>();
            services.AddScoped<ILoteRepository, LoteRepository>();
            services.AddScoped<IEtiquetaTemplateRepository, EtiquetaTemplateRepository>();
            services.AddScoped<IListaComprasRepository, ListaComprasRepository>();
            services.AddScoped<IPedidoFornecedorRepository, PedidoFornecedorRepository>();
            services.AddScoped<IPedidoFornecedorItemRepository, PedidoFornecedorItemRepository>();
            services.AddScoped<IUsuarioRepository, UsuarioRepository>();
            services.AddScoped<IPerfilRepository, PerfilRepository>();
            services.AddScoped<IPlanoRepository, PlanoRepository>();
            services.AddScoped<IAssinaturaEmpresaRepository, AssinaturaEmpresaRepository>();
            services.AddScoped<IUsuarioEmpresaRepository, UsuarioEmpresaRepository>();
            services.AddScoped<IUsuarioPerfilRepository, UsuarioPerfilRepository>();
            services.AddScoped<IAuditLogRepository, AuditLogRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IResetTokenRepository, ResetTokenRepository>();
            services.AddScoped<IEmailConfirmationTokenRepository, EmailConfirmationTokenRepository>();
            services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
            services.AddScoped<IUsoIaRepository, UsoIaRepository>();
            services.AddScoped<IProdutoAlteracaoRepository, ProdutoAlteracaoRepository>();
            services.AddScoped<IProdutoComposicaoRepository, ProdutoComposicaoRepository>();
            services.AddScoped<IProdutoComposicaoAlteracaoRepository, ProdutoComposicaoAlteracaoRepository>();
            services.AddScoped<IMovimentacaoEstoqueAlteracaoRepository, MovimentacaoEstoqueAlteracaoRepository>();
            services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
            services.AddScoped<IFaturaRepository, FaturaRepository>();
            services.AddScoped<IFaturaNumeradorService, FaturaNumeradorService>();
            services.AddScoped<ILancamentoRepository, LancamentoRepository>();
            services.AddScoped<IWebhookRecebidoRepository, WebhookRecebidoRepository>();

            // Onda P0 Payment Orchestration
            services.AddScoped<EasyStock.Application.Ports.Output.Pagamentos.IPaymentAttemptRepository,
                Repositories.Pagamentos.PaymentAttemptRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Pagamentos.IGatewayRoutingRuleRepository,
                Repositories.Pagamentos.GatewayRoutingRuleRepository>();
            // S11: cobrança do pedido
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Pagamentos.ICobrancaPedidoRepository,
                Repositories.Pagamentos.CobrancaPedidoRepository>();
            // S20: fila de impressão do canhoto
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Operacao.IImpressaoPendenteRepository,
                Repositories.Operacao.ImpressaoPendenteRepository>();

            // Modulo Contas a Pagar / Contas a Receber (CAP/CAR)
            services.AddScoped<IContaPagarRepository, ContaPagarRepository>();
            services.AddScoped<IContaReceberRepository, ContaReceberRepository>();
            services.AddScoped<ICategoriaFinanceiraRepository, CategoriaFinanceiraRepository>();
            services.AddScoped<ICentroCustoRepository, CentroCustoRepository>();
            services.AddScoped<IFluxoCaixaQueries, FluxoCaixaQueries>();
            services.AddScoped<IKdsPedidoQueries, KdsPedidoQueries>(); // S19: KDS do console
            services.AddScoped<IPrazoPreparoPedidoQueries, PrazoPreparoPedidoQueries>(); // S21: início previsto

            services.AddScoped<IAdminTenantsQueries, AdminTenantsQueries>();
            services.AddScoped<ITenantFeatureFlagRepository, TenantFeatureFlagRepository>();
            services.AddScoped<IEntityAuditQueries, EntityAuditQueries>();
            services.AddScoped<IPublicadorEventos, PublicadorEventosEmMemoria>();

            // Modulo Integration (F3) — credenciais cifradas por tenant + resolver AES-256-GCM
            services.AddScoped<ICredencialIntegracaoRepository, CredencialIntegracaoRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Integration.Crypto.IIntegrationCredentialResolver,
                Integration.IntegrationCredentialResolver>();

            // Modulo Integration (F4) — outbox transacional de eventos externos
            services.AddScoped<IOutboxEventoIntegracaoRepository, OutboxEventoIntegracaoRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Integration.IPublicadorEventoIntegracao,
                Integration.PublicadorEventoIntegracao>();
            services.AddScoped<EasyStock.Application.Ports.Output.Integration.IIntegrationEventDispatcher,
                Integration.IntegrationEventDispatcher>();

            // Handlers de integracao (keyed pelo TipoEvento). pedido.mudou_status (Onda 4 /
            // issue 866): handler de observabilidade — evita que o evento fique em retry por
            // "sem handler"; a Onda 5 (#867) pluga Hiram/marketplace ao lado deste.
            services.AddKeyedScoped<EasyStock.Application.Ports.Output.Integration.IIntegrationEventHandler,
                EasyStock.Application.Events.Pedidos.Handlers.PedidoMudouStatusLogHandler>("pedido.mudou_status");
            // estoque.desacerto (S17): observabilidade até S18/S22 plugarem consumidores reais.
            services.AddKeyedScoped<EasyStock.Application.Ports.Output.Integration.IIntegrationEventHandler,
                EasyStock.Application.Events.Estoque.Handlers.EstoqueDesacertadoLogHandler>(
                EasyStock.Application.Events.Estoque.EstoqueDesacertadoEvent.TipoEventoOutbox);
            // S11: pedido.pago (S13, S18 e S20 entram ao lado deste).
            services.AddKeyedScoped<EasyStock.Application.Ports.Output.Integration.IIntegrationEventHandler,
                EasyStock.Application.Events.Pedidos.Handlers.PedidoPagoLogHandler>(
                EasyStock.Application.Events.Pedidos.PedidoPagoEvent.TipoEvento);
            // S13: avisos de status ao cliente pelo WhatsApp (outbox de notificações).
            services.AddKeyedScoped<EasyStock.Application.Ports.Output.Integration.IIntegrationEventHandler,
                EasyStock.Application.Events.Pedidos.Handlers.NotificarClienteStatusPedidoHandler>("pedido.mudou_status");
            services.AddKeyedScoped<EasyStock.Application.Ports.Output.Integration.IIntegrationEventHandler,
                EasyStock.Application.Events.Pedidos.Handlers.NotificarClientePedidoPagoHandler>(
                EasyStock.Application.Events.Pedidos.PedidoPagoEvent.TipoEvento);

            services.AddScoped<EasyStock.Application.Ports.Output.Security.IRowLevelSecurityBypass,
                Security.RowLevelSecurityBypass>();

            // Storefront — 12 repos para entities novas (ADR-0011, 0012, 0014, 0006)
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IStorefrontRepository,
                Repositories.Storefront.StorefrontRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.ICardapioItemRepository,
                Repositories.Storefront.CardapioItemRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IFreteZonaRepository,
                Repositories.Storefront.FreteZonaRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IJanelaEntregaRepository,
                Repositories.Storefront.JanelaEntregaRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IBloqueioEntregaRepository,
                Repositories.Storefront.BloqueioEntregaRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IVagaOcupadaRepository,
                Repositories.Storefront.VagaOcupadaRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IClienteOtpRepository,
                Repositories.Storefront.ClienteOtpRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IClienteSessionRepository,
                Repositories.Storefront.ClienteSessionRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IWebhookProcessadoRepository,
                Repositories.Storefront.WebhookProcessadoRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.ICheckoutIdempotencyRepository,
                Repositories.Storefront.CheckoutIdempotencyRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IPedidoAvaliacaoRepository,
                Repositories.Storefront.PedidoAvaliacaoRepository>();
            // Atendimento (S04, ADR-0050)
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.IConversaRepository,
                Repositories.Atendimento.ConversaRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.IAtendenteRepository,
                Repositories.Atendimento.AtendenteRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.ISessaoChatSiteRepository,
                Repositories.Atendimento.SessaoChatSiteRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Atendimento.ILinkCardapioConversaRepository,
                Repositories.Atendimento.LinkCardapioConversaRepository>();
            // #1102: número da Meta pelo qual a resposta sai = o da empresa do tenant corrente.
            services.AddScoped<EasyStock.Application.Ports.Output.Atendimento.IRemetenteWhatsApp,
                Services.Atendimento.RemetenteWhatsAppDoTenant>();
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IStorefrontFaleConoscoRepository,
                Repositories.Storefront.StorefrontFaleConoscoRepository>();
            // Storefront — autenticação OTP cliente (AUTH-002)
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IClienteStorefrontRepository,
                Repositories.Storefront.ClienteStorefrontRepository>();
            // Storefront — pedidos (CHECKOUT-001 + APROVAR-001 + PEDIDOS-001)
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IPedidoStorefrontRepository,
                Repositories.Storefront.PedidoStorefrontRepository>();

            // Notification repositories (Templates, Rotinas, Outbox, Consentimentos, etc.)
            services.AddEasyStockNotificationsRepositories();

            // AI: OpenAI tem prioridade; Anthropic como fallback; stub se nenhum habilitado
            var openAiEnabled = configuration.GetValue<bool>("OpenAI:Enabled");
            var anthropicEnabled = configuration.GetValue<bool>("Anthropic:Enabled");

            if (openAiEnabled)
            {
                services.AddHttpClient("OpenAI");
                services.AddScoped<IGeradorAutoPreenchimento, GeradorAutoPreenchimentoOpenAI>();
            }
            else if (anthropicEnabled)
            {
                services.AddHttpClient("Anthropic");
                services.AddScoped<IGeradorAutoPreenchimento, GeradorAutoPreenchimentoClaude>();
            }
            else
            {
                services.AddScoped<IGeradorAutoPreenchimento, GeradorAutoPreenchimentoStub>();
            }

            // F10-B: Retention service — limpa entity_alteracoes antigas (1x/dia).
            services.AddHostedService<EntityAlteracaoRetentionService>();
            // F10-D: Mobile alert service — verifica devices offline a cada 30min.
            services.AddHostedService<MobileAlertService>();
            // ADR-0014: cancela pedidos Storefront em AguardandoPagamento há > 30 min.
            services.AddHostedService<CancelarPedidosAbandonadosBackgroundService>();

            // TASK-EZ-AVAL-001: avaliação pós-pedido via link WhatsApp + cookie.
            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<AvaliacaoTokenService>();
            services.AddSingleton<ComentarioSanitizer>();
            services.AddSingleton<AvaliacaoCookieStore>();
            services.AddScoped<AbrirPaginaAvaliacaoUseCase>();
            services.AddScoped<CriarAvaliacaoPedidoUseCase>();
            services.AddScoped<ListarAvaliacoesPublicoUseCase>();
            services.AddScoped<EnviarLinkAvaliacaoWhatsAppHandler>();
            services.AddHostedService<AgendarSolicitacaoAvaliacaoBackgroundService>();

            return services;
        }

        private static string EnsurePoolLimits(string connectionString, IConfiguration configuration)
        {
            // Default conservador. Pode ser sobrescrito via Postgres:Pool:* (config) ou
            // se a connection string já tiver "Maximum Pool Size", respeita.
            if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;
            if (connectionString.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase))
                return connectionString;

            var maxPool = configuration.GetValue<int?>("Postgres:Pool:Max") ?? 10;
            var minPool = configuration.GetValue<int?>("Postgres:Pool:Min") ?? 0;
            var idleSec = configuration.GetValue<int?>("Postgres:Pool:IdleSeconds") ?? 60;
            var timeout = configuration.GetValue<int?>("Postgres:Pool:TimeoutSeconds") ?? 15;

            var sep = connectionString.TrimEnd().EndsWith(";") ? "" : ";";
            return connectionString + sep +
                $"Maximum Pool Size={maxPool};" +
                $"Minimum Pool Size={minPool};" +
                $"Connection Idle Lifetime={idleSec};" +
                $"Timeout={timeout};" +
                "Pooling=true;Keepalive=30";
        }
    }
}
