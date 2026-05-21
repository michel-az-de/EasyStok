using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Financeiro.Common;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Application.UseCases.Financeiro.ContasPagar;

public sealed record AtualizarContaPagarCommand(
    Guid EmpresaId,
    Guid Id,
    string? Descricao = null,
    string? Observacoes = null,
    Guid? CategoriaFinanceiraId = null,
    Guid? CentroCustoId = null,
    Guid? FornecedorId = null,
    DateTime? DataCompetencia = null);

public class AtualizarContaPagarUseCase(
    IContaPagarRepository repo,
    ICategoriaFinanceiraRepository categoriaRepo,
    ICentroCustoRepository centroRepo,
    IUnitOfWork uow)
{
    public async Task<ContaPagarResult?> ExecuteAsync(AtualizarContaPagarCommand cmd, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, nameof(cmd.Id));

        var conta = await repo.GetByIdAsync(cmd.EmpresaId, cmd.Id, ct);
        if (conta is null) return null;

        if (conta.Status != StatusContaFinanceira.Rascunho)
            throw new UseCaseValidationException("So e possivel atualizar conta em Rascunho.");

        if (!string.IsNullOrWhiteSpace(cmd.Descricao)) conta.Descricao = cmd.Descricao.Trim();
        if (cmd.Observacoes is not null) conta.Observacoes = string.IsNullOrWhiteSpace(cmd.Observacoes) ? null : cmd.Observacoes.Trim();
        if (cmd.DataCompetencia.HasValue) conta.DataCompetencia = DataUtc.ParaUtc(cmd.DataCompetencia.Value);

        if (cmd.CategoriaFinanceiraId.HasValue && cmd.CategoriaFinanceiraId.Value != conta.CategoriaFinanceiraId)
        {
            var cat = await categoriaRepo.GetByIdAsync(cmd.EmpresaId, cmd.CategoriaFinanceiraId.Value, ct)
                       ?? throw new UseCaseValidationException("Categoria financeira nao encontrada.");
            if (!cat.Ativa)
                throw new UseCaseValidationException("Categoria financeira esta inativa.");
            if (cat.Tipo == TipoCategoriaFinanceira.Receita)
                throw new UseCaseValidationException("Categoria de receita nao pode ser usada em conta a pagar.");
            conta.CategoriaFinanceiraId = cat.Id;
        }

        if (cmd.CentroCustoId.HasValue)
        {
            var centro = await centroRepo.GetByIdAsync(cmd.EmpresaId, cmd.CentroCustoId.Value, ct);
            if (centro is null || !centro.Ativo)
                throw new UseCaseValidationException("Centro de custo invalido ou inativo.");
            conta.CentroCustoId = centro.Id;
        }
        if (cmd.FornecedorId.HasValue) conta.FornecedorId = cmd.FornecedorId.Value == Guid.Empty ? null : cmd.FornecedorId;

        conta.AlteradoEm = DateTime.UtcNow;
        try
        {
            await repo.UpdateAsync(conta, ct);
            await uow.CommitAsync();
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }
        return ContaPagarResult.De(conta);
    }
}
