using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Caching;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Caching;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using EasyStock.Infra.Postgre.Events;
using EasyStock.Infra.Postgre.Repositories;
using EasyStock.Infra.Postgre.Services;
using EasyStock.Infra.Sqlite.HealthChecks;
using EasyStock.Infra.Sqlite.Startup;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Sqlite.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockSqliteInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddSingleton<AuditTimestampsInterceptor>();
        // IMemoryCache e a config CacheOptions sao requeridos pelo
        // SubscriptionStatusCache. AddMemoryCache e idempotente; Configure
        // garante o IOptions vazio com defaults se ninguem mais registrou.
        services.AddMemoryCache();
        services.Configure<EasyStock.Infra.Postgre.Configuration.CacheOptions>(_ => { });
        services.AddSingleton<ISubscriptionStatusCache, SubscriptionStatusCache>();
        services.AddSingleton<AssinaturaCacheInvalidationInterceptor>();
        services.AddDbContext<EasyStockDbContext>((sp, options) =>
            options.UseSqlite(connectionString, sqlite =>
                sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(
                sp.GetRequiredService<AuditTimestampsInterceptor>(),
                sp.GetRequiredService<AssinaturaCacheInvalidationInterceptor>()));

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
        services.AddScoped<IPedidoFornecedorRepository, PedidoFornecedorRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IPerfilRepository, PerfilRepository>();
        services.AddScoped<IPlanoRepository, PlanoRepository>();
        services.AddScoped<IAssinaturaEmpresaRepository, AssinaturaEmpresaRepository>();
        services.AddScoped<IUsuarioEmpresaRepository, UsuarioEmpresaRepository>();
        services.AddScoped<IUsuarioPerfilRepository, UsuarioPerfilRepository>();
        services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
        services.AddScoped<IAnuncioIaRepository, AnuncioIaRepository>();
        services.AddScoped<IUsoIaRepository, UsoIaRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IListaComprasRepository, ListaComprasRepository>();
        services.AddScoped<IProdutoAlteracaoRepository, ProdutoAlteracaoRepository>();
        services.AddScoped<ICobrancaAssinaturaRepository, CobrancaAssinaturaRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IResetTokenRepository, ResetTokenRepository>();
        // Paridade com Postgre (anteriormente faltava 22 repos -> 17 Api.IntegrationTests
        // falhavam por ValidateOnBuild com Sqlite). Sqlite reusa as Repository classes do
        // EasyStock.Infra.Postgre.Repositories porque ambos usam o MESMO EasyStockDbContext
        // (apenas o provider EF muda — UseSqlite vs UseNpgsql). Repos que dependem de
        // features Postgres-only (FOR UPDATE, ILIKE, xmin) podem nao funcionar em runtime
        // sqlite, mas o ValidateOnBuild passa e o caminho dev/test fica consistente.
        services.AddScoped<IEmailConfirmationTokenRepository, EmailConfirmationTokenRepository>();
        services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
        services.AddScoped<ILancamentoRepository, LancamentoRepository>();
        services.AddScoped<IFaturaRepository, FaturaRepository>();
        services.AddScoped<IContaPagarRepository, ContaPagarRepository>();
        services.AddScoped<IContaReceberRepository, ContaReceberRepository>();
        services.AddScoped<ICategoriaFinanceiraRepository, CategoriaFinanceiraRepository>();
        services.AddScoped<ICentroCustoRepository, CentroCustoRepository>();
        services.AddScoped<ICupomRepository, CupomRepository>();
        services.AddScoped<IPedidoFornecedorItemRepository, PedidoFornecedorItemRepository>();
        services.AddScoped<IEtiquetaTemplateRepository, EtiquetaTemplateRepository>();
        services.AddScoped<IMovimentacaoEstoqueAlteracaoRepository, MovimentacaoEstoqueAlteracaoRepository>();
        services.AddScoped<IProdutoComposicaoRepository, ProdutoComposicaoRepository>();
        services.AddScoped<IProdutoComposicaoAlteracaoRepository, ProdutoComposicaoAlteracaoRepository>();
        services.AddScoped<IClienteTicketRepository, ClienteTicketRepository>();
        services.AddScoped<IAdminTicketRepository, AdminTicketRepository>();
        services.AddScoped<IFaqRepository, FaqRepository>();
        services.AddScoped<IFaqAdminRepository, FaqAdminRepository>();
        services.AddScoped<ILeadPublicoRepository, LeadPublicoRepository>();
        services.AddScoped<ICredencialIntegracaoRepository, CredencialIntegracaoRepository>();
        services.AddScoped<IOutboxEventoIntegracaoRepository, OutboxEventoIntegracaoRepository>();
        services.AddScoped<IWebhookRecebidoRepository, WebhookRecebidoRepository>();
        services.AddScoped<IPublicadorEventos, PublicadorEventosEmMemoria>();

        // AI: usa implementacao real se Anthropic:Enabled = true, caso contrario usa stub
        var anthropicEnabled = configuration.GetValue<bool>("Anthropic:Enabled");
        if (anthropicEnabled)
        {
            services.AddHttpClient("Anthropic");
            services.AddScoped<IGeradorDescricaoAnuncio, GeradorDescricaoAnuncioClaude>();
            services.AddScoped<IGeradorDescricaoAnuncioStreaming, GeradorDescricaoAnuncioClaudeStreaming>();
        }
        else
        {
            services.AddScoped<IGeradorDescricaoAnuncio, GeradorDescricaoAnuncioStub>();
            services.AddScoped<IGeradorDescricaoAnuncioStreaming, GeradorDescricaoAnuncioStubStreaming>();
        }
        services.AddScoped<IGeradorAutoPreenchimento, GeradorAutoPreenchimentoStub>();

        // Health check registered as singleton because it has no per-request state
        services.AddSingleton<SqliteDatabaseHealthCheck>();

        // Inicializa o schema do SQLite na subida da aplicacao
        services.AddHostedService<SqliteInitializerHostedService>();

        return services;
    }
}
