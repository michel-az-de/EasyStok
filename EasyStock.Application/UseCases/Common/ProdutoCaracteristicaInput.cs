namespace EasyStock.Application.UseCases.Common
{
    public sealed record ProdutoCaracteristicaInput(
        string Nome,
        string? Descricao,
        int? QuantidadeReferencia,
        string? VariacaoPadrao,
        int OrdemExibicao,
        Guid? VariacaoId = null);
}
