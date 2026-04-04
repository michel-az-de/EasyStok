using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Postgre.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddEasyStockPostgreInfrastructure(this IServiceCollection services, string connectionString)
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

            return services;
        }
    }
}
