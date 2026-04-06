namespace EasyStock.Application.UseCases.Common
{
    public sealed record ProdutoEmbalagemInput(
        string Nome,
        string? Descricao,
        DimensoesInput? Dimensoes,
        bool Padrao);
}
