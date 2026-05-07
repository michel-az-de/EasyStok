using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faturas.Common;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.Faturas.ListarFaturasAdmin;

public sealed record ListarFaturasAdminCommand(
    Guid? EmpresaId = null,
    StatusFatura? Status = null,
    OrigemFatura? Origem = null,
    DateTime? VencimentoDe = null,
    DateTime? VencimentoAte = null,
    decimal? ValorMin = null,
    decimal? ValorMax = null,
    string? Busca = null,
    int Page = 1,
    int PageSize = 20
);

public sealed record ListarFaturasAdminResult(
    IReadOnlyList<FaturaResumoDto> Itens,
    int Total,
    int Page,
    int PageSize
);

public class ListarFaturasAdminUseCase(IFaturaRepository repo)
{
    public async Task<ListarFaturasAdminResult> ExecuteAsync(
        ListarFaturasAdminCommand cmd,
        CancellationToken ct = default)
    {
        var page = Math.Max(1, cmd.Page);
        var pageSize = Math.Clamp(cmd.PageSize, 1, 100);

        var (itens, total) = await repo.ListarAdminAsync(
            cmd.EmpresaId, cmd.Status, cmd.Origem,
            cmd.VencimentoDe, cmd.VencimentoAte,
            cmd.ValorMin, cmd.ValorMax, cmd.Busca,
            page, pageSize, ct);

        var dtos = itens.Select(FaturaResumoDto.FromEntity).ToList();
        return new ListarFaturasAdminResult(dtos, total, page, pageSize);
    }
}
