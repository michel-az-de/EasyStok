using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
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
        services.AddDbContext<EasyStockDbContext>((sp, options) =>
            options.UseSqlite(connectionString, sqlite =>
                sqlite.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .AddInterceptors(sp.GetRequiredService<AuditTimestampsInterceptor>()));

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
