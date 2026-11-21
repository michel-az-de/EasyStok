using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// #2875: o evento que expira antes de ser avaliado nunca vira mensagem do outbox, então não há texto nem contato
/// gravados. Monta a mensagem de WhatsApp com o mesmo <see cref="ConstrutorMensagemOutbox"/> do avaliador (template da
/// rotina e telefone do payload), sem enfileirá-la, e a leva à conversa do cliente como não entregue. Sem commit: o
/// avaliador fecha a unidade de trabalho do escopo.
/// </summary>
public sealed class AvisoEventoExpiradoNaConversa(
    IEventoNotificacaoRepository eventos,
    IRotinaRepository rotinas,
    ITemplateRepository templates,
    IRendererTemplate renderer,
    AvisoNaoEntregueNaConversa aviso,
    PoliticaValidadeNotificacao politicaValidade)
{
    /// <returns>Se gravou a mensagem na conversa.</returns>
    public async Task<bool> RegistrarAsync(Guid empresaId, Guid eventoId, DateTime agora, CancellationToken ct = default)
    {
        var evento = await eventos.ObterAsync(empresaId, eventoId, ct);
        if (evento is not { Status: StatusEventoNotificacao.Expirado } || !AvisoNaoEntregueNaConversa.Cobre(evento.Tipo))
            return false;

        var rotina = SeletorRotina.Escolher(await rotinas.ListarAtivasAsync(evento.Tipo, empresaId, ct), empresaId);
        if (rotina is null || !CanaisDaRotina.Ler(rotina).Contains(CanalNotificacao.WhatsApp)) return false;

        var construida = await new ConstrutorMensagemOutbox(templates, renderer).ConstruirAsync(
            evento, rotina, CanalNotificacao.WhatsApp,
            new DestinatarioMensagem(null, ConstrutorMensagemOutbox.LerVariaveis(evento.PayloadJson)),
            [], agora, ct);
        return construida.Mensagem is { } mensagem
               && await aviso.RegistrarExpiradoAsync(evento, mensagem, politicaValidade.PrazoDe(evento.Tipo), agora, ct);
    }
}
