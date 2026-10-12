using System.Net.Sockets;
using EasyStock.Application.Ports.Output.Notifications;
using Polly.Timeout;

namespace EasyStock.Infra.Notifications;

/// <summary>
/// Classificação das falhas de envio pelo protocolo (N2). Uma regra só, para o canal de e-mail, o push e os
/// providers de WhatsApp e SMS não divergirem:
/// <list type="bullet">
/// <item>HTTP 4xx, exceto 408 e 429, e SMTP 5xx são permanentes: nunca vão passar, e repetir só gasta tentativa.</item>
/// <item>HTTP 5xx, 408, 429, rede e SMTP 4xx são transitórios: o outbox tenta de novo, com backoff.</item>
/// <item>WhatsApp e SMS saem no máximo uma vez: 5xx, timeout e queda de conexão deixam o envio
/// <see cref="DesfechoEnvio.Indeterminado"/>, porque o provider pode ter aceitado a mensagem e repetir duplicaria.</item>
/// </list>
/// Quem repete é só o outbox (backoff de 1, 5 e 30 min, uma linha de log por tentativa): nenhum canal ou provider
/// tenta de novo por conta própria.
/// </summary>
internal static class ClassificadorDeFalha
{
    /// <summary>HTTP 4xx, exceto 408 (timeout do pedido) e 429 (limite de taxa), nunca vai passar.</summary>
    public static bool HttpEhPermanente(int status) => status is >= 400 and < 500 and not (408 or 429);

    /// <summary>
    /// Resposta HTTP de falha de um provider que envia no máximo uma vez (Twilio): 4xx é recusa permanente, 408 e
    /// 429 são transitórios (nada foi aceito) e o resto, 5xx na frente, é <see cref="DesfechoEnvio.Indeterminado"/>.
    /// </summary>
    public static ResultadoEnvio DeRespostaHttpDeEnvioUnico(string provider, int status, long duracaoMs)
    {
        var erro = $"{provider} respondeu HTTP {status}";
        if (HttpEhPermanente(status))
            return new ResultadoEnvio(false, provider, erro, status, DuracaoMs: duracaoMs, FalhaPermanente: true);

        return status is 408 or 429
            ? new ResultadoEnvio(false, provider, erro, status, DuracaoMs: duracaoMs)
            : ResultadoEnvio.Indeterminado(provider, erro, status, duracaoMs);
    }

    /// <summary>
    /// Exceção ao chamar um provider que envia no máximo uma vez, sem resposta HTTP: timeout e queda de conexão deixam
    /// o envio <see cref="DesfechoEnvio.Indeterminado"/>; qualquer outra falha, que acontece antes de a mensagem sair
    /// (URL inválida, configuração), segue transitória. O cancelamento do chamador não passa por aqui: o provider o
    /// deixa subir.
    /// </summary>
    public static ResultadoEnvio DeExcecaoDeEnvioUnico(string provider, Exception ex, long duracaoMs) =>
        EhFalhaDeTransporte(ex)
            ? ResultadoEnvio.Indeterminado(provider, ex.Message, duracaoMs: duracaoMs)
            : new ResultadoEnvio(false, provider, ex.Message, DuracaoMs: duracaoMs);

    /// <summary>
    /// Timeout ou queda de conexão: não se sabe se o provider recebeu a mensagem. Um
    /// <see cref="OperationCanceledException"/> que chega aqui é timeout (do <c>HttpClient</c> ou da Polly), porque
    /// o chamador que cancela é tratado antes. DNS, conexão recusada e falha de TLS não entram: a conexão nem abriu,
    /// nada saiu, e a falha é transitória (mesma regra do <c>WhatsAppCloudClient</c>, #1507).
    /// </summary>
    public static bool EhFalhaDeTransporte(Exception ex) =>
        !ConexaoNemAbriu(ex)
        && ex is HttpRequestException or TimeoutException or IOException or SocketException
            or OperationCanceledException or TimeoutRejectedException;

    private static bool ConexaoNemAbriu(Exception ex) =>
        ex is HttpRequestException
        {
            HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError
                or HttpRequestError.SecureConnectionError
        };
}
