using Microsoft.Extensions.Configuration;

namespace EasyStock.Application.Services.Auth;

/// <summary>
/// O link do e-mail de convite (N9). Mesma regra do <see cref="LinkRedefinicaoSenha"/>: a base nunca vem do corpo da
/// requisição, vem de <c>Auth:LinkConvite</c>, um modelo com <c>{0}</c> no lugar do token (hoje a origem do Web mais
/// <c>/auth/convite?token={0}</c>; no console, <c>{origem}/#/convite?t={0}</c>, com o token no fragmento). Sem o modelo
/// usa a primeira origem confiável (<c>Auth:TrustedLinkOrigins</c> e, no fallback, <c>Cors:AllowedOrigins</c>). Sem nenhum
/// dos dois não há link seguro e quem chama recusa criar o convite.
/// </summary>
public static class LinkConviteAcesso
{
    public const string Chave = "Auth:LinkConvite";

    public const string CaminhoDoWeb = "/auth/convite?token={0}";

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
