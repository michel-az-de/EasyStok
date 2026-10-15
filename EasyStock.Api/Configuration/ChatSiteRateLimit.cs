using EasyStock.Application.UseCases.Atendimento.ChatSite;

namespace EasyStock.Api.Configuration;

/// <summary>Políticas de rate limit do chat do site (S36).</summary>
public static class ChatSiteRateLimit
{
    public const string AbrirSessao = "chat-site-sessao";
    public const string Mensagem = "chat-site-mensagem";
    public const string Leitura = "chat-site-leitura";
    public const string Identificacao = "chat-site-identificacao";

    /// <summary>
    /// Conexões de stream abertas ao mesmo tempo por sessão (#1725). O widget mantém uma; a folga cobre a
    /// reconexão e uma segunda aba. Sem o teto, 40 streams com o mesmo token custaram 1.168 consultas em 5 s.
    /// </summary>
    public const string Stream = "chat-site-stream";
    public const int StreamsPorSessao = 3;
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