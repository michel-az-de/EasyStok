using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Faturas.Common;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.Faturas.ListarFaturasCliente;

public sealed record ListarFaturasClienteCommand(
    Guid EmpresaId,
    StatusFatura? Status = null,
    DateTime? VencimentoDe = null,
    DateTime? VencimentoAte = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record ListarFaturasClienteResult(
    IReadOnlyList<FaturaResumoDto> Itens,
    int Total,
    int Page,
    int PageSize
);

public class ListarFaturasClienteUseCase(IFaturaRepository repo)
{
    public async Task<ListarFaturasClienteResult> ExecuteAsync(
        ListarFaturasClienteCommand cmd,
        CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var page = Math.Max(1, cmd.Page);
        var pageSize = Math.Clamp(cmd.PageSize, 1, 100);

        var (itens, total) = await repo.ListarClienteAsync(
            cmd.EmpresaId, cmd.Status, cmd.VencimentoDe, cmd.VencimentoAte,
            page, pageSize, ct);

        var dtos = itens.Select(FaturaResumoDto.FromEntity).ToList();
        return new ListarFaturasClienteResult(dtos, total, page, pageSize);
    }
}
