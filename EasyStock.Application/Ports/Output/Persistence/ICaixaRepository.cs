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

        /// <summary>
        /// Tenta inserir o movimento; se colidir com <c>ix_movimentos_caixa_abertura_unica</c>
        /// (corrida perdida — outra transação já abriu o caixa do dia/loja), retorna
        /// <see langword="false"/> sem lançar, e a entidade é destrackeada. Qualquer OUTRA
        /// falha de persistência propaga normalmente (issue 951: mesmo padrão de
        /// <c>VagaOcupadaRepository.OcuparDentroDeTxAsync</c> para <c>uq_vaga_ativa_por_pedido</c>
        /// — uma constraint única simples não precisa de advisory lock, o próprio índice
        /// serializa; só checagens de threshold/contagem precisam).
        /// <para>Precisa rodar dentro de uma transação explícita já em andamento: o EF Core
        /// cria um savepoint automático antes deste <c>SaveChanges</c> quando há uma transação
        /// ambiente — se este flush falhar, só ele é revertido; mudanças já persistidas antes
        /// na mesma transação (ex.: o pagamento) permanecem íntegras até o commit final.</para>
        /// </summary>
        Task<bool> TryAddMovimentoAsync(MovimentoCaixa movimento, CancellationToken ct = default);

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

        // ── Linhas para a lista "Movimentos do dia" (BUG-5) ───────────
        /// <summary>Vendas (não-canceladas) de um intervalo [iniUtc, fimUtc) — para listar como
        /// linhas no caixa. Mesma seleção de <see cref="GetTotalVendasNoIntervaloAsync"/>, então a
        /// soma das linhas == total que entra no saldo.</summary>
        Task<IReadOnlyList<Venda>> GetVendasNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null);

        /// <summary>Pagamentos de pedidos não-cancelados de um intervalo [iniUtc, fimUtc) — para
        /// listar como linhas no caixa. Mesma seleção de
        /// <see cref="GetTotalPagamentosPedidosNoIntervaloAsync"/>.</summary>
        Task<IReadOnlyList<PedidoPagamento>> GetPagamentosPedidosListaNoIntervaloAsync(Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null);
    }
}
