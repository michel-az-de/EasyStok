using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Application.UseCases.Financeiro.CentrosCusto;

public sealed record CriarCentroCustoCommand(
    Guid EmpresaId,
    string Codigo,
    string Nome,
    Guid? LojaId = null,
    string? Descricao = null);

public class CriarCentroCustoUseCase(
    ICentroCustoRepository repo,
    IUnitOfWork uow)
{
    public async Task<CentroCustoResult> ExecuteAsync(CriarCentroCustoCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (string.IsNullOrWhiteSpace(cmd.Codigo))
            throw new UseCaseValidationException("Codigo e obrigatorio.");
        if (string.IsNullOrWhiteSpace(cmd.Nome))
            throw new UseCaseValidationException("Nome e obrigatorio.");

        if (await repo.GetByCodigoAsync(cmd.EmpresaId, cmd.Codigo, ct) is not null)
            throw new UseCaseValidationException("Ja existe centro de custo com este codigo.");

        try
        {
            var centro = CentroCusto.Criar(cmd.EmpresaId, cmd.Codigo, cmd.Nome, cmd.LojaId, cmd.Descricao);
            await repo.AddAsync(centro, ct);
            await uow.CommitAsync();
            return CentroCustoResult.De(centro);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
