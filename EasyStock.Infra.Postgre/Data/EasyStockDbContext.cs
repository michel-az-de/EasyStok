using Microsoft.EntityFrameworkCore;
using EasyStock.Domain.Entities;
using System.Reflection;
using System.Threading.Tasks;
using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Infra.Postgre.Data
{
    public class EasyStockDbContext(DbContextOptions<EasyStockDbContext> options) : DbContext(options), IUnitOfWork
    {

        // Domain DbSets
        public DbSet<Empresa> Empresas { get; set; } = null!;
        public DbSet<Categoria> Categorias { get; set; } = null!;
        public DbSet<Produto> Produtos { get; set; } = null!;
        public DbSet<ProdutoVariacao> ProdutosVariacao { get; set; } = null!;
        public DbSet<ProdutoCaracteristica> ProdutosCaracteristica { get; set; } = null!;
        public DbSet<ProdutoEmbalagem> ProdutosEmbalagem { get; set; } = null!;
        public DbSet<ItemEstoque> ItensEstoque { get; set; } = null!;
        public DbSet<Venda> Vendas { get; set; } = null!;
        public DbSet<ItemVenda> ItensVenda { get; set; } = null!;
        public DbSet<MovimentacaoEstoque> MovimentacoesEstoque { get; set; } = null!;

        public async Task<int> CommitAsync()
        {
            return await base.SaveChangesAsync();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply all IEntityTypeConfiguration implementations in this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        }
    }
}
