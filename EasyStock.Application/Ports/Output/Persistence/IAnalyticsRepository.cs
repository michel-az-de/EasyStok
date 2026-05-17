using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Persistence
{
    // DTOs

    public sealed record DashboardResumo(
        Guid EmpresaId,
        int Periodo,
        int TotalSkus,
        int QuantidadeTotalEmEstoque,
        decimal ValorTotalEstoque,
        decimal ValorCustoEstoque,
        decimal MediaVendasDiaria,
        decimal ProjecaoVendasPeriodo,
        decimal ReceitaEstimadaPeriodo,
        int AlertasEstoqueBaixo,
        int AlertasVencimento,
        int AlertasItensParados);

    public sealed record ReceitaPorPeriodo(
        int Ano,
        int Mes,
        decimal ReceitaBruta,
        int TotalVendas,
        int TotalItensVendidos,
        decimal TicketMedio);

    public sealed record MargemPorProduto(
        Guid ProdutoId,
        string NomeProduto,
        decimal CustoMedio,
        decimal PrecoMedioVenda,
        decimal MargemAbsoluta,
        decimal MargemPercentual,
        int QuantidadeVendida);

    public sealed record MovimentacaoResumo(
        int Ano,
        int Mes,
        int Dia,
        TipoMovimentacaoEstoque Tipo,
        int TotalMovimentacoes,
        int QuantidadeTotal,
        decimal ValorTotal);

    public sealed record ValidadeAlerta(
        Guid ItemEstoqueId,
        Guid ProdutoId,
        string? NomeProduto,
        string? CodigoInterno,
        int QuantidadeAtual,
        DateTime DataValidade,
        int DiasAteVencimento,
        decimal ValorEmRisco);

    public sealed record ItemParadoDetalhe(
        Guid ItemEstoqueId,
        Guid ProdutoId,
        string? NomeProduto,
        string? CodigoInterno,
        int QuantidadeAtual,
        DateTime? UltimaMovimentacaoEm,
        int DiasSemMovimentacao,
        decimal ValorParado);

    public sealed record SazonalidadeMensal(
        int Ano,
        int Mes,
        int TotalSaidas,
        decimal ValorTotal,
        decimal MediaMovelTresMeses);

    public sealed record ReposicaoSugerida(
        Guid ItemEstoqueId,
        Guid ProdutoId,
        string? NomeProduto,
        string? CodigoInterno,
        int QuantidadeAtual,
        int QuantidadeMinima,
        int QuantidadeSugeridaReposicao,
        decimal VelocidadeSaidaDiaria,
        int? DiasAteRuptura,
        decimal CustoEstimadoReposicao);

    public sealed record ProjecaoRuptura(
        Guid ItemEstoqueId,
        Guid ProdutoId,
        string? NomeProduto,
        string? CodigoInterno,
        int QuantidadeAtual,
        decimal TaxaSaidaDiaria,
        int? DiasAteRuptura,
        DateTime? DataEstimadaRuptura);

    public sealed record VendaPorCanal(
        CanalVenda Canal,
        int TotalVendas,
        int TotalItensVendidos,
        decimal ReceitaTotal,
        decimal TicketMedio,
        decimal PercentualReceita);

    // ── Store Intelligence DTOs ──────────────────────────────────────────

    public sealed record LojaResumoInteligencia(
        Guid LojaId,
        string NomeLoja,
        int TotalSkus,
        int QuantidadeTotalEmEstoque,
        decimal ValorTotalEstoque,
        decimal ValorCustoEstoque,
        int AlertasEstoqueBaixo,
        int AlertasVencimento,
        int AlertasItensParados,
        int ItensAbaixoMinimo,
        decimal MediaVendasDiaria,
        decimal ReceitaPeriodo,
        DateTime? UltimaMovimentacao,
        decimal HealthScore,
        string HealthClassificacao,
        decimal DimStockHealth,
        decimal DimSalesVelocity,
        decimal DimExpiryRisk,
        decimal DimIdleRisk,
        decimal DimReplenishmentUrgency);

    public sealed record LojaComparacao(
        Guid LojaId,
        string NomeLoja,
        decimal HealthScore,
        string HealthClassificacao,
        decimal ReceitaPeriodo,
        int TotalSkus,
        int QuantidadeEstoque,
        decimal ValorEstoque,
        int AlertasTotal,
        int AlertasCriticos,
        int AlertasVencimento,
        int ItensParados,
        int ItensAbaixoMinimo,
        decimal MediaVendasDiaria);

    public sealed record ProdutoTurnover(
        Guid ProdutoId,
        string NomeProduto,
        int QuantidadeVendida,
        decimal ReceitaGerada,
        decimal TaxaSaidaDiaria);

    public sealed record IndicadorAcao(
        string Tipo,
        string Severidade,
        string Titulo,
        string Descricao,
        Guid? LojaId,
        string? NomeLoja,
        Guid? ReferenciaId);

    /// <summary>
    /// Resumo do dia em curso: vendas (pedidos entregues), pedidos pendentes,
    /// status do caixa e Pix recebidos. Usado pelo dashboard primario do lojista
    /// para responder "como vai o negocio HOJE".
    /// </summary>
    public sealed record ResumoDia(
        int PedidosEntreguesHoje,
        decimal FaturamentoHoje,
        decimal TicketMedioHoje,
        int PedidosPendentes,
        decimal ValorPedidosPendentes,
        bool CaixaAbertaHoje,
        bool CaixaFechadaHoje,
        decimal SaldoCaixaAtual,
        int PixRecebidosHoje,
        decimal ValorPixHoje,
        bool OnboardingCompleto);

    // ── Receita x Custo DTOs ────────────────────────────────────────────

    public sealed record ReceitaCustoDia(
        string Label,
        decimal Receita,
        decimal Custo,
        decimal Lucro);

    // ── Dashboard Full DTOs ─────────────────────────────────────────────

    public sealed record DashboardKpis(
        decimal Receita,
        decimal TicketMedio,
        int Pedidos,
        int PedidosEntregues,
        int PedidosPendentes,
        int ItensEmEstoque,
        decimal CustoEstoque,
        decimal? MargemBruta,
        int LotesProduzidos,
        int ClientesAtivos,
        decimal PercentualCritico,
        int LotesAtivos);

    public sealed record DashboardKpisDelta(
        decimal? Receita,
        decimal? TicketMedio,
        decimal? Pedidos,
        decimal? ItensEmEstoque,
        decimal? CustoEstoque,
        decimal? MargemBruta,
        decimal? LotesProduzidos,
        decimal? ClientesAtivos);

    public sealed record EstoqueStatusDistribuicao(
        int Ok,
        int Atencao,
        int Critico,
        int Parado,
        int Total);

    public sealed record PedidoPendenteResumo(
        Guid Id,
        string? ClienteNome,
        string ItensResumo,
        decimal Total,
        decimal TotalPago,
        decimal EmAberto,
        DateTime CriadoEm,
        string Status);

    public sealed record AlertaEstoqueResumo(
        Guid ItemEstoqueId,
        Guid ProdutoId,
        string? NomeProduto,
        string Tipo,
        int Quantidade,
        int Dias);

    public sealed record InsightDto(
        string Componente,
        string Severidade,
        string Texto);

    public sealed record DashboardFullResult(
        DashboardKpis Kpis,
        DashboardKpisDelta Delta,
        EstoqueStatusDistribuicao EstoqueStatus,
        IReadOnlyList<PedidoPendenteResumo> PedidosPendentes,
        decimal PedidosPendentesTotal,
        IReadOnlyList<AlertaEstoqueResumo> AlertasEstoque,
        int EntreguesSemVenda,
        IReadOnlyList<InsightDto> Insights);

    // ── Dashboard Extras DTOs ──────────────────────────────────────────────────

    public sealed record FluxoCaixaDia(
        string Label,
        decimal Entradas,
        decimal Saidas,
        decimal SaldoAcumulado);

    public sealed record ValidadeSemanaItem(
        string Semana,
        int Quantidade,
        string[] NomesProdutos,
        int DiasMedia);

    public sealed record TopProdutoDashboard(
        Guid ProdutoId,
        string Nome,
        int Quantidade,
        decimal Receita);

    public sealed record TopClienteDashboard(
        Guid ClienteId,
        string Nome,
        decimal TotalPago,
        int Pedidos,
        decimal TicketMedio);

    public sealed record ProducaoPorOperador(
        string Operador,
        int Lotes,
        int Unidades);

    public sealed record EntradasSaidasSemana(
        string Label,
        decimal Entradas,
        decimal Saidas);

    public sealed record FornecedorResumoItem(
        Guid Id,
        string Nome,
        bool Ativo);

    public sealed record FornecedoresResumo(
        int Ativos,
        int Inativos,
        IReadOnlyList<FornecedorResumoItem> Lista);

    public sealed record NovosClientesMes(
        string Label,
        int Novos);

    public sealed record DashboardExtrasResult(
        IReadOnlyList<FluxoCaixaDia> FluxoCaixa,
        IReadOnlyList<ValidadeSemanaItem> ValidadeTimeline,
        IReadOnlyList<TopProdutoDashboard> TopProdutos,
        IReadOnlyList<TopClienteDashboard> TopClientes,
        IReadOnlyList<ProducaoPorOperador> ProducaoPorOperador,
        IReadOnlyList<EntradasSaidasSemana> EntradasSaidasSemanal,
        FornecedoresResumo Fornecedores,
        IReadOnlyList<NovosClientesMes> NovosClientes);

    // Interface

    public interface IAnalyticsRepository
    {
        /// <summary>Resumo geral do dashboard.</summary>
        Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null);

        /// <summary>Resumo do dia em curso: vendas, pedidos pendentes, caixa e Pix.</summary>
        Task<ResumoDia> GetResumoDiaAsync(Guid empresaId, Guid? lojaId = null);

        /// <summary>Receita agrupada por mês nos últimos N meses.</summary>
        Task<IReadOnlyList<ReceitaPorPeriodo>> GetReceitaPorPeriodoAsync(Guid empresaId, int meses = 12, Guid? lojaId = null);

        /// <summary>Margem por produto nos últimos N dias.</summary>
        Task<IReadOnlyList<MargemPorProduto>> GetMargemPorProdutoAsync(Guid empresaId, int dias = 30, int page = 1, int pageSize = 20, Guid? lojaId = null);

        /// <summary>Resumo de movimentações agrupado por dia/mês.</summary>
        Task<IReadOnlyList<MovimentacaoResumo>> GetMovimentacoesResumoAsync(
            Guid empresaId,
            DateTime de,
            DateTime ate,
            TipoMovimentacaoEstoque? tipo = null,
            Guid? lojaId = null);

        /// <summary>Itens com vencimento nos próximos N dias.</summary>
        Task<(IReadOnlyList<ValidadeAlerta> Items, int TotalCount)> GetAlertasValidadeAsync(
            Guid empresaId,
            int dias = 30,
            int page = 1,
            int pageSize = 20,
            Guid? lojaId = null);

        /// <summary>Itens sem movimentação há mais de N dias.</summary>
        Task<(IReadOnlyList<ItemParadoDetalhe> Items, int TotalCount)> GetItensParadosDetalhadosAsync(
            Guid empresaId,
            int diasSemMovimento = 90,
            int page = 1,
            int pageSize = 20,
            Guid? lojaId = null);

        /// <summary>Sazonalidade mensal de saídas para um produto.</summary>
        Task<IReadOnlyList<SazonalidadeMensal>> GetSazonalidadeAsync(Guid empresaId, Guid produtoId, int meses = 12, Guid? lojaId = null);

        /// <summary>Sugestão de reposição para itens abaixo do mínimo.</summary>
        Task<(IReadOnlyList<ReposicaoSugerida> Items, int TotalCount)> GetSugestaoReposicaoDetalhadaAsync(
            Guid empresaId,
            int diasHistorico = 30,
            int page = 1,
            int pageSize = 20,
            Guid? lojaId = null);

        /// <summary>Projeção de ruptura de estoque com base na taxa de saída diária.</summary>
        Task<(IReadOnlyList<ProjecaoRuptura> Items, int TotalCount)> GetProjecaoRupturaAsync(
            Guid empresaId,
            int diasHistorico = 30,
            int page = 1,
            int pageSize = 20,
            Guid? lojaId = null);

        /// <summary>Vendas agrupadas por canal de venda.</summary>
        Task<IReadOnlyList<VendaPorCanal>> GetVendasPorCanalAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null);

        // ── Receita × Custo ─────────────────────────────────────────────

        /// <summary>Série temporal de Receita/Custo/Lucro: por dia (≤30d) ou por mês (>30d).</summary>
        Task<IReadOnlyList<ReceitaCustoDia>> GetReceitaCustoSerieAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null, int timezoneOffsetMinutes = 0);

        // ── Alertas ──────────────────────────────────────────────────────

        /// <summary>Itens com status Crítico ou abaixo do mínimo (top N).</summary>
        Task<IReadOnlyList<AlertaEstoqueResumo>> GetItensCriticosResumoAsync(Guid empresaId, int top = 20, Guid? lojaId = null);

        // ── Dashboard Full ───────────────────────────────────────────────

        /// <summary>Distribuição dos itens de estoque por status.</summary>
        Task<EstoqueStatusDistribuicao> GetEstoqueStatusDistribuicaoAsync(Guid empresaId, Guid? lojaId = null);

        /// <summary>Pedidos com pagamento pendente (Total > TotalPago, não cancelados).</summary>
        Task<IReadOnlyList<PedidoPendenteResumo>> GetPedidosPendentesAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null, int pageSize = 50);

        /// <summary>Contagem de pedidos entregues sem Venda associada.</summary>
        Task<int> GetEntreguesSemVendaCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null);

        /// <summary>KPIs completos do dashboard com dados de Vendas como fonte de receita.</summary>
        Task<DashboardKpis> GetDashboardKpisAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null);

        /// <summary>Contagem de lotes finalizados no período.</summary>
        Task<int> GetLotesFinalizadosCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null);

        /// <summary>Clientes distintos com pedido no período.</summary>
        Task<int> GetClientesAtivosCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null);

        // ── Store Intelligence ───────────────────────────────────────────

        /// <summary>Comparativo de inteligência entre todas as lojas da empresa.</summary>
        Task<IReadOnlyList<LojaComparacao>> GetComparacaoLojasAsync(Guid empresaId, int periodoDias = 30);

        /// <summary>Resumo de inteligência operacional de uma loja específica.</summary>
        Task<LojaResumoInteligencia?> GetResumoInteligenciaLojaAsync(Guid empresaId, Guid lojaId, int periodoDias = 30);

        /// <summary>Ranking de produtos por giro (turnover) em uma loja.</summary>
        Task<IReadOnlyList<ProdutoTurnover>> GetTopProdutosPorLojaAsync(Guid empresaId, Guid lojaId, int periodoDias = 30, int top = 10, bool ascending = false);

        /// <summary>Indicadores acionáveis por loja ou para toda a empresa.</summary>
        Task<IReadOnlyList<IndicadorAcao>> GetIndicadoresAcaoAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null);

        // ── Dashboard Extras ─────────────────────────────────────────────────────

        /// <summary>Fluxo de caixa agrupado por dia (≤30d) ou mês (>30d) a partir de FechamentosCaixa.</summary>
        Task<IReadOnlyList<FluxoCaixaDia>> GetFluxoCaixaAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null);

        /// <summary>Timeline de validades: itens agrupados por semana futura (próximas 4 semanas).</summary>
        Task<IReadOnlyList<ValidadeSemanaItem>> GetValidadeTimelineAsync(Guid empresaId, Guid? lojaId = null);

        /// <summary>Top N produtos mais vendidos no período (via MovimentacoesEstoque Saida/Venda).</summary>
        Task<IReadOnlyList<TopProdutoDashboard>> GetTopProdutosAsync(Guid empresaId, DateTime de, DateTime ate, int top = 5, Guid? lojaId = null);

        /// <summary>Top N clientes por valor pago no período (via Pedidos).</summary>
        Task<IReadOnlyList<TopClienteDashboard>> GetTopClientesAsync(Guid empresaId, DateTime de, DateTime ate, int top = 5, Guid? lojaId = null);

        /// <summary>Produção por operador: lotes finalizados + unidades (etiquetas) no período.</summary>
        Task<IReadOnlyList<ProducaoPorOperador>> GetProducaoPorOperadorAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null);

        /// <summary>Entradas vs saídas agrupadas por semana no período.</summary>
        Task<IReadOnlyList<EntradasSaidasSemana>> GetEntradasSaidasSemanalAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null);

        /// <summary>Resumo de fornecedores: contagem ativo/inativo + lista.</summary>
        Task<FornecedoresResumo> GetFornecedoresResumoAsync(Guid empresaId, Guid? lojaId = null);

        /// <summary>Novos clientes por mês nos últimos N meses.</summary>
        Task<IReadOnlyList<NovosClientesMes>> GetNovosClientesPorMesAsync(Guid empresaId, int meses = 6, Guid? lojaId = null);
    }
}
