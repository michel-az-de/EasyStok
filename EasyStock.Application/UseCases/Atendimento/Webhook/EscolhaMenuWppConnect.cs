using EasyStock.Application.Ports.Output.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// #2515: na conexão provisória o menu sai numerado no texto. Texto do cliente que escolhe uma opção do menu pendente
/// volta como o toque no botão (<c>interactive</c> sem texto, como o clique da Meta), e a ação do botão roda em vez de o
/// "2" chegar solto ao agente.
/// </summary>
public static class EscolhaMenuWppConnect
{
    public static async Task<EventoWhatsApp> TraduzirAsync(EventoWhatsApp evento, string sessao, IMenusNumeradosWppConnect menus,
        CancellationToken ct = default)
    {
        var entradas = new List<EntradaWhatsApp>(evento.Entradas.Count);
        foreach (var entrada in evento.Entradas)
        {
            var mensagens = new List<MensagemRecebidaWhatsApp>(entrada.Mensagens.Count);
            foreach (var msg in entrada.Mensagens)
                mensagens.Add(msg.Tipo == "text" && await menus.EscolhaAsync(sessao, msg.De, msg.TextoCorpo, ct) is { } escolha
                    ? msg with { Tipo = "interactive", TextoCorpo = null, BotaoRespostaId = escolha.Id }
                    : msg);
            entradas.Add(entrada with { Mensagens = mensagens });
        }
        return new(entradas);
    }
}
