namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface ICaixaRepository
    {
        // ── Movimentos ────────────────────────────────────────────
        Task<MovimentoCaixa?> GetMovimentoAsync(Guid empresaId, Guid id);

        Task<(IEnumerable<MovimentoCaixa> items, int total)> ListMovimentosAsync(
            Guid empresaId,
            int page,
            int pageSize,
            string? tipo = null,
            DateTime? desde = null,
            DateTime? ate = null,
            bool incluirEstornados = false,
            string? sort = "datamovimento",
            string? order = "desc");

        /// <summary>Movimentos não-estornados de um dia específico (resumo do caixa).</summary>
        Task<IEnumerable<MovimentoCaixa>> GetMovimentosDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null);

        /// <summary>Movimentos não-estornados num intervalo de instante real [iniUtc, fimUtc).
        /// Base cross-day: agrega a sessão de caixa que pode atravessar a meia-noite BRT.</summary>
        Task<IEnumerable<MovimentoCaixa>> GetMovimentosNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null);

        /// <summary>Última abertura sem fechamento posterior (sessão em aberto, possivelmente de um
        /// dia anterior). Null se o último evento abertura/fechamento foi um fechamento. Espelha a
        /// lógica de "último evento" do dashboard (AnalyticsRepository.ResumoDia) — issue #596.</summary>
        Task<MovimentoCaixa?> GetAberturaPendenteAsync(Guid empresaId, Guid? lojaId = null);

        /// <summary>
        /// Aberturas pendentes (sem fechamento posterior na mesma empresa/loja) de TODOS os
        /// tenants cujo instante é anterior a <paramref name="limiteInferiorUtc"/> (00:00 BRT de
        /// hoje em UTC) e cujo dia operacional BRT é anterior a hoje — i.e. caixas esquecidos
        /// abertos de dias anteriores. Usado pelo CaixaEsquecidoJob.
        /// <para>REQUER que o caller ligue <c>UseRowLevelSecurityBypass()</c> ANTES de abrir a
        /// conexão (cross-tenant; ver CaixaEsquecidoCrossTenantRlsTests). NÃO usar em request HTTP.
        /// Agrupa por (empresa, loja) — difere do null-loja loja-agnóstico de
        /// <see cref="GetAberturaPendenteAsync"/> (ADR-0034).</para>
        /// </summary>
        Task<IReadOnlyList<MovimentoCaixa>> GetAberturasEsquecidasAsync(DateTime limiteInferiorUtc, CancellationToken ct = default);

        Task AddMovimentoAsync(MovimentoCaixa movimento);
        Task UpdateMovimentoAsync(MovimentoCaixa movimento);

        // ── Fechamentos ───────────────────────────────────────────
        Task<FechamentoCaixa?> GetFechamentoDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null);

        Task<(IEnumerable<FechamentoCaixa> items, int total)> ListFechamentosAsync(
            Guid empresaId, int page, int pageSize,
            DateOnly? desde = null, DateOnly? ate = null);

        Task AddFechamentoAsync(FechamentoCaixa fechamento);

        // ── Agregadores pra ObterCaixaDia ─────────────────────────
        /// <summary>Soma de Vendas do dia (não-canceladas) para a empresa+loja.</summary>
        Task<decimal> GetTotalVendasDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null);

        /// <summary>Soma de Vendas (não-canceladas) num intervalo de instante real [iniUtc, fimUtc).</summary>
        Task<decimal> GetTotalVendasNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null);

        /// <summary>Soma de pagamentos de pedidos não-cancelados naquele dia.</summary>
        Task<decimal> GetTotalPagamentosPedidosDoDiaAsync(Guid empresaId, DateOnly data, Guid? lojaId = null);

        /// <summary>Soma de pagamentos de pedidos não-cancelados num intervalo [iniUtc, fimUtc).</summary>
        Task<decimal> GetTotalPagamentosPedidosNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null);
    }
}
