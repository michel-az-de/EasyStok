namespace EasyStock.Application.Ports.Output.Ai;

/// <summary>
/// Gera sugestão de descrição para um produto a partir apenas do nome/contexto,
/// sem necessitar de um ProdutoId persistido — ideal para o formulário de cadastro.
/// </summary>
public interface IGeradorAutoPreenchimento
{
    IAsyncEnumerable<string> GerarDescricaoProdutoStreamAsync(
        string nomeProduto,
        string? categoria,
        string? marca,
        string? instrucoes,
        CancellationToken ct = default);
}
