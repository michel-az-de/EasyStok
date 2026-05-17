using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Application.UseCases.Financeiro.ContasReceber;

public sealed record AtualizarContaReceberCommand(
    Guid EmpresaId,
    Guid Id,
    string? Descricao = null,
    string? Observacoes = null,
    Guid? CategoriaFinanceiraId = null,
    Guid? CentroCustoId = null,
    Guid? ClienteId = null,
    DateTime? DataCompetencia = null);

public class AtualizarContaReceberUseCase(
    IContaReceberRepository repo,
    ICategoriaFinanceiraRepository categoriaRepo,
    ICentroCustoRepository centroRepo,
    IUnitOfWork uow)
{
    public async Task<ContaReceberResult?> ExecuteAsync(AtualizarContaReceberCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        var conta = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (conta is null) return null;

        if (conta.Status != StatusContaFinanceira.Rascunho)
            throw new UseCaseValidationException("So e possivel atualizar conta em Rascunho.");

        if (!string.IsNullOrWhiteSpace(cmd.Descricao)) conta.Descricao = cmd.Descricao.Trim();
        if (cmd.Observacoes is not null) conta.Observacoes = string.IsNullOrWhiteSpace(cmd.Observacoes) ? null : cmd.Observacoes.Trim();
        if (cmd.DataCompetencia.HasValue) conta.DataCompetencia = cmd.DataCompetencia;

        if (cmd.CategoriaFinanceiraId.HasValue && cmd.CategoriaFinanceiraId.Value != conta.CategoriaFinanceiraId)
        {
            var cat = await categoriaRepo.GetByIdAsync(cmd.EmpresaId, cmd.CategoriaFinanceiraId.Value, ct)
                       ?? throw new UseCaseValidationException("Categoria financeira nao encontrada.");
            if (!cat.Ativa) throw new UseCaseValidationException("Categoria financeira esta inativa.");
            if (cat.Tipo == TipoCategoriaFinanceira.Despesa)
                throw new UseCaseValidationException("Categoria de despesa nao pode ser usada em conta a receber.");
            conta.CategoriaFinanceiraId = cat.Id;
        }
        if (cmd.CentroCustoId.HasValue)
        {
            var centro = await centroRepo.GetByIdAsync(cmd.EmpresaId, cmd.CentroCustoId.Value, ct);
            if (centro is null || !centro.Ativo) throw new UseCaseValidationException("Centro de custo invalido ou inativo.");
            conta.CentroCustoId = centro.Id;
        }
        if (cmd.ClienteId.HasValue) conta.ClienteId = cmd.ClienteId.Value == Guid.Empty ? null : cmd.ClienteId;

        conta.AlteradoEm = DateTime.UtcNow;
        try
        {
            await repo.UpdateAsync(conta, ct);
            await uow.CommitAsync();
        }
        catch (RegraDeDominioVioladaException ex) { throw new UseCaseValidationException(ex.Message); }
        return ContaReceberResult.De(conta);
    }
}
