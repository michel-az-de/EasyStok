using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2875: o aviso ao cliente que sai pela fila de notificações (<see cref="TipoEventoNotificacao.ComandaAtualizada"/>,
/// enfileirado pelo <see cref="AvisoStatusPedidoCliente"/>) e não chega vira mensagem não entregue na conversa do
/// cliente, com o motivo. Assim entra em "Não entregues" e a loja reenvia quando a janela abrir. O espelho do envio que
/// deu certo continua no provider; aqui só o que não saiu.
/// <para>
/// Grava sem commit: quem chama fecha a unidade de trabalho junto com o estado do outbox ou do evento.
/// </para>
/// </summary>
public sealed class AvisoNaoEntregueNaConversa(IConversaRepository conversas)
{
    /// <summary>Tipos de aviso que vão ao cliente com conversa no WhatsApp.</summary>
    public static bool Cobre(TipoEventoNotificacao tipo) => tipo == TipoEventoNotificacao.ComandaAtualizada;

    /// <summary>Falha definitiva do envio (outbox <c>Falhado</c>, sem fallback de canal aberto).</summary>
    /// <returns>Se gravou a mensagem na conversa.</returns>
    public Task<bool> RegistrarFalhaAsync(
        EventoNotificacao evento, OutboxMensagemNotificacao mensagem, ResultadoEnvio resultado, DateTime em,
        CancellationToken ct = default)
    {
        var motivo = MotivoAvisoNaoEntregue.DaFalha(resultado.CodigoErro, resultado.StatusHttp, resultado.ErroDetalhado);
        return motivo is null ? Task.FromResult(false) : RegistrarAsync(evento, mensagem, motivo, em, ct);
    }

    /// <summary>
    /// Aviso que passou do prazo sem sair: a mensagem do outbox que expirou ou, quando o evento expirou antes de ser
    /// avaliado, a montada só para ler o texto e o contato.
    /// </summary>
    /// <returns>Se gravou a mensagem na conversa.</returns>
    public Task<bool> RegistrarExpiradoAsync(
        EventoNotificacao evento, OutboxMensagemNotificacao mensagem, TimeSpan prazo, DateTime em,
        CancellationToken ct = default) =>
        RegistrarAsync(evento, mensagem, MotivoAvisoNaoEntregue.Expirado(prazo), em, ct);

    private async Task<bool> RegistrarAsync(
        EventoNotificacao evento, OutboxMensagemNotificacao mensagem, string motivo, DateTime em, CancellationToken ct)
    {
        if (!Cobre(evento.Tipo) || mensagem.Canal != CanalNotificacao.WhatsApp
            || string.IsNullOrWhiteSpace(mensagem.CorpoRenderizado))
            return false;

        var conversa = await ConversaDoClienteAsync(evento, mensagem.Destinatario, ct);
        if (conversa is null) return false;

        var naoEntregue = Mensagem.Saida(conversa.EmpresaId, conversa.Id, AutorMensagem.Sistema, em,
            TipoConteudoMensagem.Texto, Truncar(mensagem.CorpoRenderizado));
        // A loja decide o reenvio em "Não entregues": repetir sozinho mandaria o status do pedido já velho.
        naoEntregue.RegistrarFalhaEnvio(motivo, TipoFalhaEnvio.Permanente, em, reenvioAutomatico: false);
        await conversas.AddMensagemAsync(naoEntregue, ct);
        return true;
    }

    /// <summary>A conversa aberta do contato (a mesma do espelho do envio que dá certo) e, sem ela, a do pedido.</summary>
    private async Task<Conversa?> ConversaDoClienteAsync(EventoNotificacao evento, string destinatario, CancellationToken ct)
    {
        if (Contato(destinatario) is { } contato
            && await conversas.ObterAbertaPorContatoAsync(evento.EmpresaId, CanalConversa.WhatsApp, contato, ct) is { } aberta)
            return aberta;

        if (evento.RefEntidadeId is not { } pedidoId) return null;
        return await conversas.ObterIdPorPedidoAsync(evento.EmpresaId, pedidoId, ct) is { } conversaId
            ? await conversas.ObterPorIdAsync(evento.EmpresaId, conversaId, ct)
            : null;
    }

    private static string? Contato(string destinatario)
    {
        try
        {
            return Conversa.NormalizarContato(CanalConversa.WhatsApp, destinatario);
        }
        catch (RegraDeDominioVioladaException)
        {
            return null;
        }
    }

    private static string Truncar(string texto) =>
        texto.Length <= Mensagem.TextoTamanhoMaximo ? texto : texto[..Mensagem.TextoTamanhoMaximo];
}
