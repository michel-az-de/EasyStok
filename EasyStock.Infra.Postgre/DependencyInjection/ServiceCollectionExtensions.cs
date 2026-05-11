using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Configuration;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Events;
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
                    sp.GetRequiredService<SetTenantOnConnectionInterceptor>()));

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
            services.AddScoped<IListaComprasRepository, ListaComprasRepository>();
            services.AddScoped<IPedidoFornecedorRepository, PedidoFornecedorRepository>();
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
            services.AddScoped<IMovimentacaoEstoqueAlteracaoRepository, MovimentacaoEstoqueAlteracaoRepository>();
            services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
            services.AddScoped<ICobrancaAssinaturaRepository, CobrancaAssinaturaRepository>();
            services.AddScoped<IFaturaRepository, FaturaRepository>();
            services.AddScoped<IFaturaNumeradorService, FaturaNumeradorService>();
            services.AddScoped<IWebhookRecebidoRepository, WebhookRecebidoRepository>();
            services.AddScoped<IClienteTicketRepository, ClienteTicketRepository>();
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
