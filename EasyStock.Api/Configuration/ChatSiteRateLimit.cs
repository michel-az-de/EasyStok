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

    // #1622-1, #1643-2, #1725-1, #1725-2: teto por IP encadeado ao da sessão ou do token (LimitePorIpPublico). A folga
    // cobre alguns visitantes atrás do mesmo NAT ou CGNAT; inventar token não passa daqui.
    public const int MensagensPorIpPorMinuto = 60;
    public const int IdentificacoesPorIpPorMinuto = 15;
    public const int ComandaEnviosPorIpPorMinuto = 30;
    public const int ComandaRascunhosPorIpPorMinuto = 240;
    public const int ComandaLeiturasPorIpPorMinuto = 120;
    public const int StreamsPorIp = 10;

    /// <summary>
    /// Partição por sessão (hash do token, nunca o token cru); sem token ou fora do formato gerado, pelo IP. O teto por IP
    /// encadeado (<see cref="LimitePorIpPublico"/>) segura quem inventa token no formato certo.
    /// </summary>
    public static string ChaveMensagem(HttpContext context)
    {
        var token = context.Request.Headers[HeaderToken].FirstOrDefault()?.Trim();
        return AcessoChatSite.TemFormatoDeToken(token)
            ? "sessao:" + AcessoChatSite.HashDoToken(token!)
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "anon");
    }
}