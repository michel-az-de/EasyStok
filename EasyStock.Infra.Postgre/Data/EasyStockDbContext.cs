using Microsoft.EntityFrameworkCore;
using EasyStock.Domain.Entities;
using System.Reflection;
using System.Threading.Tasks;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data.Configurations.Mobile;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Data
{
    public class EasyStockDbContext : DbContext, IUnitOfWork
    {
        private readonly ILogger<EasyStockDbContext>? _logger;

        public EasyStockDbContext(DbContextOptions<EasyStockDbContext> options)
            : base(options) { }

        public EasyStockDbContext(DbContextOptions<EasyStockDbContext> options, ILogger<EasyStockDbContext> logger)
            : base(options)
        {
            _logger = logger;
        }


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
        public DbSet<Cliente> Clientes { get; set; } = null!;
        public DbSet<Pedido> Pedidos { get; set; } = null!;
        public DbSet<MovimentoCaixa> MovimentosCaixa { get; set; } = null!;
        public DbSet<FechamentoCaixa> FechamentosCaixa { get; set; } = null!;
        public DbSet<FornecedorAlteracao> FornecedorAlteracoes { get; set; } = null!;
        public DbSet<VendaAlteracao> VendaAlteracoes { get; set; } = null!;
        public DbSet<Lote> Lotes { get; set; } = null!;
        public DbSet<ListaCompras> ListasCompras { get; set; } = null!;
        public DbSet<PedidoFornecedor> PedidosFornecedor { get; set; } = null!;
        public DbSet<ConfiguracaoLoja> ConfiguracoesLoja { get; set; } = null!;
        public DbSet<AnuncioIa> AnunciosIa { get; set; } = null!;
        public DbSet<UsoIa> UsoIa { get; set; } = null!;
        public DbSet<ProdutoAlteracao> ProdutoAlteracoes { get; set; } = null!;

        // Admin Module DbSets
        public DbSet<AdminTicket> AdminTickets { get; set; } = null!;
        public DbSet<AdminTicketMensagem> AdminTicketMensagens { get; set; } = null!;
        public DbSet<AdminImpersonationLog> AdminImpersonationLogs { get; set; } = null!;
        public DbSet<AdminAuditLog> AdminAuditLogs { get; set; } = null!;
        public DbSet<Cupom> Cupons { get; set; } = null!;

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
            try
            {
                return await base.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger?.LogWarning(ex,
                    "Conflito de concorrência ao persistir mudanças no EasyStockDbContext. Entidades afetadas: {Entities}",
                    string.Join(", ", ex.Entries.Select(e => e.Entity.GetType().Name)));
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger?.LogError(ex,
                    "Falha ao persistir mudanças no EasyStockDbContext. Inner: {Inner}. Entidades: {Entities}",
                    ex.InnerException?.Message,
                    string.Join(", ", ex.Entries.Select(e => e.Entity.GetType().Name)));
                throw;
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Apply all IEntityTypeConfiguration implementations in this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            // Módulo Casa da Baba Mobile — entidades prefixadas com mobile_*,
            // schema aplicado via SQL raw (MobileSchemaInitializer), mas o EF
            // precisa conhecer as entidades para queries via Set<T>().
            modelBuilder.RegisterMobileModels();
        }
    }
}
