using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.ObterHistoricoAlteracoesFornecedor;

public sealed record ObterHistoricoAlteracoesFornecedorQuery(
    Guid EmpresaId,
    Guid FornecedorId,
    int Max = 200);

public sealed record FornecedorAlteracaoResult(
    Guid Id,
    Guid FornecedorId,
    Guid? AlteradoPorUserId,
    string? AlteradoPorNome,
    string Campo,
    string? ValorAntigo,
    string? ValorNovo,
    DateTime AlteradoEm,
    string? Origem
);

/// <summary>
/// Onda P4 — retorna o trail de alterações de um fornecedor (ordem
/// cronológica reversa). Valida tenant antes de consultar.
/// </summary>
public class ObterHistoricoAlteracoesFornecedorUseCase(IFornecedorRepository repo)
{
    public async Task<IReadOnlyList<FornecedorAlteracaoResult>?> ExecuteAsync(
        ObterHistoricoAlteracoesFornecedorQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(q.FornecedorId, "FornecedorId");

        var fornecedor = await repo.GetByIdAsync(q.EmpresaId, q.FornecedorId);
        if (fornecedor == null) return null;

        var alteracoes = await repo.GetAlteracoesAsync(q.FornecedorId, q.Max);
        return alteracoes.Select(a => new FornecedorAlteracaoResult(
            a.Id, a.FornecedorId, a.AlteradoPorUserId, a.AlteradoPorNome,
            a.Campo, a.ValorAntigo, a.ValorNovo, a.AlteradoEm, a.Origem)).ToList();
    }
}
