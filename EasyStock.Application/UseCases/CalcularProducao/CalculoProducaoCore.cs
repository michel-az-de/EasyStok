using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.CalcularProducao;

/// <summary>
/// Algoritmo puro de calculo de producao 1 nivel. Usado pelo
/// <see cref="CalcularProducaoUseCase"/> (single product) e pelo
/// <see cref="CalcularCestaProducaoUseCase"/> (cesta in-context).
/// Sem dependencia de repositorio: caller resolve produto + composicoes + saldos
/// e passa tudo materializado. Onda 2 estendera para suportar recursao BOM.
/// </summary>
internal static class CalculoProducaoCore
{
    public const decimal PrecisaoMinima = 0.0001m;

    /// <summary>
    /// Calcula 1 produto-final: aplica fator, soma saldos por insumo, marca faltas e estima custo.
    /// </summary>
    /// <param name="produto">Produto-final (precisa de RendimentoBase, RendimentoUnidade).</param>
    /// <param name="qtdDesejada">Quantidade alvo de producao.</param>
    /// <param name="unidadeDesejada">Unidade da quantidade desejada.</param>
    /// <param name="composicoes">Linhas da receita do produto-final (com Insumo carregado).</param>
    /// <param name="saldosPorInsumo">Saldos de estoque de cada InsumoId. Dicionario vindo de IItemEstoqueRepository.GetByProdutosAsync.</param>
    /// <exception cref="UseCaseValidationException">INVALID_RENDIMENTO, UNIT_INCOMPATIBLE.</exception>
    public static CalcularProducaoResult Calcular(
        Produto produto,
        decimal qtdDesejada,
        UnidadeMedida unidadeDesejada,
        IReadOnlyCollection<ProdutoComposicao> composicoes,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<ItemEstoque>> saldosPorInsumo)
    {
        if (produto.RendimentoBase <= 0)
            throw new UseCaseValidationException("INVALID_RENDIMENTO", "Rendimento do produto deve ser maior que zero.");

        var (qtdNoRendimento, erroConversaoRendimento) = UnidadeMedidaConverter.Converter(
            qtdDesejada, unidadeDesejada, produto.RendimentoUnidade);

        if (qtdNoRendimento == null)
            throw new UseCaseValidationException(
                "UNIT_INCOMPATIBLE",
                $"Unidade desejada ({unidadeDesejada}) incompativel com rendimento do produto ({produto.RendimentoUnidade}): {erroConversaoRendimento}");

        var fator = qtdNoRendimento.Value / produto.RendimentoBase;

        var linhas = new List<CalcularInsumoResult>(composicoes.Count);
        decimal custoTotal = 0m;
        var algumCustoConhecido = false;
        var tudoDisponivel = true;

        foreach (var comp in composicoes)
        {
            var qtdNecessaria = comp.Quantidade * fator;
            var insumo = comp.Insumo!;

            decimal saldoNaUnidadeBase = 0m;
            IReadOnlyCollection<ItemEstoque>? lotes = null;
            if (saldosPorInsumo.TryGetValue(comp.InsumoId, out lotes))
                saldoNaUnidadeBase = lotes.Sum(l => l.QuantidadeAtual.Value);

            var (saldoConvertido, erroConversao) = UnidadeMedidaConverter.Converter(
                saldoNaUnidadeBase, insumo.UnidadeMedidaBase, comp.Unidade);

            var conversaoFalhou = saldoConvertido == null;
            var saldoFinal = saldoConvertido ?? 0m;
            decimal? falta = null;

            if (conversaoFalhou)
            {
                falta = qtdNecessaria;
                tudoDisponivel = false;
            }
            else if (saldoFinal < qtdNecessaria)
            {
                falta = qtdNecessaria - saldoFinal;
                tudoDisponivel = false;
            }

            decimal? custoUnit = null;
            if (lotes is { Count: > 0 })
                custoUnit = lotes.First().CustoUnitario.Valor;
            else if (insumo.CustoReferencia != null)
                custoUnit = insumo.CustoReferencia.Valor;

            decimal? custoLinha = custoUnit.HasValue ? custoUnit.Value * qtdNecessaria : null;
            if (custoLinha.HasValue)
            {
                custoTotal += custoLinha.Value;
                algumCustoConhecido = true;
            }

            linhas.Add(new CalcularInsumoResult(
                InsumoId: comp.InsumoId,
                InsumoNome: insumo.Nome,
                QuantidadeNecessaria: qtdNecessaria,
                UnidadeReceita: comp.Unidade,
                SaldoAtual: saldoNaUnidadeBase,
                UnidadeSaldo: insumo.UnidadeMedidaBase,
                Falta: falta,
                CustoUnitarioReferencia: custoUnit,
                CustoEstimadoLinha: custoLinha,
                ConversaoFalhou: conversaoFalhou,
                Aviso: conversaoFalhou ? erroConversao : null));
        }

        return new CalcularProducaoResult(
            ProdutoFinalId: produto.Id,
            ProdutoFinalNome: produto.Nome,
            QuantidadeDesejada: qtdDesejada,
            UnidadeDesejada: unidadeDesejada,
            FatorMultiplicador: fator,
            Insumos: linhas,
            TudoDisponivel: tudoDisponivel,
            CustoEstimadoTotal: algumCustoConhecido ? custoTotal : null);
    }
}
