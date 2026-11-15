using EasyStock.Application.UseCases.CalcularProducao;

namespace EasyStock.Application.UseCases.Producao;

/// <summary>
/// #1776 (item 4): custo de uma porção produzida pela receita, na mesma conta da baixa de insumos
/// (<see cref="BaixaDeInsumosDaProducao"/>): custo das linhas × produzido ÷ rendimento, dividido pelas porções.
/// A embalagem da porção (M1.4e) entra uma por porção no lugar da embalagem da receita.
/// Só fecha se toda linha tem custo: parcial pareceria barato demais, como na tela de receitas. Sem custo: null.
/// </summary>
internal static class CustoDaProducao
{
    public static decimal? PorPorcao(PratoParaBaixa p, IReadOnlyCollection<ProdutoComposicao> receita, Produto? embalagemDaPorcao)
    {
        if (p.Porcoes <= 0) return null;
        var linhas = receita.Where(l => embalagemDaPorcao is null || l.Insumo?.EhEmbalagem != true).ToList();
        if (linhas.Count == 0 && embalagemDaPorcao is null) return null;

        var (fator, _) = BaixaDeInsumosDaProducao.FatorDaReceita(p);
        if (fator is null) return null;

        var custoDasLinhas = 0m;
        foreach (var linha in linhas)
        {
            if (linha.Insumo?.CustoDaFicha is not { } custoDoInsumo) return null;
            var custo = CalculoProducaoCore.CustoNaUnidadeDoInsumo(linha.Quantidade, linha.Unidade, linha.Insumo.UnidadeMedidaBase, custoDoInsumo);
            if (custo is null) return null;
            custoDasLinhas += custo.Value;
        }

        var porPorcao = custoDasLinhas * fator.Value / p.Porcoes;
        if (embalagemDaPorcao is not null)
        {
            if (embalagemDaPorcao.CustoDaFicha is not { } custoDaEmbalagem) return null;
            porPorcao += custoDaEmbalagem;
        }
        return Math.Round(porPorcao, 2, MidpointRounding.AwayFromZero);
    }
}
