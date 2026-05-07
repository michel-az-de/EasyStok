using Microsoft.EntityFrameworkCore;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data.Configurations.Mobile;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Data
{
    public class EasyStockDbContext : DbContext, IUnitOfWork
    {
        private readonly ILogger<EasyStockDbContext>? _logger;
        private readonly ICurrentUserAccessor? _currentUser;

        public EasyStockDbContext(DbContextOptions<EasyStockDbContext> options)
            : base(options) { }

        public EasyStockDbContext(DbContextOptions<EasyStockDbContext> options, ILogger<EasyStockDbContext> logger)
            : base(options)
        {
            _logger = logger;
        }

        public EasyStockDbContext(
            DbContextOptions<EasyStockDbContext> options,
            ICurrentUserAccessor currentUser)
            : base(options)
        {
            _currentUser = currentUser;
        }

        public EasyStockDbContext(
            DbContextOptions<EasyStockDbContext> options,
            ILogger<EasyStockDbContext> logger,
            ICurrentUserAccessor currentUser)
            : base(options)
        {
            _logger = logger;
            _currentUser = currentUser;
        }

        /// <summary>
        /// Tenant atual usado pelo <c>HasQueryFilter</c> global. Quando não há
        /// usuário autenticado (jobs, seeds, migrations) retorna
        /// <see cref="Guid.Empty"/> — o filtro elimina tudo, exigindo que o
        /// chamador use <c>IgnoreQueryFilters()</c> explicitamente.
        /// </summary>
        public Guid CurrentTenantId => _currentUser is { IsAuthenticated: true } u ? u.EmpresaId : Guid.Empty;

        /// <summary>
        /// Bypass do filtro multi-tenant para SuperAdmin (back-office cross-tenant).
        /// Usuarios normais nao podem setar isso — vem do <see cref="ICurrentUserAccessor"/>.
        /// Para jobs/seeds sem contexto, use <c>IgnoreQueryFilters()</c> diretamente.
        /// </summary>
        public bool IsSuperAdmin => _currentUser is { IsAuthenticated: true, Nivel: NivelAcesso.SuperAdmin };


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
        public DbSet<MovimentacaoEstoqueAlteracao> MovimentacaoEstoqueAlteracoes { get; set; } = null!;
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
        public DbSet<IdempotencyKey> IdempotencyKeys { get; set; } = null!;

        // Admin Module DbSets
        public DbSet<SystemErrorLog> SystemErrorLogs { get; set; } = null!;
        public DbSet<SeedRunLog> SeedRunLogs { get; set; } = null!;
        public DbSet<AdminTicket> AdminTickets { get; set; } = null!;
        public DbSet<AdminTicketMensagem> AdminTicketMensagens { get; set; } = null!;
        public DbSet<TicketAnexo> TicketAnexos { get; set; } = null!;
        public DbSet<TicketHistorico> TicketHistoricos { get; set; } = null!;
        public DbSet<AdminTicketTecnicoMeta> AdminTicketTecnicoMetas { get; set; } = null!;
        public DbSet<SlaConfiguracao> SlaConfiguracoes { get; set; } = null!;
        public DbSet<AdminImpersonationLog> AdminImpersonationLogs { get; set; } = null!;
        public DbSet<AdminAuditLog> AdminAuditLogs { get; set; } = null!;
        public DbSet<AdminAcessoPiiLog> AdminAcessosPiiLogs { get; set; } = null!;
        public DbSet<AdminNotaTenant> AdminNotasTenant { get; set; } = null!;
        public DbSet<Cupom> Cupons { get; set; } = null!;

        // Identity / SaaS DbSets
        public DbSet<Usuario> Usuarios { get; set; } = null!;
        public DbSet<RefreshToken> RefreshTokens { get; set; } = null!;
        public DbSet<ResetToken> ResetTokens { get; set; } = null!;
        public DbSet<EmailConfirmationToken> EmailConfirmationTokens { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<UsuarioEmpresa> UsuariosEmpresas { get; set; } = null!;
        public DbSet<Perfil> Perfis { get; set; } = null!;
        public DbSet<PerfilPermissao> PerfisPermissoes { get; set; } = null!;
        public DbSet<UsuarioPerfil> UsuariosPerfis { get; set; } = null!;
        public DbSet<Plano> Planos { get; set; } = null!;
        public DbSet<AssinaturaEmpresa> AssinaturasEmpresa { get; set; } = null!;
        public DbSet<CobrancaAssinatura> CobrancasAssinatura { get; set; } = null!;
        public DbSet<ConfiguracaoSistema> ConfiguracoesSistema { get; set; } = null!;

        // Modulo Financeiro (F1+)
        public DbSet<Fatura> Faturas { get; set; } = null!;
        public DbSet<FaturaItem> FaturaItens { get; set; } = null!;
        public DbSet<FaturaPagamento> FaturaPagamentos { get; set; } = null!;
        public DbSet<FaturaEvento> FaturaEventos { get; set; } = null!;
        public DbSet<FaturaContador> FaturaContadores { get; set; } = null!;
        public DbSet<WebhookRecebido> WebhookRecebidos { get; set; } = null!;

        // Landing publica — leads capturados sem multi-tenant (sem EmpresaId).
        public DbSet<LeadPublico> LeadsPublicos { get; set; } = null!;

        // Notifications module DbSets
        public DbSet<TemplateNotificacao> NotifTemplates { get; set; } = null!;
        public DbSet<VariavelTemplateCatalogo> NotifVariaveisTemplate { get; set; } = null!;
        public DbSet<RotinaNotificacao> NotifRotinas { get; set; } = null!;
        public DbSet<EventoNotificacao> NotifEventos { get; set; } = null!;
        public DbSet<OutboxMensagemNotificacao> NotifOutboxMensagens { get; set; } = null!;
        public DbSet<LogEnvioNotificacao> NotifLogsEnvio { get; set; } = null!;
        public DbSet<ConsentimentoNotificacao> NotifConsentimentos { get; set; } = null!;
        public DbSet<ConfiguracaoCanal> NotifConfiguracoesCanal { get; set; } = null!;
        public DbSet<BloqueioNotificacao> NotifBloqueios { get; set; } = null!;
        public DbSet<PreferenciaNotificacaoUsuario> NotifPreferenciasUsuario { get; set; } = null!;

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

        public async Task<IDbTransactionScope> BeginTransactionAsync(CancellationToken ct = default)
        {
            var tx = await Database.BeginTransactionAsync(ct);
            return new TransactionScope(tx);
        }

        /// <summary>
        /// Escopo transacional com semântica explícita: rollback-by-default no
        /// <c>Dispose</c> a menos que <see cref="CommitAsync"/> tenha sido chamado.
        /// Anteriormente o <c>Dispose</c> fazia auto-commit, mascarando bugs onde
        /// uma exceção pulava o commit explícito mas a transação persistia.
        /// </summary>
        private sealed class TransactionScope : IDbTransactionScope
        {
            private readonly Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction _tx;
            private bool _committed;
            private bool _disposed;

            public TransactionScope(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx)
            {
                _tx = tx;
            }

            public async Task CommitAsync(CancellationToken ct = default)
            {
                if (_committed) return;
                await _tx.CommitAsync(ct);
                _committed = true;
            }

            public async ValueTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    if (!_committed)
                    {
                        try { await _tx.RollbackAsync(); }
                        catch { /* já estava rolled-back ou conexão fechada — best-effort */ }
                    }
                }
                finally
                {
                    await _tx.DisposeAsync();
                }
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

            // Multi-tenancy global query filter — qualquer entidade com
            // propriedade EmpresaId (Guid) ganha filtro automatico
            // EmpresaId == CurrentTenantId. Anteriormente o isolamento dependia
            // 100% do dev lembrar do .Where(...) — risco LGPD/multi-tenant.
            //
            // Excecoes (admin/cross-tenant) ficam em SkipTenantFilter abaixo.
            // Endpoints SuperAdmin que precisem ler outros tenants devem usar
            // explicitamente .IgnoreQueryFilters().
            ApplyTenantQueryFilters(modelBuilder);
        }

        private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var clr = entityType.ClrType;
                if (SkipTenantFilter(clr)) continue;

                var empresaIdProp = clr.GetProperty("EmpresaId");
                if (empresaIdProp is null) continue;
                if (empresaIdProp.PropertyType != typeof(Guid) &&
                    empresaIdProp.PropertyType != typeof(Guid?)) continue;

                // Constroi: e => this.IsSuperAdmin || EF.Property<Guid>(e, "EmpresaId") == this.CurrentTenantId
                var parameter = Expression.Parameter(clr, "e");
                var efPropertyMethod = typeof(EF).GetMethod(nameof(EF.Property))!
                    .MakeGenericMethod(empresaIdProp.PropertyType);
                var efPropertyCall = Expression.Call(
                    efPropertyMethod,
                    parameter,
                    Expression.Constant("EmpresaId"));
                var contextRef = Expression.Constant(this);
                var tenantAccess = Expression.Property(contextRef, nameof(CurrentTenantId));
                Expression tenantExpr = empresaIdProp.PropertyType == typeof(Guid?)
                    ? Expression.Convert(tenantAccess, typeof(Guid?))
                    : tenantAccess;
                var equality = Expression.Equal(efPropertyCall, tenantExpr);
                var superAdminBypass = Expression.Property(contextRef, nameof(IsSuperAdmin));
                var body = Expression.OrElse(superAdminBypass, equality);
                var lambda = Expression.Lambda(body, parameter);

                modelBuilder.Entity(clr).HasQueryFilter(lambda);
            }
        }

        /// <summary>
        /// Tipos isentos do filtro global de tenant — admin tooling cross-tenant
        /// e modulo Mobile (esquema separado, escopo e a loja, nao a empresa).
        /// </summary>
        private static bool SkipTenantFilter(Type clrType)
        {
            // Modulo Casa da Baba Mobile — esquema mobile_* e escopo proprio.
            if (clrType.Namespace?.StartsWith("EasyStock.Domain.Entities.Mobile", StringComparison.Ordinal) == true)
                return true;

            // Admin tooling — auditoria/feature flags cross-tenant.
            return clrType == typeof(AdminImpersonationLog)
                || clrType == typeof(TenantFeatureFlag);
        }
    }
}
