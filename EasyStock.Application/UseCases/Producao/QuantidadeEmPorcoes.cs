using System.Globalization;
using EasyStock.Application.Ports.Output.Persistence.Storefront;

namespace EasyStock.Application.UseCases.Producao;

/// <summary>
/// #1776 (itens 1 e 5): o saldo de prato são porções. Contagem e perda de prato aceitam só porção inteira e no
/// máximo <see cref="Teto"/> por lançamento (o mesmo teto de porções do lote). Prato é o produto em
/// <see cref="UnidadeMedida.Un"/>, fora de insumo e embalagem, que tem receita ou está ligado a item do cardápio.
/// Item de revenda contado em unidade fica de fora. Vale só para a entrada nova: saldo já gravado não muda.
/// </summary>
internal static class QuantidadeEmPorcoes
{
    public const int Teto = RegistrarProducaoUseCase.TetoPorcoesPorLote;

    /// <param name="oQue">Início da frase: "A contagem", "A perda".</param>
    public static async Task ValidarAsync(
        Guid empresaId, Produto? produto, decimal quantidade, string oQue,
        IProdutoComposicaoRepository? receitas, ICardapioItemRepository? cardapio, CancellationToken ct = default)
    {
        if (produto is null || produto.UnidadeMedidaBase != UnidadeMedida.Un || produto.EhInsumo || produto.EhEmbalagem)
            return;
        var fracao = quantidade != decimal.Truncate(quantidade);
        if (!fracao && quantidade <= Teto) return; // só consulta receita e cardápio quando a entrada seria recusada
        if (!await EhPratoAsync(empresaId, produto.Id, receitas, cardapio, ct)) return;

        if (fracao)
            throw new UseCaseValidationException(
                $"{oQue} de \"{produto.Nome}\" é em porções: informe um número inteiro.");
        throw new UseCaseValidationException(
            $"{oQue} passa do limite de {Teto.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} porções por lançamento: confira o número.");
    }

    private static async Task<bool> EhPratoAsync(
        Guid empresaId, Guid produtoId, IProdutoComposicaoRepository? receitas, ICardapioItemRepository? cardapio,
        CancellationToken ct)
    {
        if (receitas is not null && (await receitas.GetByProdutoFinalAsync(empresaId, produtoId, null, ct)).Count > 0)
            return true;
        return cardapio is not null && await cardapio.ProdutoEstaNoCardapioAsync(empresaId, produtoId, ct);
    }
}
