namespace EasyStock.Application.Services.Notifications.Orchestrators;

/// <summary>
/// Orquestra 1 rodada de avaliação:
/// (a) processa <see cref="EasyStock.Domain.Entities.Notifications.EventoNotificacao"/> pendentes,
/// (b) detecta rotinas Cron que deveriam ter disparado.
/// Idempotente — pode ser invocado por loop in-process ou trigger HTTP.
/// </summary>
public interface INotificacoesAvaliadorOrchestrator
{
    /// <param name="janelaAvaliacao">Sem efeito desde a N12 (servia ao ramo cron, removido); fica na assinatura
    /// para os chamadores. O agendamento por horário está no coletor de rotinas agendadas.</param>
    Task ExecutarRodadaAsync(TimeSpan janelaAvaliacao, CancellationToken ct = default);
}
