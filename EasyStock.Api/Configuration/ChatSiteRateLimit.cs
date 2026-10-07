using EasyStock.Application.UseCases.Atendimento.ChatSite;

namespace EasyStock.Api.Configuration;

/// <summary>Políticas de rate limit do chat do site (S36).</summary>
public static class ChatSiteRateLimit
{
    public const string AbrirSessao = "chat-site-sessao";
    public const string Mensagem = "chat-site-mensagem";
    public const string Leitura = "chat-site-leitura";
    public const string Identificacao = "chat-site-identificacao";
    public const string HeaderToken = "X-Chat-Token";
    public const int MensagensPorMinuto = 20;

    /// <summary>O formulário antes do chat (#1430) se manda uma vez; a folga cobre correção de digitação.</summary>
    public const int IdentificacoesPorMinuto = 5;

    /// <summary>Partição por sessão (hash do token, nunca o token cru); sem token, pelo IP.</summary>
    public static string ChaveMensagem(HttpContext context)
    {
        var token = context.Request.Headers[HeaderToken].FirstOrDefault();
        return string.IsNullOrWhiteSpace(token)
            ? "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "anon")
            : "sessao:" + AcessoChatSite.HashDoToken(token.Trim());
    }
}