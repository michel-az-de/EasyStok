using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Services.Atendimento;

public enum SinalPonteWppConnect
{
    /// <summary>
    /// Nada se conclui nesta rodada e o estado do incidente fica como está: ponte desconectada ou sem resposta há pouco,
    /// ou a única suspeita é anterior à primeira observação deste processo (#3138).
    /// </summary>
    NaoAvaliada = 0,
    Entregando = 1,
    /// <summary>Conectada, enxerga mensagem recebida e não a entregou ao webhook.</summary>
    Muda = 2,
    /// <summary>Desconectada ou sem resposta por <see cref="VigiaPonteWppConnect.PrazoDesconectada"/> seguidos: não é ponte muda, sai do escopo do vigia.</summary>
    Desconectada = 3,
}

/// <summary>
/// #3117: em 21/11/2026 a ponte ficou <c>CONNECTED</c> por cinco horas sem entregar mensagem. O estado não mede a entrega,
/// e o silêncio também não (madrugada sem tráfego é silêncio saudável). Aqui se compara o que a ponte enxerga com o que o
/// webhook recebeu: a última mensagem recebida de cada conversa individual tem de ter registro em <c>webhooks_recebidos</c>
/// (marca de entrega, gravada na chegada, ou o registro do processamento).
/// <para>
/// Carga na ponte por rodada: o estado, uma listagem de conversas e, só para conversa suspeita, até
/// <see cref="MaximoDeConsultas"/> leituras de mensagens (para saber a data e o tipo, que a listagem não traz).
/// </para>
/// <para>
/// Uma mensagem sem registro só prova ponte muda se nada foi entregue depois dela e se este processo já via a ponte
/// conectada quando ela chegou (#3138). Entrega posterior mostra um buraco antigo (a API reiniciando num deploy, por
/// exemplo); mensagem anterior a uma reconexão observada (duas leituras desconectadas seguidas: uma só é oscilação do
/// <c>check-connection</c>) foi sincronizada sem <c>onmessage</c>; e, logo depois de um
/// reinício do processo, a suspeita anterior deixa a rodada sem conclusão, para um incidente aberto não fechar sozinho.
/// </para>
/// <para>
/// Limites conhecidos: queda e volta da ponte entre duas rodadas não é observada, e a mensagem sincronizada nesse
/// intervalo conta como muda até a primeira entrega seguinte. A suspeita só é vista enquanto a conversa está entre as
/// <see cref="MaximoDeConversas"/> mais recentes. Ponte que perde parte das mensagens e entrega o resto não é vista: a
/// entrega posterior esconde a perda. As contagens vivem na memória do processo. Se a página do WhatsApp dentro da ponte congelar inteira, a listagem congela junto e nada é
/// detectado. Mensagem que a ponte entregou e a API recusou (400, telefone sem resolver) não é ponte muda: o webhook
/// grava a marca de entrega antes do parser. Telefone e texto passam por aqui só para a comparação; nada disso sai em
/// log nem no retorno.
/// </para>
/// </summary>
public sealed class VigiaPonteWppConnect(
    IWppConnectSessaoClient cliente, IWebhookRecebidoRepository recebidos, MemoriaVigiaPonteWppConnect memoria, TimeProvider relogio,
    ILogger<VigiaPonteWppConnect> logger)
{
    public static readonly TimeSpan Carencia = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Janela = TimeSpan.FromHours(6);
    public static readonly TimeSpan PrazoDesconectada = TimeSpan.FromMinutes(30);
    public const int ObservacoesDesconectada = 6;
    public const int MaximoDeConversas = 20;
    public const int MaximoDeConsultas = 3;

    /// <summary>Os tipos que a ponte entrega pelo <c>onmessage</c> e o parser do webhook reconhece.</summary>
    private static readonly HashSet<string> TiposEntregues = new(StringComparer.Ordinal)
        { "chat", "ptt", "image", "audio", "video", "document", "sticker", "location" };

    private readonly ILogger<VigiaPonteWppConnect> _logger = logger;

    public async Task<SinalPonteWppConnect> AvaliarAsync(CredencialWppConnect sessao, CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow();
        if (!await ConectadaAsync(sessao, ct))
        {
            // #3138: desconectada de vez (QR expirado, contêiner parado) não é ponte muda. Tempo e observações seguidas:
            // só o tempo contaria como contínuo um intervalo em que ninguém olhou.
            var fora = memoria.ObservarDesconectada(sessao.Sessao, agora);
            return agora - fora.Desde >= PrazoDesconectada && fora.Observacoes >= ObservacoesDesconectada
                ? SinalPonteWppConnect.Desconectada
                : SinalPonteWppConnect.NaoAvaliada;
        }

        var conexao = memoria.ObservarConectada(sessao.Sessao, agora);
        var ate = agora - Carencia;
        var desde = agora - Janela;
        // Anterior ao início da captura o webhook descarta de propósito: não é perda.
        if (sessao.InicioDaCaptura > desde) desde = sessao.InicioDaCaptura;

        var consultas = 0;
        var inconclusiva = false;
        DateTime? ultimaEntrega = null;
        var leuUltimaEntrega = false;
        foreach (var ultima in await cliente.ListarUltimasRecebidasAsync(sessao, MaximoDeConversas, ct))
        {
            var idExterno = sessao.IdExterno(ultima.MensagemId);
            if (memoria.JaDescartada(idExterno)) continue;
            // A marca de entrega é gravada na chegada; o registro do processamento cobre o que chegou antes dela existir.
            if (await recebidos.ObterAsync(ProcessarWebhookWppConnectUseCase.ProvedorEntrega, idExterno, ct) is not null
                || await recebidos.ObterAsync(ProcessarEventoWhatsAppUseCase.Provedor, idExterno, ct) is not null) continue;
            if (memoria.AnteriorAoProcesso(idExterno, out var lembrada))
            {
                inconclusiva |= !conexao.AposQueda && lembrada >= desde;
                continue;
            }
            if (++consultas > MaximoDeConsultas) break;

            var mensagem = (await cliente.ListarMensagensRecentesAsync(sessao, ultima.Chat, ct))
                .FirstOrDefault(m => m.Id == ultima.MensagemId);
            if (mensagem is null) continue;

            var quando = DateTimeOffset.FromUnixTimeSeconds(mensagem.Quando);
            // Nunca vai contar: lembra, para não gastar a consulta da próxima rodada com a mesma mensagem.
            if (mensagem.DeMim || mensagem.Tipo is null || !TiposEntregues.Contains(mensagem.Tipo) || quando < desde)
            {
                memoria.Descartar(idExterno);
                continue;
            }

            if (quando > ate) continue;

            // A ponte entregou outra mensagem depois desta: é um buraco antigo, não uma ponte muda.
            if (!leuUltimaEntrega)
            {
                ultimaEntrega = await recebidos.UltimoRecebidoEmAsync(
                    ProcessarWebhookWppConnectUseCase.ProvedorEntrega, sessao.IdExterno(string.Empty), ct);
                leuUltimaEntrega = true;
            }
            if (ultimaEntrega is { } entrega && entrega > quando.UtcDateTime)
            {
                memoria.Descartar(idExterno);
                continue;
            }

            if (quando >= conexao.Desde) return SinalPonteWppConnect.Muda;

            // Anterior a uma reconexão observada: sincronizada sem passar pelo webhook, nunca vai contar.
            if (conexao.AposQueda)
            {
                memoria.Descartar(idExterno);
                continue;
            }

            // Anterior à primeira observação deste processo: não prova ponte muda nem entrega.
            memoria.LembrarAnteriorAoProcesso(idExterno, quando);
            inconclusiva = true;
        }

        return inconclusiva ? SinalPonteWppConnect.NaoAvaliada : SinalPonteWppConnect.Entregando;
    }

    /// <summary>Ponte que não responde conta como desconectada: nos dois casos não há entrega a medir.</summary>
    private async Task<bool> ConectadaAsync(CredencialWppConnect sessao, CancellationToken ct)
    {
        try
        {
            return (await cliente.ObterEstadoAsync(sessao, ct)).Conectado;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Só o tipo: a mensagem da exceção pode trazer endereço ou resposta da ponte.
            _logger.LogWarning("Ponte WPPConnect sem resposta para a sessão {Sessao}: {TipoErro}.", sessao.Sessao, ex.GetType().Name);
            return false;
        }
    }
}
