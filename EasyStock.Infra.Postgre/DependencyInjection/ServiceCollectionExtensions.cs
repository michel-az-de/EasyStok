using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Caching;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Caching;
using EasyStock.Infra.Postgre.Configuration;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Events;
using EasyStock.Infra.Postgre.Hosting;
using EasyStock.Infra.Postgre.Services;
using Microsoft.EntityFrameworkCore;
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
            services.AddSingleton<ISubscriptionStatusCache, SubscriptionStatusCache>();
            services.AddSingleton<AssinaturaCacheInvalidationInterceptor>();
            services.AddScoped<EntityChangeInterceptor>();
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
                    sp.GetRequiredService<AssinaturaCacheInvalidationInterceptor>(),
                    sp.GetRequiredService<EntityChangeInterceptor>()));

            services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<EasyStockDbContext>());
            services.AddScoped<ICategoriaRepository, CategoriaRepository>();
            services.AddScoped<IEmpresaRepository, EmpresaRepository>();
            services.AddScoped<IProdutoRepository, ProdutoRepository>();
            services.AddScoped<IProdutoVariacaoRepository, ProdutoVariacaoRepository>();
            services.AddScoped<IProdutoCaracteristicaRepository, ProdutoCaracteristicaRepository>();
            services.AddScoped<IProdutoEmbalagemRepository, ProdutoEmbalagemRepository>();
            services.AddScoped<IItemEstoqueRepository, ItemEstoqueRepository>();
            services.AddScoped<IItemVendaRepository, ItemVendaRepository>();
            services.AddScoped<IMovimentacaoEstoqueRepository, MovimentacaoEstoqueRepository>();
            services.AddScoped<IVendaRepository, VendaRepository>();
            services.AddScoped<INotificacaoRepository, NotificacaoRepository>();
            services.AddScoped<ILojaRepository, LojaRepository>();
            services.AddScoped<IConfiguracaoLojaRepository, ConfiguracaoLojaRepository>();
            services.AddScoped<IFornecedorRepository, FornecedorRepository>();
            services.AddScoped<IClienteRepository, ClienteRepository>();
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
            services.AddScoped<ICupomRepository, CupomRepository>();
            services.AddScoped<IUsuarioEmpresaRepository, UsuarioEmpresaRepository>();
            services.AddScoped<IUsuarioPerfilRepository, UsuarioPerfilRepository>();
            services.AddScoped<IAuditLogRepository, AuditLogRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IResetTokenRepository, ResetTokenRepository>();
            services.AddScoped<IEmailConfirmationTokenRepository, EmailConfirmationTokenRepository>();
            services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
            services.AddScoped<IAnuncioIaRepository, AnuncioIaRepository>();
            services.AddScoped<IUsoIaRepository, UsoIaRepository>();
            services.AddScoped<IProdutoAlteracaoRepository, ProdutoAlteracaoRepository>();
            services.AddScoped<IProdutoComposicaoRepository, ProdutoComposicaoRepository>();
            services.AddScoped<IProdutoComposicaoAlteracaoRepository, ProdutoComposicaoAlteracaoRepository>();
            services.AddScoped<IMovimentacaoEstoqueAlteracaoRepository, MovimentacaoEstoqueAlteracaoRepository>();
            services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
            services.AddScoped<ICobrancaAssinaturaRepository, CobrancaAssinaturaRepository>();
            services.AddScoped<IFaturaRepository, FaturaRepository>();
            services.AddScoped<IFaturaNumeradorService, FaturaNumeradorService>();
            services.AddScoped<ILancamentoRepository, LancamentoRepository>();
            services.AddScoped<IWebhookRecebidoRepository, WebhookRecebidoRepository>();

            // Onda P0 Payment Orchestration
            services.AddScoped<EasyStock.Application.Ports.Output.Pagamentos.IPaymentAttemptRepository,
                Repositories.Pagamentos.PaymentAttemptRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Pagamentos.IGatewayRoutingRuleRepository,
                Repositories.Pagamentos.GatewayRoutingRuleRepository>();

            // Modulo Contas a Pagar / Contas a Receber (CAP/CAR)
            services.AddScoped<IContaPagarRepository, ContaPagarRepository>();
            services.AddScoped<IContaReceberRepository, ContaReceberRepository>();
            services.AddScoped<ICategoriaFinanceiraRepository, CategoriaFinanceiraRepository>();
            services.AddScoped<ICentroCustoRepository, CentroCustoRepository>();
            services.AddScoped<IFluxoCaixaQueries, FluxoCaixaQueries>();

            services.AddScoped<IClienteTicketRepository, ClienteTicketRepository>();
            services.AddScoped<IAdminTicketRepository, AdminTicketRepository>();
            services.AddScoped<IFaqRepository, FaqRepository>();
            services.AddScoped<IFaqAdminRepository, FaqAdminRepository>();
            services.AddScoped<ILeadPublicoRepository, LeadPublicoRepository>();
            services.AddScoped<IAdminTenantsQueries, AdminTenantsQueries>();
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

            // Modulo Fiscal NFC-e (F1) — repositorios + servicos sobre Nfe*
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.INfeRepository,
                Repositories.Fiscal.NfeRepository>();
            services.AddScoped<EasyStock.Application.Ports.Output.Fiscal.INumeracaoNfeService,
                Repositories.Fiscal.NumeracaoNfeService>();
            services.AddScoped<EasyStock.Application.Ports.Output.Fiscal.IGeradorChaveAcesso,
                Repositories.Fiscal.GeradorChaveAcesso>();
            services.AddScoped<EasyStock.Application.Ports.Output.Fiscal.ICertificadoA1Repository,
                Repositories.Fiscal.NfeCertificadoA1Repository>();
            services.AddScoped<EasyStock.Application.Services.Fiscal.IConfigFiscalResolver,
                Services.ConfigFiscalResolver>();
            services.AddScoped<EasyStock.Application.Ports.Output.Security.IRowLevelSecurityBypass,
                Security.RowLevelSecurityBypass>();
            // TODO F2: registrar IGatewayFiscal (FocusNFeAdapter) e INfeCertificadoA1Service em EasyStock.Infra.Integrations.Fiscal

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
            services.AddScoped<EasyStock.Application.Ports.Output.Persistence.Storefront.IStorefrontFaleConoscoRepository,
                Repositories.Storefront.StorefrontFaleConoscoRepository>();

            // Notification repositories (Templates, Rotinas, Outbox, Consentimentos, etc.)
            services.AddEasyStockNotificationsRepositories();

            // AI: OpenAI tem prioridade; Anthropic como fallback; stub se nenhum habilitado
            var openAiEnabled = configuration.GetValue<bool>("OpenAI:Enabled");
            var anthropicEnabled = configuration.GetValue<bool>("Anthropic:Enabled");

            if (openAiEnabled)
            {
                services.AddHttpClient("OpenAI");
                services.AddScoped<IGeradorDescricaoAnuncio, GeradorDescricaoAnuncioOpenAI>();
                services.AddScoped<IGeradorDescricaoAnuncioStreaming, GeradorDescricaoAnuncioOpenAIStreaming>();
                services.AddScoped<IGeradorAutoPreenchimento, GeradorAutoPreenchimentoOpenAI>();
            }
            else if (anthropicEnabled)
            {
                services.AddHttpClient("Anthropic");
                services.AddScoped<IGeradorDescricaoAnuncio, GeradorDescricaoAnuncioClaude>();
                services.AddScoped<IGeradorDescricaoAnuncioStreaming, GeradorDescricaoAnuncioClaudeStreaming>();
                services.AddScoped<IGeradorAutoPreenchimento, GeradorAutoPreenchimentoClaude>();
            }
            else
            {
                services.AddScoped<IGeradorDescricaoAnuncio, GeradorDescricaoAnuncioStub>();
                services.AddScoped<IGeradorDescricaoAnuncioStreaming, GeradorDescricaoAnuncioStubStreaming>();
                services.AddScoped<IGeradorAutoPreenchimento, GeradorAutoPreenchimentoStub>();
            }

            // F10-B: Retention service — limpa entity_alteracoes antigas (1x/dia).
            services.AddHostedService<EntityAlteracaoRetentionService>();
            // F10-D: Mobile alert service — verifica devices offline a cada 30min.
            services.AddHostedService<MobileAlertService>();

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
