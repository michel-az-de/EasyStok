using EasyStock.Application.UseCases.Financeiro.Common;

namespace EasyStock.Application.UseCases.Financeiro.Categorias;

public sealed record AtualizarCategoriaFinanceiraCommand(
    Guid EmpresaId,
    Guid Id,
    string Nome,
    string? Cor = null,
    string? Icone = null,
    int? Ordem = null);

public class AtualizarCategoriaFinanceiraUseCase(
    ICategoriaFinanceiraRepository repo,
    IUnitOfWork uow)
{
    public async Task<CategoriaFinanceiraResult?> ExecuteAsync(AtualizarCategoriaFinanceiraCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, nameof(cmd.Id));
        if (string.IsNullOrWhiteSpace(cmd.Nome))
            throw new UseCaseValidationException("Nome e obrigatorio.");

        var categoria = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (categoria is null) return null;

        if (!string.Equals(categoria.Nome, cmd.Nome.Trim(), StringComparison.OrdinalIgnoreCase) &&
            await repo.ExisteNomeAsync(cmd.EmpresaId, cmd.Nome, categoria.ParentId, excludeId: cmd.Id, ct))
            throw new UseCaseValidationException("Ja existe categoria ativa com este nome no mesmo nivel.");

        try
        {
            categoria.Renomear(cmd.Nome);
            categoria.AtualizarApresentacao(cmd.Cor, cmd.Icone, cmd.Ordem);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await repo.UpdateAsync(categoria, ct);
        await uow.CommitAsync();
        return CategoriaFinanceiraResult.De(categoria);
    }
}
