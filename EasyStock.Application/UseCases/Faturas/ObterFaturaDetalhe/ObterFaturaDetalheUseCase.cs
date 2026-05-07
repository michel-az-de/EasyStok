using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Faturas.Common;

namespace EasyStock.Application.UseCases.Faturas.ObterFaturaDetalhe;

public sealed record ObterFaturaDetalheCommand(
    Guid? EmpresaId,
    Guid FaturaId,
    /// <summary>true = admin (sem filtro EmpresaId); false = cliente (filtra).</summary>
    bool Admin = false
);

public class ObterFaturaDetalheUseCase(IFaturaRepository repo)
{
    public async Task<FaturaDetalheDto?> ExecuteAsync(ObterFaturaDetalheCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureNotEmpty(cmd.FaturaId, nameof(cmd.FaturaId));

        var fatura = cmd.Admin
            ? await repo.GetByIdAdminAsync(cmd.FaturaId, ct)
            : (cmd.EmpresaId.HasValue
                ? await repo.GetByIdAsync(cmd.EmpresaId.Value, cmd.FaturaId, ct)
                : null);

        return fatura is null ? null : FaturaDetalheDto.FromEntity(fatura);
    }
}
