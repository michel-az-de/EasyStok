using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Domain.Entities.Atendimento;

namespace EasyStock.Application.Services.Atendimento.AcoesBotao;

/// <summary>
/// <c>acao:escolher_janela:&lt;janelaId&gt;:&lt;data&gt;</c> (S16): botão oferecido por <c>listar_janelas</c>.
/// Fechar o pedido ainda pede o agente (itens, endereço, cobrança), então o toque devolve a vez a ele:
/// enfileira o turno, e o agente lê a escolha no histórico (<c>[botão tocado: ...]</c>) e chama
/// <c>criar_pedido</c>, que revalida o prazo mínimo. A mensagem do botão já foi gravada pelo webhook.
/// </summary>
public sealed class EscolherJanelaAcaoBotao(IQueueService queueService) : IAcaoBotaoHandler
{
    public const string NomeAcao = "escolher_janela";

    public string Nome => NomeAcao;

    public Task ExecutarAsync(Guid empresaId, Conversa conversa, string payload, DateTime agora, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conversa);
        return queueService.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, new ProcessarTurnoAgenteJob(empresaId, conversa.Id));
    }
}
