using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// #2871: o resumo do pedido registrado pelo Console quando a conversa do WhatsApp está fora da janela de 24 h (o cliente
/// falou no WhatsApp pessoal da dona e nunca escreveu à loja, ou a última mensagem dele é antiga). Texto livre não sai
/// fora da janela; o resumo vai pela fila de avisos no modelo <c>pedido_atualizado</c> (aviso <c>ComandaAtualizada</c>),
/// numa linha só, e entra no histórico da conversa quando o envio sai.
/// </summary>
public sealed class ResumoPedidoForaDaJanela(AvisoStatusPedidoCliente avisoStatus, IUnitOfWork unitOfWork)
{
    /// <summary>Marco da chave de idempotência <c>pedidoId|resumo</c>: um resumo por pedido.</summary>
    public const string Marco = "resumo";

    public static bool Atende(Conversa conversa, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(conversa);
        return conversa.Canal == CanalConversa.WhatsApp && !conversa.DentroDaJanela(agora);
    }

    /// <summary>Enfileira o resumo e grava. Falso quando o aviso não pode sair (sem celular, WhatsApp revogado, bloqueio).</summary>
    public async Task<bool> EnfileirarAsync(Guid empresaId, Guid pedidoId, string texto, CancellationToken ct)
    {
        if (!await avisoStatus.EnfileirarAsync(TipoEventoNotificacao.ComandaAtualizada, empresaId, pedidoId, Marco, ct,
                mensagem: ResumoPedidoConversa.EmUmaLinha(texto)))
            return false;
        await unitOfWork.CommitAsync();
        return true;
    }
}
