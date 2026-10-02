namespace EasyStock.Application.Ports.Output.Notifications;

/// <summary>
/// Retrato agregado do backlog do motor de notificações (N1, ver e avisar): só números, nenhuma linha. Alimenta o health
/// check de backlog (<c>/health/notificacoes</c> na API e o ping do Worker). O que o heartbeat dos loops não enxerga
/// (loop vivo que não envia, envio que só simula, mensagem presa no limbo) aparece aqui. "Na última hora" conta pelo
/// momento em que a mensagem terminou (<c>ProximaTentativaEm</c> das terminais, ver <c>OutboxMensagemNotificacao</c>).
/// </summary>
/// <param name="IdadeDoPendenteElegivelMaisAntigo">Há quanto tempo a mensagem <c>Pendente</c> elegível mais antiga espera; nulo sem backlog.</param>
/// <param name="IdadeDoEventoPendenteMaisAntigo">Há quanto tempo o evento <c>Pendente</c> mais antigo espera o avaliador; nulo sem backlog.</param>
/// <param name="EmEnvioAlemDoLease"><c>EmEnvio</c> com o lease vencido: o loop reservou e não fechou.</param>
/// <param name="FalhadoNaUltimaHora">Mensagens que terminaram <c>Falhado</c> na última hora.</param>
/// <param name="SimuladoNaUltimaHora">Mensagens que terminaram <c>Simulado</c> (stub ou console) na última hora.</param>
/// <param name="IndeterminadoNaUltimaHora">Mensagens que terminaram <c>Indeterminado</c> na última hora.</param>
/// <param name="ExpiradoNaUltimaHora">Mensagens que expiraram por prazo na última hora.</param>
/// <param name="ProcessadoSemOutboxEm24h">Eventos <c>Processado</c> nas últimas 24 h sem nenhuma mensagem no outbox.</param>
public sealed record BacklogNotificacoes(
    TimeSpan? IdadeDoPendenteElegivelMaisAntigo,
    TimeSpan? IdadeDoEventoPendenteMaisAntigo,
    int EmEnvioAlemDoLease,
    int FalhadoNaUltimaHora,
    int SimuladoNaUltimaHora,
    int IndeterminadoNaUltimaHora,
    int ExpiradoNaUltimaHora,
    int ProcessadoSemOutboxEm24h);

/// <summary>
/// Mede o <see cref="BacklogNotificacoes"/> com consultas agregadas, cross-tenant por natureza: a implementação liga o
/// bypass de RLS pela porta <c>IRowLevelSecurityBypass</c> e não traz linha nenhuma.
/// </summary>
public interface IBacklogNotificacoes
{
    Task<BacklogNotificacoes> MedirAsync(CancellationToken ct = default);
}
