using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Application.UseCases.Financeiro.Categorias;

public sealed record MoverCategoriaFinanceiraCommand(Guid EmpresaId, Guid Id, Guid? NovoParentId);

public class MoverCategoriaFinanceiraUseCase(
    ICategoriaFinanceiraRepository repo,
    IUnitOfWork uow)
{
    public async Task<CategoriaFinanceiraResult?> ExecuteAsync(MoverCategoriaFinanceiraCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var c = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (c is null) return null;

        CategoriaFinanceira? novoParent = null;
        if (cmd.NovoParentId.HasValue)
        {
            novoParent = await repo.GetByIdAsync(cmd.EmpresaId, cmd.NovoParentId.Value, ct)
                          ?? throw new UseCaseValidationException("Categoria pai nao encontrada.");
            // Detecta ciclo (novoParent ja e descendente de c)
            var cursor = novoParent;
            while (cursor is not null)
            {
                if (cursor.Id == c.Id)
                    throw new UseCaseValidationException("Mover criaria ciclo na hierarquia.");
                if (cursor.ParentId is null) break;
                cursor = await repo.GetByIdAsync(cmd.EmpresaId, cursor.ParentId.Value, ct);
            }
        }

        try
        {
            c.MoverPara(novoParent);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await repo.UpdateAsync(c, ct);
        await uow.CommitAsync();
        return CategoriaFinanceiraResult.De(c);
    }
}
