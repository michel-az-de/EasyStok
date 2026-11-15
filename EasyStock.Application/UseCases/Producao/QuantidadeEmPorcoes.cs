using System.Globalization;

namespace EasyStock.Application.UseCases.Producao;

/// <summary>
/// #1776 (itens 1 e 5): o prato ligado ao estoque é contado em <see cref="UnidadeMedida.Un"/> e não é insumo nem
/// embalagem; o saldo dele são porções. Contagem e perda de prato aceitam só porção inteira e no máximo
/// <see cref="Teto"/> por lançamento (o mesmo teto de porções do lote). Insumo e embalagem seguem fracionados.
/// Vale só para a entrada nova: o saldo já gravado com fração continua sendo lido como está.
/// </summary>
internal static class QuantidadeEmPorcoes
{
    public const int Teto = RegistrarProducaoUseCase.TetoPorcoesPorLote;

    public static bool ContaEmPorcoes(Produto produto) =>
        produto.UnidadeMedidaBase == UnidadeMedida.Un && !produto.EhInsumo && !produto.EhEmbalagem;

    /// <param name="oQue">Início da frase: "A contagem", "A perda".</param>
    public static void Validar(Produto? produto, decimal quantidade, string oQue)
    {
        if (produto is null || !ContaEmPorcoes(produto)) return;
        if (quantidade != decimal.Truncate(quantidade))
            throw new UseCaseValidationException(
                $"{oQue} de \"{produto.Nome}\" é em porções: informe um número inteiro.");
        if (quantidade > Teto)
            throw new UseCaseValidationException(
                $"{oQue} passa do limite de {Teto.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} porções por lançamento: confira o número.");
    }
}
