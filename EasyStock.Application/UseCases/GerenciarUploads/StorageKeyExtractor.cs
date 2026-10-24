namespace EasyStock.Application.UseCases.GerenciarUploads;

/// <summary>
/// Helper puro para converter uma URL (absoluta ou relativa) na chave de
/// armazenamento (<c>storage key</c>) usada pelos provedores de arquivos
/// (S3, disco local via <c>/files/</c>, etc.). Extraído para facilitar
/// testes unitários sem depender do <see cref="GerenciarUploadsUseCase"/>.
/// </summary>
public static class StorageKeyExtractor
{
    /// <summary>
    /// Converte a URL pública/relativa para a chave de armazenamento.
    /// </summary>
    /// <returns>
    /// A chave extraída, ou <c>null</c> se a URL for nula, vazia ou
    /// não reconhecida.
    /// </returns>
    public static string? Extract(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        // URL relativa local: "/files/caminho/arquivo.jpg" -> "caminho/arquivo.jpg"
        const string filesPrefix = "/files/";
        var idx = url.IndexOf(filesPrefix, System.StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
            return url[(idx + filesPrefix.Length)..];

        // URL absoluta: extrai PathAndQuery sem o primeiro segmento (bucket/container).
        if (System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.TrimStart('/');
            var firstSlash = path.IndexOf('/');
            if (firstSlash > 0 && firstSlash < path.Length - 1)
                return path[(firstSlash + 1)..];
            return path;
        }

        return null;
    }

    /// <summary>
    /// #2027: a chave da URL só sai se for do dono, ou seja, começar por <paramref name="prefixoDoDono"/> e não
    /// tiver <c>..</c>. <c>FotoUrl</c>, logo e avatar são texto gravado pelo tenant e podem apontar para o arquivo
    /// de outra empresa; apagar ou ler por eles sem conferir mexia no storage alheio.
    /// </summary>
    public static string? ExtractDoDono(string? url, string prefixoDoDono)
    {
        var chave = Extract(url);
        if (chave is null || chave.Contains("..", System.StringComparison.Ordinal) || chave.Contains('\\')) return null;
        return chave.StartsWith(prefixoDoDono, System.StringComparison.Ordinal) ? chave : null;
    }

    /// <summary>Foto deste prato: <c>cardapios/{empresa}/{vitrine}/{prato}/</c>, onde o upload e o acervo gravam.</summary>
    public static string? ExtractDaFotoDoPrato(string? url, System.Guid storefrontId, System.Guid itemId) =>
        ExtractDaVitrine(url, storefrontId) is { } chave
        && chave.Split('/') is [_, _, _, var prato, _, ..] && prato == itemId.ToString()
            ? chave
            : null;

    /// <summary>Foto de algum prato desta vitrine: <c>cardapios/{empresa}/{vitrine}/...</c>.</summary>
    public static string? ExtractDaVitrine(string? url, System.Guid storefrontId) =>
        ExtractDoDono(url, "cardapios/") is { } chave
        && chave.Split('/') is [_, _, var vitrine, ..] && vitrine == storefrontId.ToString()
            ? chave
            : null;
}
