using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Application.Ports.Output.Persistence;
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
            services.AddDbContext<EasyStockDbContext>(options => options.UseNpgsql(connectionString));

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
            services.AddScoped<IFornecedorRepository, FornecedorRepository>();
            services.AddScoped<IUsuarioRepository, UsuarioRepository>();
            services.AddScoped<IPerfilRepository, PerfilRepository>();
            services.AddScoped<IPlanoRepository, PlanoRepository>();
            services.AddScoped<IAssinaturaEmpresaRepository, AssinaturaEmpresaRepository>();
            services.AddScoped<IRegistrarEmpresaRepository, RegistrarEmpresaRepository>();
            services.AddScoped<IPublicadorEventos, PublicadorEventosEmMemoria>();

            // AI: usa implementacao real se Anthropic:Enabled = true, caso contrario usa stub
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
}
