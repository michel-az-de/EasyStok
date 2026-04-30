using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Configuration;
using EasyStock.Infra.Postgre.Data;
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

            services.AddDbContext<EasyStockDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                {
                    npgsql.MigrationsAssembly("EasyStock.Infra.Postgre");
                    npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
                    npgsql.CommandTimeout(30);
                    npgsql.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                }));

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
            services.AddScoped<IUsuarioEmpresaRepository, UsuarioEmpresaRepository>();
            services.AddScoped<IUsuarioPerfilRepository, UsuarioPerfilRepository>();
            services.AddScoped<IAuditLogRepository, AuditLogRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
            services.AddScoped<IResetTokenRepository, ResetTokenRepository>();
            services.AddScoped<IAnalyticsRepository, AnalyticsRepository>();
            services.AddScoped<IAnuncioIaRepository, AnuncioIaRepository>();
            services.AddScoped<IUsoIaRepository, UsoIaRepository>();
            services.AddScoped<IProdutoAlteracaoRepository, ProdutoAlteracaoRepository>();
            services.AddScoped<IPublicadorEventos, PublicadorEventosEmMemoria>();

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
    }
}
