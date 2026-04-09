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

    // Interface

    public interface IAnalyticsRepository
    {
        /// <summary>Resumo geral do dashboard.</summary>
        Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30);

        /// <summary>Receita agrupada por mês nos últimos N meses.</summary>
        Task<IReadOnlyList<ReceitaPorPeriodo>> GetReceitaPorPeriodoAsync(Guid empresaId, int meses = 12);

        /// <summary>Margem por produto nos últimos N dias.</summary>
        Task<IReadOnlyList<MargemPorProduto>> GetMargemPorProdutoAsync(Guid empresaId, int dias = 30, int page = 1, int pageSize = 20);

        /// <summary>Resumo de movimentações agrupado por dia/mês.</summary>
        Task<IReadOnlyList<MovimentacaoResumo>> GetMovimentacoesResumoAsync(
            Guid empresaId,
            DateTime de,
            DateTime ate,
            TipoMovimentacaoEstoque? tipo = null);

        /// <summary>Itens com vencimento nos próximos N dias.</summary>
        Task<(IReadOnlyList<ValidadeAlerta> Items, int TotalCount)> GetAlertasValidadeAsync(
            Guid empresaId,
            int dias = 30,
            int page = 1,
            int pageSize = 20);

        /// <summary>Itens sem movimentação há mais de N dias.</summary>
        Task<(IReadOnlyList<ItemParadoDetalhe> Items, int TotalCount)> GetItensParadosDetalhadosAsync(
            Guid empresaId,
            int diasSemMovimento = 90,
            int page = 1,
            int pageSize = 20);

        /// <summary>Sazonalidade mensal de saídas para um produto.</summary>
        Task<IReadOnlyList<SazonalidadeMensal>> GetSazonalidadeAsync(Guid empresaId, Guid produtoId, int meses = 12);

        /// <summary>Sugestão de reposição para itens abaixo do mínimo.</summary>
        Task<(IReadOnlyList<ReposicaoSugerida> Items, int TotalCount)> GetSugestaoReposicaoDetalhadaAsync(
            Guid empresaId,
            int diasHistorico = 30,
            int page = 1,
            int pageSize = 20);

        /// <summary>Projeção de ruptura de estoque com base na taxa de saída diária.</summary>
        Task<(IReadOnlyList<ProjecaoRuptura> Items, int TotalCount)> GetProjecaoRupturaAsync(
            Guid empresaId,
            int diasHistorico = 30,
            int page = 1,
            int pageSize = 20);

        /// <summary>Vendas agrupadas por canal de venda.</summary>
        Task<IReadOnlyList<VendaPorCanal>> GetVendasPorCanalAsync(Guid empresaId, DateTime de, DateTime ate);
    }
}
