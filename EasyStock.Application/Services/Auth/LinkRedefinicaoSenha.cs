using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Auth;

/// <summary>
/// O link do e-mail de redefinição (N8). A base nunca vem do corpo da requisição (#765): vem de
/// <c>Auth:LinkRedefinirSenha</c>, um modelo com <c>{0}</c> no lugar do token (hoje a origem do Web mais
/// <c>/auth/redefinir-senha?token={0}</c>; quando o console tiver a tela, <c>{origem}/#/redefinir?t={0}</c>, com o token no
/// fragmento, que não chega ao servidor nem ao log do proxy). Sem o modelo, usa a primeira origem confiável
/// (<c>Auth:TrustedLinkOrigins</c> e, no fallback, <c>Cors:AllowedOrigins</c>, as mesmas do <c>LinkBaseUrlResolver</c>)
/// com o caminho do Web. Sem nenhum dos dois não há link seguro e o chamador não publica.
/// </summary>
public static class LinkRedefinicaoSenha
{
    public const string Chave = "Auth:LinkRedefinirSenha";

    public const string CaminhoDoWeb = "/auth/redefinir-senha?token={0}";

    public static string? Montar(IConfiguration configuration, string token)
    {
        var modelo = configuration[Chave]?.Trim();
        if (string.IsNullOrEmpty(modelo))
        {
            var origem = configuration.GetSection("Auth:TrustedLinkOrigins").GetChildren().Select(c => c.Value)
                .Concat(configuration.GetSection("Cors:AllowedOrigins").GetChildren().Select(c => c.Value))
                .FirstOrDefault(o => !string.IsNullOrWhiteSpace(o) && Uri.TryCreate(o, UriKind.Absolute, out _));
            if (origem is null) return null;
            modelo = origem.Trim().TrimEnd('/') + CaminhoDoWeb;
        }

        if (!modelo.Contains("{0}", StringComparison.Ordinal)) return null;
        return modelo.Replace("{0}", Uri.EscapeDataString(token), StringComparison.Ordinal);
    }
}
