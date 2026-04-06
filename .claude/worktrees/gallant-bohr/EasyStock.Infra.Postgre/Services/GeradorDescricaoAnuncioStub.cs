using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities;

namespace EasyStock.Infra.Postgre.Services
{
    internal sealed class GeradorDescricaoAnuncioStub : IGeradorDescricaoAnuncio
    {
        public Task<string> GerarAsync(Produto produto, ProdutoVariacao? variacao, ItemEstoque? itemEstoque, string? instrucoesComplementares = null)
        {
            var descricao = itemEstoque?.DescricaoAnuncio
                ?? produto.SugestaoDescricaoAnuncio
                ?? produto.DescricaoBase
                ?? produto.Nome;

            return Task.FromResult(descricao);
        }
    }
}
