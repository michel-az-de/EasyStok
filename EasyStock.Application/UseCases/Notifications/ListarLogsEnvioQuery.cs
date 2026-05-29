using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.Notifications;

public sealed record ListarLogsEnvioQuery(
    Guid? EmpresaId,
    StatusOutbox? Status = null,
    CanalNotificacao? Canal = null,
    DateTime? De = null,
    DateTime? Ate = null,
    int Page = 1,
    int PageSize = 20) : ICommand;

public sealed record ListarLogsEnvioResult(
    IReadOnlyList<LogEnvioNotificacao> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed class ListarLogsEnvioUseCase(ILogEnvioNotificacaoRepository logRepository)
    : IUseCase<ListarLogsEnvioQuery, ListarLogsEnvioResult>
{
    public async Task<ListarLogsEnvioResult> ExecuteAsync(ListarLogsEnvioQuery query)
    {
        var (items, total) = await logRepository.ListarAsync(
            query.EmpresaId, query.Status, query.Canal,
            query.De, query.Ate, query.Page, query.PageSize);

        return new ListarLogsEnvioResult(items, total, query.Page, query.PageSize);
    }
}
