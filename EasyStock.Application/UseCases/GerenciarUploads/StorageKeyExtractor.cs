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
}
