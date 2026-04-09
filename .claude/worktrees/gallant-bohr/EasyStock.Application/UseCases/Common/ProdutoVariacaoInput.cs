namespace EasyStock.Application.UseCases.Common
{
    public sealed record ProdutoVariacaoInput(
        string Nome,
        string? Cor,
        string? Tamanho,
        string? DescricaoComercial,
        string? Sku,
        string? CodigoBarras,
        string? AtributosJson,
        DimensoesInput? DimensoesPadrao,
        bool Ativa = true);
}
