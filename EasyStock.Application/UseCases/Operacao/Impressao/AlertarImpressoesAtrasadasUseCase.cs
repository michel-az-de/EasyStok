using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Operacao;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Rodada do <c>ImpressaoPendenteAlertaJob</c> (S20): canhoto pendente há mais de <see cref="Atraso"/> vira
/// <c>impressao.atrasada</c> no SSE da empresa (o console avisa a dona). Repete a cada rodada enquanto
/// continuar pendente. Só olha as últimas <see cref="JanelaMaxima"/>: sem consumidor a fila só acumula, e o
/// alerta não pode crescer sem fim. Não grava nada.
/// </summary>
public sealed class AlertarImpressoesAtrasadasUseCase(
    IImpressaoPendenteRepository repo,
    IOperacaoEventPublisher operacaoEventos,
    TimeProvider relogio)
{
    public static readonly TimeSpan Atraso = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan JanelaMaxima = TimeSpan.FromHours(12);
    public const int MaximoPorRodada = 50;

    public async Task<int> ExecuteAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var atrasadas = await repo.ListarAtrasadasAsync(agora - JanelaMaxima, agora - Atraso, MaximoPorRodada, ct);
        foreach (var a in atrasadas)
            await operacaoEventos.PublicarAsync(EventosOperacao.ImpressaoAtrasada, a.EmpresaId,
                new ImpressaoAtrasadaOperacao(a.ImpressaoId, a.PedidoId, a.CriadaEm), ct);
        return atrasadas.Count;
    }
}
