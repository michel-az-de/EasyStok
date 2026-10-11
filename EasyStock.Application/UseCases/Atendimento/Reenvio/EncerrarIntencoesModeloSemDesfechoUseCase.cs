using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.UseCases.Atendimento.Reenvio;

/// <summary>
/// #1562 (bypass de RLS, cross-tenant): a intenção de modelo é gravada antes do POST ao canal. Se o processo cai
/// entre os dois, ela ficaria pendente para sempre. Passado o prazo, vira falha incerta, com o mesmo aviso do
/// envio sem resposta. Nunca reenvia: ninguém sabe se o modelo saiu.
/// </summary>
public sealed class EncerrarIntencoesModeloSemDesfechoUseCase(IConversaRepository repository, IUnitOfWork unitOfWork, TimeProvider relogio)
{
    /// <summary>Folga sobre o envio em andamento: o POST ao canal desiste em segundos.</summary>
    public static readonly TimeSpan PrazoSemDesfecho = TimeSpan.FromMinutes(10);

    public Task<int> ExecuteAsync(int limite, CancellationToken ct = default) =>
        unitOfWork.ExecuteInTransactionSemRetryAsync(async token =>
        {
            var agora = relogio.GetUtcNow().UtcDateTime;
            var presas = await repository.ListarIntencoesModeloSemDesfechoComLockAsync(agora - PrazoSemDesfecho, limite, token);
            foreach (var intencao in presas)
                intencao.RegistrarFalhaEnvio(EnviarModeloConversaUseCase.AvisoEnvioIncerto, TipoFalhaEnvio.Incerta, agora);
            if (presas.Count > 0)
                await unitOfWork.CommitAsync();
            return presas.Count;
        }, ct);
}
