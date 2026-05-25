using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence;

public sealed record MovimentacaoEstoqueAlteracaoResumo(
    Guid Id,
    string Acao,
    Guid UsuarioId,
    string? UsuarioNome,
    string? EmailUsuario,
    string? AlteracoesJson,
    string? Ip,
    string? UserAgent,
    DateTime AlteradoEm,
    string? Motivo,
    string? Observacao);

public interface IMovimentacaoEstoqueAlteracaoRepository
{
    Task AddAsync(MovimentacaoEstoqueAlteracao alteracao);
    Task<IReadOnlyList<MovimentacaoEstoqueAlteracaoResumo>> GetByMovimentacaoAsync(Guid empresaId, Guid movimentacaoId, int limit = 100);
    Task<IReadOnlyList<MovimentacaoEstoqueAlteracaoResumo>> GetByUsuarioAsync(Guid usuarioId, DateTime desde, DateTime ate, int pageSize = 50);
    Task<int> DeleteByUsuarioAsync(Guid usuarioId);
}
