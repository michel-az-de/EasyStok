using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.MongoDb.Data;
using EasyStock.Infra.MongoDb.Events;
using EasyStock.Infra.MongoDb.HealthChecks;
using EasyStock.Infra.MongoDb.Repositories;
using EasyStock.Infra.MongoDb.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEasyStockMongoInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string databaseName,
        IConfiguration configuration)
    {
        MongoClassMapRegistrar.RegisterAll();

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
        services.AddScoped(sp => new MongoEasyStockContext(sp.GetRequiredService<IMongoClient>(), databaseName));
        services.AddScoped<MongoUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<MongoUnitOfWork>());

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
        services.AddScoped<IPedidoFornecedorRepository, PedidoFornecedorRepository>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IPerfilRepository, PerfilRepository>();
        services.AddScoped<IPlanoRepository, PlanoRepository>();
        services.AddScoped<IAssinaturaEmpresaRepository, AssinaturaEmpresaRepository>();
        services.AddScoped<IUsuarioEmpresaRepository, UsuarioEmpresaRepository>();
        services.AddScoped<IUsuarioPerfilRepository, UsuarioPerfilRepository>();
        services.AddScoped<IPublicadorEventos, PublicadorEventosEmMemoria>();

        services.AddSingleton<MongoDatabaseHealthCheck>(sp => new MongoDatabaseHealthCheck(sp.GetRequiredService<IMongoClient>(), databaseName));
        services.AddScoped<MongoMigrationRunner>();
        services.AddHostedService<MongoMigrationHostedService>();

        var anthropicEnabled = configuration.GetValue<bool>("Anthropic:Enabled");
        if (anthropicEnabled)
        {
            services.AddHttpClient("Anthropic");
            services.AddScoped<IGeradorDescricaoAnuncio, GeradorDescricaoAnuncioClaude>();
        }
        else
        {
            services.AddScoped<IGeradorDescricaoAnuncio, GeradorDescricaoAnuncioStub>();
        }

        return services;
    }
}
