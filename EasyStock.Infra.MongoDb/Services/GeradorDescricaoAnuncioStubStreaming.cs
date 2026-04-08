using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Domain.Entities;
using System.Runtime.CompilerServices;

namespace EasyStock.Infra.MongoDb.Services;

internal sealed class GeradorDescricaoAnuncioStubStreaming : IGeradorDescricaoAnuncioStreaming
{
    public async IAsyncEnumerable<string> GerarStreamAsync(
        Produto produto,
        ProdutoVariacao? variacao,
        ItemEstoque? itemEstoque,
        string? instrucoesComplementares = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var descricao = itemEstoque?.DescricaoAnuncio
            ?? produto.SugestaoDescricaoAnuncio
            ?? produto.DescricaoBase
            ?? $"[STUB] Descricao gerada para: {produto.Nome}";

        var palavras = descricao.Split(' ');
        foreach (var palavra in palavras)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return palavra + " ";
            await Task.Delay(30, ct);
        }
    }
}
