namespace EasyStock.Application.Ports.Output.Ai;

public interface IGeradorDescricaoAnuncio
{
    Task<string> GerarAsync(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoesComplementares = null);
}
