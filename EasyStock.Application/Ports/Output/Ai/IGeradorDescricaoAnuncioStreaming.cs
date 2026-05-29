namespace EasyStock.Application.Ports.Output.Ai
{
    public interface IGeradorDescricaoAnuncioStreaming
    {
        IAsyncEnumerable<string> GerarStreamAsync(
            Produto produto,
            ProdutoVariacao? variacao,
            ItemEstoque? itemEstoque,
            string? instrucoesComplementares = null,
            CancellationToken ct = default);
    }
}
