using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Entities.Financeiro;

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
        ValidarTextos(cmd.Codigo, cmd.Nome, cmd.Descricao);

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

    /// <summary>#2095 (F7/F15): tamanho da coluna e sem tags HTML (o centro aceitava "&lt;script&gt;").</summary>
    internal static void ValidarTextos(string? codigo, string? nome, string? descricao)
    {
        ValidacaoFinanceira.Texto(codigo, ValidacaoFinanceira.CodigoCentroMax, "Código do centro de custo");
        ValidacaoFinanceira.Texto(nome, ValidacaoFinanceira.NomeCadastroMax, "Nome do centro de custo");
        ValidacaoFinanceira.Texto(descricao, ValidacaoFinanceira.DescricaoCentroMax, "Descrição do centro de custo");
        UseCaseGuards.EnsureSemTagsHtml(codigo, "Código do centro de custo");
        UseCaseGuards.EnsureSemTagsHtml(nome, "Nome do centro de custo");
        UseCaseGuards.EnsureSemTagsHtml(descricao, "Descrição do centro de custo");
    }
}
