namespace EasyStock.Application.Ports.Output.Persistence;

public sealed record ProdutoAlteracaoResumo(
    Guid Id,
    string Acao,
    Guid UsuarioId,
    string? UsuarioNome,
    string? AlteracoesJson,
    DateTime AlteradoEm,
    string? Motivo,
    string? Observacao);

public interface IProdutoAlteracaoRepository
{
    Task AddAsync(ProdutoAlteracao alteracao);
    Task<IReadOnlyList<ProdutoAlteracaoResumo>> GetByProdutoAsync(Guid empresaId, Guid produtoId, int limit = 100);
}
