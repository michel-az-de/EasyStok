namespace EasyStock.Web.Services;

/// <summary>
/// Helper que decide se um POST web deve carregar header Idempotency-Key.
/// Caminhos em <see cref="ProtectedPrefixes"/> automaticamente recebem um
/// UUID gerado por requisicao — backend (whitelist em Program.cs) usa o
/// header pra dedup retry. Outras rotas POST seguem sem header.
/// </summary>
internal static class IdempotencyKeyHelper
{
    private static readonly string[] ProtectedPrefixes =
    {
        "itensestoque",
        "vendas",
        "movimentacoes",
        "estoque/estorno",
    };

    public static string? AutoGenerateIfApplicable(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var lower = path.TrimStart('/').ToLowerInvariant();
        foreach (var prefix in ProtectedPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.Ordinal))
                return Guid.NewGuid().ToString("N");
        }
        return null;
    }
}
