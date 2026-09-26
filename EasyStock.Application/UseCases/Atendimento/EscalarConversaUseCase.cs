using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>
/// Escalada para a dona (S07, US-013): <see cref="Conversa.Assumir"/> (o agente cala, RN-04),
/// <c>Mensagem(Sistema, motivo)</c> interna (sem <c>wamid</c>, nunca enviada ao cliente), evento
/// <see cref="TipoEventoNotificacao.ConversaEscalada"/> e SSE <c>conversa.escalada</c>.
///
/// <para>
/// O evento vai para o outbox na mesma unidade de trabalho de quem chama (ADR-0030): não há commit
/// aqui. Sem usuário alvo no payload, o Push sai para todas as <c>WebPushSubscription</c> ativas da
/// empresa (destinatário <c>empresa:{id}</c>, resolvido pelo <c>WebPushCanal</c>): a dona é avisada
/// em todos os dispositivos.
/// </para>
///
/// <para>
/// O SSE é dica para o console recarregar a conversa; hoje é no-op (S18). Sai antes do commit do
/// chamador: quando S18 ligar o broker, o console relê o estado, então um aviso adiantado é inofensivo.
/// </para>
/// </summary>
public sealed class EscalarConversaUseCase(
    IConversaRepository conversaRepository,
    INotificadorService notificador,
    IOperacaoEventPublisher eventPublisher) : IEscaladorConversa
{
    public const string PrefixoMotivo = "escalado para a dona: ";
    public const string EventoSse = "conversa.escalada";

    public async Task EscalarAsync(Guid empresaId, Conversa conversa, string motivo, DateTime agora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversa);
        if (!conversa.EstaAberta) return;

        var motivoLimpo = string.IsNullOrWhiteSpace(motivo) ? "sem motivo informado" : motivo.Trim();
        var texto = PrefixoMotivo + motivoLimpo;
        if (texto.Length > Mensagem.TextoTamanhoMaximo) texto = texto[..Mensagem.TextoTamanhoMaximo];

        conversa.Assumir(agora);
        await conversaRepository.AddMensagemAsync(
            Mensagem.Saida(empresaId, conversa.Id, AutorMensagem.Sistema, agora, TipoConteudoMensagem.Texto, texto),
            ct);

        var cliente = conversa.ContatoNome ?? conversa.ContatoIdExterno;
        var payload = JsonSerializer.Serialize(new
        {
            conversaId = conversa.Id.ToString(),
            cliente,
            motivo = motivoLimpo,
        });
        await notificador.EnfileirarEventoAsync(TipoEventoNotificacao.ConversaEscalada, empresaId, payload, conversa.Id, ct);

        await eventPublisher.PublicarAsync(EventoSse, empresaId,
            new { conversaId = conversa.Id, cliente, motivo = motivoLimpo }, ct);
    }
}
