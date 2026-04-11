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

    // Interface

    public interface IAnalyticsRepository
    {
        /// <summary>Resumo geral do dashboard.</summary>
        Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null);

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

        // ── Store Intelligence ───────────────────────────────────────────

        /// <summary>Comparativo de inteligência entre todas as lojas da empresa.</summary>
        Task<IReadOnlyList<LojaComparacao>> GetComparacaoLojasAsync(Guid empresaId, int periodoDias = 30);

        /// <summary>Resumo de inteligência operacional de uma loja específica.</summary>
        Task<LojaResumoInteligencia?> GetResumoInteligenciaLojaAsync(Guid empresaId, Guid lojaId, int periodoDias = 30);

        /// <summary>Ranking de produtos por giro (turnover) em uma loja.</summary>
        Task<IReadOnlyList<ProdutoTurnover>> GetTopProdutosPorLojaAsync(Guid empresaId, Guid lojaId, int periodoDias = 30, int top = 10, bool ascending = false);

        /// <summary>Indicadores acionáveis por loja ou para toda a empresa.</summary>
        Task<IReadOnlyList<IndicadorAcao>> GetIndicadoresAcaoAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null);
    }
}
