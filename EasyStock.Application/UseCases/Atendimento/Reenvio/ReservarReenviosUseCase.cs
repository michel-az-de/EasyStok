using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Reenvio;

/// <summary>Mensagem tirada do agendamento para reenviar no escopo da empresa dela.</summary>
public sealed record ReenvioReservado(Guid EmpresaId, Guid ConversaId, Guid MensagemId);

/// <summary>
/// S57 (#1355), passo 1 (bypass de RLS, cross-tenant): trava as mensagens com reenvio vencido e tira o
/// agendamento delas na mesma transação, para outro processo da API não reenviar a mesma mensagem.
/// S58 (#1391): antes, quem esperava o cliente e ele já respondeu volta para a fila na hora.
/// </summary>
public sealed class ReservarReenviosUseCase(IConversaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public Task<IReadOnlyList<ReenvioReservado>> ExecuteAsync(int limite, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync<IReadOnlyList<ReenvioReservado>>(async token =>
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            var respondidas = await repository.ListarAguardandoComRespostaComLockAsync(limite, token);
            foreach (var mensagem in respondidas)
                mensagem.LiberarAposResposta(agora);
            if (respondidas.Count > 0)
                await unitOfWork.CommitAsync();

            var vencidas = await repository.ListarReenviosVencidosComLockAsync(agora, limite, token);
            foreach (var mensagem in vencidas)
                mensagem.ReservarReenvio();
            await unitOfWork.CommitAsync();
            return vencidas.Select(m => new ReenvioReservado(m.EmpresaId, m.ConversaId, m.Id)).ToList();
        }, ct);
}
