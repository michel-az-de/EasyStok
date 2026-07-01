namespace EasyStock.Application.UseCases.Analytics.Reposicao;

public class CalcularReposicaoUseCase(
    ObterReposicaoUseCase obterReposicao,
    ILogger<CalcularReposicaoUseCase> logger)
{
    public async Task<(IEnumerable<CalcularReposicaoResult> Items, int TotalCount)> ExecuteAsync(CalcularReposicaoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        // Fonte única (ADR-0039): a projeção por-produto substitui GetSugestaoReposicaoDetalhadaAsync;
        // mapeia ItemReposicao -> ReposicaoSugerida -> CalcularReposicaoResult (contrato preservado).
        var todos = await obterReposicao.ExecuteAsync(
            new ObterReposicaoCommand(cmd.EmpresaId, cmd.LojaId, cmd.DiasHistorico));
        var mapeados = todos.Select(i => CalcularReposicaoResult.FromDto(i.ToReposicaoSugerida())).ToList();
        var pagina = mapeados.Skip((cmd.Page - 1) * cmd.PageSize).Take(cmd.PageSize).ToList();

        logger.LogInformation("Calculated {Count} replenishment suggestions for empresa {EmpresaId}",
            mapeados.Count, cmd.EmpresaId);

        return (pagina, mapeados.Count);
    }
}
