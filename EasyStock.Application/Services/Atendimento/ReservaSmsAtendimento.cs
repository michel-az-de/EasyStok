using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// S60 (#1391): reserva por SMS ligada só com a chave <c>Atendimento:ReenvioMensagens:SmsReserva</c> e um provedor
/// real. O provedor stub responde "enviado" sem enviar; a Api desliga a reserva nesse caso (ADR-0057, item 10).
/// </summary>
public sealed record ReservaSmsOpcoes(bool Ativa)
{
    public static readonly ReservaSmsOpcoes Desligada = new(false);
}

/// <summary>
/// S60: quando o WhatsApp desiste de uma mensagem de texto (sem reenvio agendado, sem esperar o cliente, falha que
/// não é incerta), o mesmo texto vai uma vez por SMS para o número da conversa. Falha do SMS só é registrada no log:
/// não muda o resultado do reenvio.
/// </summary>
public sealed class ReservaSmsAtendimento(
    ResolvedorCanal canais, ReservaSmsOpcoes opcoes, ILogger<ReservaSmsAtendimento> logger)
{
    public async Task TentarAsync(Conversa conversa, Mensagem mensagem, DateTime agora, CancellationToken ct = default)
    {
        if (!opcoes.Ativa || conversa.Canal != CanalConversa.WhatsApp || !mensagem.PrecisaDeReservaSms) return;

        try
        {
            await canais.Obter(CanalConversa.Sms).EnviarTextoAsync(conversa.ContatoIdExterno, mensagem.Texto!, ct);
            mensagem.RegistrarReservaSms(agora);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Reserva por SMS da mensagem {MensagemId} não saiu.", mensagem.Id);
        }
    }
}
