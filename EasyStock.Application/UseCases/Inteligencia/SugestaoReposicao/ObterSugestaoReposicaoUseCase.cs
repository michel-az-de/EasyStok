using EasyStock.Application.UseCases.Analytics.Reposicao;

namespace EasyStock.Application.UseCases.Inteligencia.SugestaoReposicao;

public class ObterSugestaoReposicaoUseCase(
    ObterReposicaoUseCase obterReposicao,
    ILogger<ObterSugestaoReposicaoUseCase> logger)
{
    public async Task<(IEnumerable<SugestaoReposicaoResult> Items, int Total)> ExecuteAsync(ObterSugestaoReposicaoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        // Fonte única (ADR-0039): a projeção por-produto substitui GetSugestaoReposicaoAsync
        // (predicado por-lote). ItemEstoqueId é identidade de lote, sem equivalente por-produto:
        // fica vazio (o fluxo "Gerar" usa NomeProduto/ProdutoId, não o lote).
        var todos = await obterReposicao.ExecuteAsync(
            new ObterReposicaoCommand(cmd.EmpresaId, cmd.LojaId)).ConfigureAwait(false);

        var mapeados = todos.Select(i => new SugestaoReposicaoResult(
            Guid.Empty,
            i.ProdutoId,
            i.Nome,
            null,
            i.QuantidadeVigente,
            i.NivelMinimo,
            i.QuantidadeSugerida,
            i.CustoEstimadoReposicao)).ToList();
        var pagina = mapeados.Skip((cmd.Page - 1) * cmd.PageSize).Take(cmd.PageSize).ToList();

        logger.LogInformation(
            "Sugestao de reposicao (fonte unica) para empresa {EmpresaId}, loja {LojaId}: {Total} elegiveis.",
            cmd.EmpresaId, cmd.LojaId ?? Guid.Empty, mapeados.Count);

        return (pagina, mapeados.Count);
    }
}
