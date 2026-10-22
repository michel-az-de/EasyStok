using System.Threading.RateLimiting;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Configuration;

/// <summary>
/// Teto por IP das rotas públicas do chat do site e da comanda (#1622-1, #1643-2, #1725-1, #1725-2). As políticas de cada
/// rota particionam pela sessão ou pelo token, para um visitante não gastar a cota do outro atrás do mesmo NAT; sozinhas,
/// cediam a token inventado (partição nova a cada requisição). Este limitador global, encadeado
/// (<see cref="PartitionedRateLimiter.CreateChained{TResource}"/>), soma o teto por IP pela política da rota: janela por
/// minuto nas escritas e leituras, concorrência nos streams. Rota sem política daqui passa livre.
/// </summary>
public static class LimitePorIpPublico
{
    private static readonly IReadOnlyDictionary<string, int> PorMinuto = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [ChatSiteRateLimit.Mensagem] = ChatSiteRateLimit.MensagensPorIpPorMinuto,
        [ChatSiteRateLimit.Identificacao] = ChatSiteRateLimit.IdentificacoesPorIpPorMinuto,
        [ComandaRateLimit.Envio] = ChatSiteRateLimit.ComandaEnviosPorIpPorMinuto,
        [ComandaRateLimit.Rascunho] = ChatSiteRateLimit.ComandaRascunhosPorIpPorMinuto,
        [ComandaRateLimit.Leitura] = ChatSiteRateLimit.ComandaLeiturasPorIpPorMinuto,
    };

    private static readonly IReadOnlySet<string> Streams = new HashSet<string>(StringComparer.Ordinal)
    {
        ChatSiteRateLimit.Stream, ComandaRateLimit.Stream,
    };

    public static PartitionedRateLimiter<HttpContext> Criar() => PartitionedRateLimiter.CreateChained(
        PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
            Politica(contexto) is { } politica && PorMinuto.TryGetValue(politica, out var limite)
                ? RateLimitPartition.GetFixedWindowLimiter($"{politica}|{Ip(contexto)}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limite, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter(string.Empty)),
        PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
            Politica(contexto) is { } politica && Streams.Contains(politica)
                ? RateLimitPartition.GetConcurrencyLimiter($"{politica}|{Ip(contexto)}", _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = ChatSiteRateLimit.StreamsPorIp, QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter(string.Empty)));

    private static string? Politica(HttpContext contexto) =>
        contexto.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

    private static string Ip(HttpContext contexto) => contexto.Connection.RemoteIpAddress?.ToString() ?? "anon";
}

/// <summary>Políticas da comanda pública (cardápio da conversa) e a partição pelo token.</summary>
public static class ComandaRateLimit
{
    public const string Leitura = "comanda-leitura";
    public const string Rascunho = "comanda-rascunho";
    public const string Envio = "comanda-envio";
    public const string Stream = "comanda-stream";

    /// <summary>
    /// #1725-1: partição pelo hash do token só quando ele tem o formato gerado (<see cref="AcessoChatSite.NovoToken"/>);
    /// o resto cai no IP. O token cru nunca vira chave (fica em memória e chega a log de diagnóstico).
    /// </summary>
    public static string Chave(HttpContext contexto, string? token)
    {
        var limpo = token?.Trim();
        return FormatoGerado(limpo)
            ? "token:" + AcessoChatSite.HashDoToken(limpo!)
            : "ip:" + (contexto.Connection.RemoteIpAddress?.ToString() ?? "anon");
    }

    public static bool FormatoGerado(string? token) =>
        token is { Length: 43 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
