namespace EasyStock.Application.Seguranca;

/// <summary>
/// O que do corpo de resposta de um provedor pode ir para log, exceção e banco (I4.5, #1698).
/// Endpoint de autenticação devolve token no corpo, então dele não se registra nada; do resto, o texto
/// redigido e cortado.
/// </summary>
public static class CorpoDeProvedor
{
    public const string Omitido = "[corpo omitido: endpoint de autenticação]";

    private static readonly string[] MarcasDeAutenticacao = ["oauth", "/token", "/auth", "/login"];

    public static bool EhDeAutenticacao(string? caminho) =>
        caminho is { Length: > 0 }
        && MarcasDeAutenticacao.Any(m => caminho.Contains(m, StringComparison.OrdinalIgnoreCase));

    public static string ParaRegistro(string? caminho, string? corpo, int limite = 500)
    {
        if (EhDeAutenticacao(caminho)) return Omitido;
        if (string.IsNullOrEmpty(corpo)) return string.Empty;
        // Redige antes de cortar: o corte no meio de um token deixaria a ponta dele sem casar com o padrão.
        var redigido = SensitivePatterns.Redact(corpo);
        return redigido.Length > limite ? redigido[..limite] : redigido;
    }
}