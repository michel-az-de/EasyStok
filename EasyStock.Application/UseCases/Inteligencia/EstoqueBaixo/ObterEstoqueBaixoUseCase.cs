using EasyStock.Application.UseCases.Analytics.Reposicao;

namespace EasyStock.Application.UseCases.Inteligencia.EstoqueBaixo;

public class ObterEstoqueBaixoUseCase(
    ObterReposicaoUseCase obterReposicao,
    ILogger<ObterEstoqueBaixoUseCase> logger)
{
    public async Task<(IEnumerable<EstoqueBaixoResult> Items, int Total)> ExecuteAsync(ObterEstoqueBaixoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        // Fonte única (ADR-0039 / issue 748): "estoque baixo" = itens elegíveis para reposição
        // (mesma projeção por-produto), no lugar do predicado por-lote GetEstoqueBaixoAsync (qty<=limite).
        var todos = await obterReposicao.ExecuteAsync(
            new ObterReposicaoCommand(cmd.EmpresaId, cmd.LojaId)).ConfigureAwait(false);

        var mapeados = todos.Select(i => new EstoqueBaixoResult(
            Guid.Empty, i.ProdutoId, i.Nome, null, i.QuantidadeVigente, i.NivelMinimo)).ToList();
        var pagina = mapeados.Skip((cmd.Page - 1) * cmd.PageSize).Take(cmd.PageSize).ToList();

        logger.LogInformation(
            "Estoque baixo (fonte unica) para empresa {EmpresaId}, loja {LojaId}: {Total} itens.",
            cmd.EmpresaId, cmd.LojaId ?? Guid.Empty, mapeados.Count);

        return (pagina, mapeados.Count);
    }
}
