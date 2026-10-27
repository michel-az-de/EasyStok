using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.UseCases.Financeiro.Categorias;

public sealed record CriarCategoriaFinanceiraCommand(
    Guid EmpresaId,
    string Nome,
    TipoCategoriaFinanceira Tipo,
    Guid? ParentId = null,
    string? Cor = null,
    string? Icone = null,
    int Ordem = 0);

public class CriarCategoriaFinanceiraUseCase(
    ICategoriaFinanceiraRepository repo,
    IUnitOfWork uow,
    ILogger<CriarCategoriaFinanceiraUseCase> logger)
{
    public async Task<CategoriaFinanceiraResult> ExecuteAsync(CriarCategoriaFinanceiraCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (string.IsNullOrWhiteSpace(cmd.Nome))
            throw new UseCaseValidationException("Nome e obrigatorio.");
        UseCaseGuards.EnsureSemTagsHtml(cmd.Nome, "Nome da categoria");
        // #2095 (F15/F7): tipo fora do enum e textos acima da coluna viravam categoria inválida ou 500.
        if (!Enum.IsDefined(cmd.Tipo))
            throw new UseCaseValidationException("Tipo da categoria inválido. Use Despesa, Receita ou Ambas.");
        ValidacaoFinanceira.Texto(cmd.Nome, ValidacaoFinanceira.NomeCadastroMax, "Nome da categoria");
        ValidacaoFinanceira.Texto(cmd.Cor, ValidacaoFinanceira.CorMax, "Cor");
        ValidacaoFinanceira.Texto(cmd.Icone, ValidacaoFinanceira.IconeMax, "Ícone");

        CategoriaFinanceira? parent = null;
        if (cmd.ParentId.HasValue)
        {
            parent = await repo.GetByIdAsync(cmd.EmpresaId, cmd.ParentId.Value, ct)
                     ?? throw new UseCaseValidationException("Categoria pai nao encontrada.");
        }

        if (await repo.ExisteNomeAsync(cmd.EmpresaId, cmd.Nome, cmd.ParentId, excludeId: null, ct))
            throw new UseCaseValidationException("Ja existe categoria ativa com este nome no mesmo nivel.");

        try
        {
            var categoria = CategoriaFinanceira.Criar(cmd.EmpresaId, cmd.Nome, cmd.Tipo, parent, cmd.Cor, cmd.Icone, cmd.Ordem);
            await repo.AddAsync(categoria, ct);
            await uow.CommitAsync();
            logger.LogInformation("Categoria financeira {Id} criada para empresa {Empresa}", categoria.Id, cmd.EmpresaId);
            return CategoriaFinanceiraResult.De(categoria);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
    }
}
