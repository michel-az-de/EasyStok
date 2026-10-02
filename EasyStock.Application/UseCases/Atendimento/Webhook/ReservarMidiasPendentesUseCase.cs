using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Webhook;

/// <summary>
/// #1397, passo 1 da varredura (bypass de RLS, cross-tenant): trava os anexos com tentativa vencida e os
/// reserva por <see cref="Domain.Entities.Atendimento.Mensagem.PrazoFilaMidia"/> na mesma transação. Cobre o
/// job que a fila em memória perdeu num restart e as novas tentativas depois de uma falha.
/// </summary>
public sealed class ReservarMidiasPendentesUseCase(IConversaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    public Task<IReadOnlyList<ArmazenarMidiaWhatsAppJob>> ExecuteAsync(int limite, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync<IReadOnlyList<ArmazenarMidiaWhatsAppJob>>(async token =>
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            var pendentes = await repository.ListarMidiasPendentesComLockAsync(agora, limite, token);
            foreach (var mensagem in pendentes)
                mensagem.ReservarTentativaMidia(agora);
            await unitOfWork.CommitAsync();
            return pendentes
                .Where(m => m.ExternoId is not null && m.MidiaIdExterno is not null)
                .Select(m => new ArmazenarMidiaWhatsAppJob(m.EmpresaId, m.ConversaId, m.ExternoId!, m.MidiaIdExterno!))
                .ToList();
        }, ct);
}
