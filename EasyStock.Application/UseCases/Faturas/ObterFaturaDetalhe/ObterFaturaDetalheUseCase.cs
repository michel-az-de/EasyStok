using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Faturas.Common;

namespace EasyStock.Application.UseCases.Faturas.ObterFaturaDetalhe;

/// <summary>Command de leitura de detalhe de fatura (cliente ou admin).</summary>
/// <param name="EmpresaId">Empresa do cliente quando <c>Admin == false</c>; ignorado em modo admin.</param>
/// <param name="FaturaId">Identificador da fatura.</param>
/// <param name="Admin">true = admin (sem filtro EmpresaId); false = cliente (filtra).</param>
public sealed record ObterFaturaDetalheCommand(
    Guid? EmpresaId,
    Guid FaturaId,
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
