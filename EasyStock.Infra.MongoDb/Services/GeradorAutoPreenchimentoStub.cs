using EasyStock.Application.Ports.Output.Ai;
using System.Runtime.CompilerServices;

namespace EasyStock.Infra.MongoDb.Services;

internal sealed class GeradorAutoPreenchimentoStub : IGeradorAutoPreenchimento
{
    public async IAsyncEnumerable<string> GerarDescricaoProdutoStreamAsync(
        string nomeProduto,
        string? categoria,
        string? marca,
        string? instrucoes,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield return $"{nomeProduto}. Configure OpenAI:Enabled = true para gerar descrições com IA.";
    }
}
