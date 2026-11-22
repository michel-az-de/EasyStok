using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Services.Atendimento;

public enum SinalPonteWppConnect
{
    /// <summary>Ponte desconectada ou sem resposta: a tela de integração cuida disso; aqui não se conclui nada.</summary>
    NaoAvaliada = 0,
    Entregando = 1,
    /// <summary>Conectada, enxerga mensagem recebida e não a entregou ao webhook.</summary>
    Muda = 2,
}

/// <summary>
/// #3117: em 21/11/2026 a ponte ficou <c>CONNECTED</c> por cinco horas sem entregar mensagem. O estado não mede a entrega,
/// e o silêncio também não (madrugada sem tráfego é silêncio saudável). Aqui se compara o que a ponte enxerga com o que o
/// webhook recebeu: a última mensagem recebida de cada conversa individual tem de ter registro em <c>webhooks_recebidos</c>,
/// que é gravado na chegada, mesmo quando a mensagem acaba não sendo gravada (duplicata, regra de domínio).
/// <para>
/// Carga na ponte por rodada: o estado, uma listagem de conversas e, só para conversa suspeita, até
/// <see cref="MaximoDeConsultas"/> leituras de mensagens (para saber a data e o tipo, que a listagem não traz).
/// </para>
/// <para>
/// Limite conhecido: se a página do WhatsApp dentro da ponte congelar inteira, a listagem congela junto e nada é
/// detectado. Telefone e texto passam por aqui só para a comparação; nada disso sai em log nem no retorno.
/// </para>
/// </summary>
public sealed class VigiaPonteWppConnect(
    IWppConnectSessaoClient cliente, IWebhookRecebidoRepository recebidos, TimeProvider relogio)
{
    public static readonly TimeSpan Carencia = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Janela = TimeSpan.FromHours(6);
    public const int MaximoDeConversas = 20;
    public const int MaximoDeConsultas = 3;

    /// <summary>Os tipos que a ponte entrega pelo <c>onmessage</c> e o parser do webhook reconhece.</summary>
    private static readonly HashSet<string> TiposEntregues = new(StringComparer.Ordinal)
        { "chat", "ptt", "image", "audio", "video", "document", "sticker", "location" };

    public async Task<SinalPonteWppConnect> AvaliarAsync(CredencialWppConnect sessao, CancellationToken ct = default)
    {
        if (!(await cliente.ObterEstadoAsync(sessao, ct)).Conectado) return SinalPonteWppConnect.NaoAvaliada;

        var agora = relogio.GetUtcNow();
        var ate = agora - Carencia;
        var desde = agora - Janela;
        // Anterior ao início da captura o webhook descarta de propósito: não é perda.
        if (sessao.InicioDaCaptura > desde) desde = sessao.InicioDaCaptura;

        var consultas = 0;
        foreach (var ultima in await cliente.ListarUltimasRecebidasAsync(sessao, MaximoDeConversas, ct))
        {
            if (await recebidos.ObterAsync(ProcessarEventoWhatsAppUseCase.Provedor, sessao.IdExterno(ultima.MensagemId), ct) is not null)
                continue;
            if (++consultas > MaximoDeConsultas) break;

            var mensagem = (await cliente.ListarMensagensRecentesAsync(sessao, ultima.Chat, ct))
                .FirstOrDefault(m => m.Id == ultima.MensagemId);
            if (mensagem is null || mensagem.DeMim || mensagem.Tipo is null || !TiposEntregues.Contains(mensagem.Tipo)) continue;

            var quando = DateTimeOffset.FromUnixTimeSeconds(mensagem.Quando);
            if (quando >= desde && quando <= ate) return SinalPonteWppConnect.Muda;
        }

        return SinalPonteWppConnect.Entregando;
    }
}
