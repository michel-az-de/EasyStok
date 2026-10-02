using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Operacao;
using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Application.UseCases.Operacao.Impressao;

/// <summary>
/// Rodada do <c>ImpressaoPendenteAlertaJob</c> (S20): canhoto pendente há mais de <see cref="Atraso"/> vira
/// <c>impressao.atrasada</c> no SSE da empresa (o console avisa a dona). Repete a cada rodada enquanto
/// continuar pendente. Só olha as últimas <see cref="JanelaMaxima"/>: sem consumidor a fila só acumula, e o
/// alerta não pode crescer sem fim. Não grava nada: devolve as atrasadas para o job abrir um escopo por
/// impressão e chamar <see cref="NotificarImpressaoTravadaUseCase"/> (e-mail e WhatsApp, N11). O limite vem de
/// <see cref="PrazosOptions.ImpressaoPendenteMin"/>.
/// </summary>
public sealed class AlertarImpressoesAtrasadasUseCase(
    IImpressaoPendenteRepository repo,
    IOperacaoEventPublisher operacaoEventos,
    TimeProvider relogio,
    IOptions<PrazosOptions> prazos)
{
    /// <summary>Padrão do limite; a fonte é <see cref="PrazosOptions.ImpressaoPendenteMin"/> (N11).</summary>
    public static readonly TimeSpan Atraso = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan JanelaMaxima = TimeSpan.FromHours(12);
    public const int MaximoPorRodada = 50;

    public async Task<IReadOnlyList<ImpressaoAtrasada>> ExecuteAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var atrasadas = await repo.ListarAtrasadasAsync(agora - JanelaMaxima, agora - prazos.Value.ImpressaoPendente, MaximoPorRodada, ct);
        foreach (var a in atrasadas)
            await operacaoEventos.PublicarAsync(EventosOperacao.ImpressaoAtrasada, a.EmpresaId,
                new ImpressaoAtrasadaOperacao(a.ImpressaoId, a.PedidoId, a.CriadaEm), ct);
        return atrasadas;
    }
}
