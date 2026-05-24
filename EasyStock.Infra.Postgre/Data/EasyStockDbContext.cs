using Microsoft.EntityFrameworkCore;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Financeiro;
using EasyStock.Domain.Financeiro.Events;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Integration;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Reporting;
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
        /// Override explícito de tenant para fluxos SEM principal JWT (módulo
        /// Mobile, autenticado por <c>X-Mobile-Api-Key</c>). Setado via
        /// <see cref="SetMobileTenantContext"/>; tem precedência sobre o claim.
        /// </summary>
        private Guid? _tenantContextOverride;

        /// <summary>
        /// Tenant atual usado pelo <c>HasQueryFilter</c> global E pelo
        /// <c>SetTenantOnConnectionInterceptor</c> (que emite <c>SET app.empresa_id</c>).
        /// Ordem de resolução: override explícito (Mobile) → claim do JWT →
        /// <see cref="Guid.Empty"/> (jobs/seeds/migrations, que devem usar
        /// <c>IgnoreQueryFilters()</c> + bypass).
        /// </summary>
        public Guid CurrentTenantId =>
            _tenantContextOverride
            ?? (_currentUser is { IsAuthenticated: true } u ? u.EmpresaId : Guid.Empty);

        /// <summary>
        /// Define o tenant da conexão quando NÃO há principal JWT — o caso do
        /// módulo Mobile (device via <c>X-Mobile-Api-Key</c>). Sem isto,
        /// <see cref="CurrentTenantId"/> cairia em <see cref="Guid.Empty"/> e a
        /// policy RLS <c>tenant_isolation</c> (fail-closed) zeraria as linhas ERP
        /// no sync reverso (web→mobile) e bloquearia o auto-link mobile→ERP por
        /// <c>WITH CHECK</c> — silenciosamente. Com o tenant do device setado, o
        /// interceptor emite <c>SET app.empresa_id</c> correto e o RLS libera
        /// EXATAMENTE aquele tenant (isolamento preservado). O DbContext é scoped
        /// por request, então o valor não vaza entre requests.
        /// </summary>
        public void SetMobileTenantContext(Guid empresaId) => _tenantContextOverride = empresaId;

        /// <summary>
        /// Bypass do filtro multi-tenant para SuperAdmin (back-office cross-tenant).
        /// Usuarios normais nao podem setar isso — vem do <see cref="ICurrentUserAccessor"/>.
        /// Para jobs/seeds sem contexto, use <c>IgnoreQueryFilters()</c> diretamente.
        /// </summary>
        public bool IsSuperAdmin => _currentUser is { IsAuthenticated: true, Nivel: NivelAcesso.SuperAdmin };

        /// <summary>
        /// Quando <c>true</c>, o <c>SetTenantOnConnectionInterceptor</c> emite
        /// <c>SET app.bypass_rls = 'true'</c> na próxima abertura de conexão,
        /// fazendo a policy <c>tenant_isolation</c> aceitar qualquer linha.
        /// <para>
        /// É usado por código cross-tenant deliberado: migrations, schema
        /// bootstrap, SuperAdmin seed, jobs de reconciliação, login pré-auth
        /// (sem <c>CurrentTenantId</c>). Nunca expor a flag a controllers/handlers
        /// — eles devem confiar em <see cref="IsSuperAdmin"/>, que vem do JWT.
        /// </para>
        /// <para>
        /// Use <see cref="UseRowLevelSecurityBypass"/> para escopo curto via
        /// <c>using</c> — defesa em profundidade real, já que
        /// <c>IgnoreQueryFilters</c> sozinho não basta: ele desliga o filtro
        /// EF, mas a policy do Postgres ainda zera as linhas.
        /// </para>
        /// </summary>
        public bool BypassRowLevelSecurity { get; private set; }

        /// <summary>
        /// Liga <see cref="BypassRowLevelSecurity"/> e devolve um
        /// <see cref="IDisposable"/> que desliga ao sair do escopo. Cobre os
        /// casos onde precisamos ler/escrever cross-tenant: jobs noturnos,
        /// reconciliação de pagamento, login pré-JWT, seed de SuperAdmin.
        /// <para>
        /// O interceptor reaplica o setting na próxima abertura de conexão.
        /// Se a sua operação roda dentro de uma única conexão já aberta, force
        /// reabertura ou execute <c>SET app.bypass_rls</c> manualmente — mas o
        /// caso normal (cada use case abre conexão nova via repository) já
        /// funciona transparente.
        /// </para>
        /// </summary>
        public IDisposable UseRowLevelSecurityBypass()
        {
            var previous = BypassRowLevelSecurity;
            BypassRowLevelSecurity = true;
            return new RlsBypassScope(this, previous);
        }

        private sealed class RlsBypassScope : IDisposable
        {
            private readonly EasyStockDbContext _ctx;
            private readonly bool _previous;
            private bool _disposed;

            public RlsBypassScope(EasyStockDbContext ctx, bool previous)
            {
                _ctx = ctx;
                _previous = previous;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _ctx.BypassRowLevelSecurity = _previous;
            }
        }


        // Domain DbSets
        public DbSet<Empresa> Empresas { get; set; } = null!;

        // Storefront aggregates (ver ADR-0002 multi-tenancy, ADR-0014 vaga lifecycle)
        public DbSet<EasyStock.Domain.Entities.Storefront.Storefront> Storefronts { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Storefront.CardapioItem> CardapioItens { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Storefront.WebhookProcessado> WebhooksProcessados { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Storefront.CheckoutIdempotency> CheckoutsIdempotency { get; set; } = null!;
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
        public DbSet<EtiquetaTemplateSistema> EtiquetaTemplatesSistema { get; set; } = null!;
        public DbSet<EtiquetaTemplate> EtiquetaTemplates { get; set; } = null!;
        public DbSet<EtiquetaEmpresaDefault> EtiquetaEmpresaDefaults { get; set; } = null!;
        public DbSet<ListaCompras> ListasCompras { get; set; } = null!;
        public DbSet<PedidoFornecedor> PedidosFornecedor { get; set; } = null!;
        public DbSet<ConfiguracaoLoja> ConfiguracoesLoja { get; set; } = null!;
        public DbSet<AnuncioIa> AnunciosIa { get; set; } = null!;
        public DbSet<UsoIa> UsoIa { get; set; } = null!;
        public DbSet<ProdutoAlteracao> ProdutoAlteracoes { get; set; } = null!;
        public DbSet<ProdutoComposicao> ProdutosComposicao { get; set; } = null!;
        public DbSet<ProdutoComposicaoAlteracao> ProdutosComposicaoAlteracoes { get; set; } = null!;
        public DbSet<EntityAlteracao> EntityAlteracoes { get; set; } = null!;
        public DbSet<MobileProcessedMutation> MobileProcessedMutations { get; set; } = null!;
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

        // FAQ — base global publica (sem multi-tenant)
        public DbSet<FaqCategoria> FaqCategorias { get; set; } = null!;
        public DbSet<FaqItem> FaqItens { get; set; } = null!;
        public DbSet<FaqVisualizacao> FaqVisualizacoes { get; set; } = null!;
        public DbSet<FaqFeedback> FaqFeedbacks { get; set; } = null!;

        public DbSet<AdminImpersonationLog> AdminImpersonationLogs { get; set; } = null!;
        public DbSet<AdminAuditLog> AdminAuditLogs { get; set; } = null!;
        public DbSet<AdminAcessoPiiLog> AdminAcessosPiiLogs { get; set; } = null!;
        public DbSet<AdminNotaTenant> AdminNotasTenant { get; set; } = null!;
        public DbSet<Cupom> Cupons { get; set; } = null!;

        // Endpoint health monitoring (Worker EndpointHealthMonitorService)
        public DbSet<EndpointHealthState> EndpointHealthStates { get; set; } = null!;

        // Releases de APK distribuidos via CapacitorUpdater (Casa da Baba e outros).
        public DbSet<EasyStock.Domain.Entities.Mobile.ApkRelease> ApkReleases { get; set; } = null!;

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

        // Payment Orchestration (Onda P0)
        public DbSet<PaymentAttempt> PaymentAttempts { get; set; } = null!;
        public DbSet<PaymentAttemptEvent> PaymentAttemptEvents { get; set; } = null!;
        public DbSet<GatewayRoutingRule> GatewayRoutingRules { get; set; } = null!;
        public DbSet<GatewayHealthSnapshot> GatewayHealthSnapshots { get; set; } = null!;

        // Modulo Financeiro AR/AP — lancamentos previstos/realizados do tenant
        public DbSet<Lancamento> Lancamentos { get; set; } = null!;
        public DbSet<LancamentoBaixa> LancamentoBaixas { get; set; } = null!;

        // Modulo Contas a Pagar / Contas a Receber (CAP/CAR)
        public DbSet<EasyStock.Domain.Entities.Financeiro.CategoriaFinanceira> CategoriasFinanceiras { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.CentroCusto> CentrosCusto { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.ContaPagar> ContasPagar { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.ContaPagarAlteracao> ContaPagarAlteracoes { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.ContaReceber> ContasReceber { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.ContaReceberAlteracao> ContaReceberAlteracoes { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.ParcelaPagar> ParcelasPagar { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.ParcelaReceber> ParcelasReceber { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.PagamentoParcela> PagamentosParcela { get; set; } = null!;
        public DbSet<EasyStock.Domain.Entities.Financeiro.ContaFinanceiraEvento> ContasFinanceirasEventos { get; set; } = null!;

        // Landing publica — leads capturados sem multi-tenant (sem EmpresaId).
        public DbSet<LeadPublico> LeadsPublicos { get; set; } = null!;

        // Modulo Integration (F3+) — credenciais cifradas (AES-256-GCM) por tenant
        public DbSet<CredencialIntegracao> CredenciaisIntegracao { get; set; } = null!;

        // Modulo Integration (F4+) — outbox transacional de eventos externos
        public DbSet<OutboxEventoIntegracao> OutboxEventosIntegracao { get; set; } = null!;

        // Modulo Fiscal (NFC-e Corte 1) — fundacao Domain pra emissao via Focus/eNotas
        public DbSet<EmpresaConfiguracaoFiscal> EmpresaConfiguracoesFiscais { get; set; } = null!;
        public DbSet<NfeDocumento> NfeDocumentos { get; set; } = null!;
        public DbSet<NfeItem> NfeItens { get; set; } = null!;
        public DbSet<NfeEvento> NfeEventos { get; set; } = null!;

        // Módulo de Relatórios — motor assíncrono multi-tenant
        public DbSet<ReportRun> ReportRuns { get; set; } = null!;

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
        public DbSet<WebPushSubscription> NotifWebPushSubscriptions { get; set; } = null!;

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

        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> action,
            CancellationToken ct = default)
        {
            // CreateExecutionStrategy reusa a politica do provedor (EnableRetryOnFailure
            // do Npgsql). Sem isso, BeginTransactionAsync direto perde a capacidade
            // de retry sob falha transitoria — exceção propaga ao caller.
            var strategy = Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(ct, async (token) =>
            {
                await using var tx = await Database.BeginTransactionAsync(token);
                await action(token);
                await tx.CommitAsync(token);
            });
        }

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken ct = default)
        {
            var strategy = Database.CreateExecutionStrategy();
            return strategy.ExecuteAsync(ct, async (token) =>
            {
                await using var tx = await Database.BeginTransactionAsync(token);
                var result = await action(token);
                await tx.CommitAsync(token);
                return result;
            });
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

            // Domain events nao sao entidades persistidas — sao publicados pelo UseCase
            // apos commit. EF Core descobriria via convencao a partir das colecoes
            // de eventos pendentes nos agregados, daria erro de PK obrigatoria.
            modelBuilder.Ignore<LancamentoBaixadoEvent>();

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

            // ReportRun — EmpresaId é nullable (null para contexto AdminSaaS).
            // Isolamento multi-tenant tratado manualmente em WorkerCurrentUserAccessor
            // e ITenantScopedQueryBuilder (defesa em profundidade §ADR-R07).
            // Se aplicarmos o filtro global, runs AdminSaaS (EmpresaId=null)
            // jamais seriam retornadas e o filter quebraria para elas.
            if (clrType == typeof(ReportRun)) return true;

            // Admin tooling — auditoria/feature flags cross-tenant.
            // GatewayRoutingRule: EmpresaId nullable (NULL = regra global) e o repository
            // filtra manualmente "EmpresaId == tenant OR EmpresaId IS NULL". Filtro
            // automatico por igualdade eliminaria as regras globais.
            // FaturaContador — tabela auxiliar com PK composta (EmpresaId, Ano).
            // Acesso direto via SQL raw (INSERT...ON CONFLICT) ou lookup por PK no
            // fallback; sem necessidade de filter.
            return clrType == typeof(AdminImpersonationLog)
                || clrType == typeof(TenantFeatureFlag)
                || clrType == typeof(GatewayRoutingRule)
                || clrType == typeof(FaturaContador);
        }
    }
}
