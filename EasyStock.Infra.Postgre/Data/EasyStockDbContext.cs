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
        public DbSet<Notificacao> Notificacoes { get; set; } = null!;
        public DbSet<Loja> Lojas { get; set; } = null!;
        public DbSet<Fornecedor> Fornecedores { get; set; } = null!;
        public DbSet<PedidoFornecedor> PedidosFornecedor { get; set; } = null!;
        public DbSet<ItemPedidoFornecedor> ItensPedidoFornecedor { get; set; } = null!;
        public DbSet<ConfiguracaoLoja> ConfiguracoesLoja { get; set; } = null!;
        public DbSet<AnuncioIa> AnunciosIa { get; set; } = null!;
        public DbSet<UsoIa> UsoIa { get; set; } = null!;

        // Identity / SaaS DbSets
        public DbSet<Usuario> Usuarios { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
        public DbSet<ResetToken> ResetTokens { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<UsuarioEmpresa> UsuariosEmpresas { get; set; } = null!;
        public DbSet<Perfil> Perfis { get; set; } = null!;
        public DbSet<PerfilPermissao> PerfisPermissoes { get; set; } = null!;
        public DbSet<UsuarioPerfil> UsuariosPerfis { get; set; } = null!;
        public DbSet<Plano> Planos { get; set; } = null!;
        public DbSet<AssinaturaEmpresa> AssinaturasEmpresa { get; set; } = null!;

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
