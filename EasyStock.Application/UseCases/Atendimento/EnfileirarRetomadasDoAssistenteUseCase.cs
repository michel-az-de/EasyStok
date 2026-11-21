using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>
/// #3068: varredura da conversa parada com a equipe. Enfileira o turno marcado como retomada para cada candidata; o
/// turno é idempotente (confere prazo, portões e a guarda por mensagem), então enfileirar a mais não gera resposta dupla.
/// O cliente tem de estar esperando há mais que o prazo e há menos que a janela: além dela é conversa velha, que a
/// varredura não ressuscita (deploy, Assistente religado).
/// </summary>
public sealed class EnfileirarRetomadasDoAssistenteUseCase(
    IRetomadasPendentesQuery query,
    IQueueService fila,
    IOptions<RetomadaAssistenteOptions> opcoes)
{
    public async Task<int> ExecuteAsync(DateTime agora, CancellationToken ct = default)
    {
        var prazo = opcoes.Value.Prazo;
        var janela = opcoes.Value.Janela;
        if (prazo <= TimeSpan.Zero || janela <= prazo)
            return 0;

        var candidatas = await query.ListarAsync(agora - janela, agora - prazo, ct);
        foreach (var candidata in candidatas)
            await fila.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente,
                new ProcessarTurnoAgenteJob(candidata.EmpresaId, candidata.ConversaId, Retomada: true));
        return candidatas.Count;
    }
}
