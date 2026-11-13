using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.UseCases.Atendimento;

/// <summary>
/// #2283: reenfileira o turno do agente que se perdeu quando a Api reiniciou. Roda uma vez, no start do consumidor da
/// fila. O turno é idempotente: conversa já respondida, escalada ou fora da janela é ignorada por
/// <c>AgenteAtendimentoService</c>, então reenfileirar a mais não gera resposta dupla.
/// </summary>
public sealed class RecuperarTurnosPerdidosUseCase(
    ITurnosPendentesQuery query,
    IQueueService fila,
    ILogger<RecuperarTurnosPerdidosUseCase> logger)
{
    /// <summary>Janela zero ou negativa desliga a recuperação (rollback por configuração).</summary>
    public async Task<int> ExecuteAsync(DateTime agora, TimeSpan janela, CancellationToken ct = default)
    {
        if (janela <= TimeSpan.Zero)
            return 0;

        var pendentes = await query.ListarAsync(agora - janela, agora, ct);
        foreach (var turno in pendentes)
            await fila.EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, new ProcessarTurnoAgenteJob(turno.EmpresaId, turno.ConversaId));

        // A contagem mede a frequência da perda: zero em todo deploy diz que a fila durável (opção B) não é necessária.
        // Warning mesmo com zero: produção só grava de Warning para cima, e a linha é uma por start.
        logger.LogWarning("Turnos do agente recuperados no start: {Quantidade} (janela de {JanelaMinutos} min).",
            pendentes.Count, janela.TotalMinutes);
        return pendentes.Count;
    }
}
