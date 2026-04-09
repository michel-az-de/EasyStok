using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fornecedor;

public sealed record ObterFornecedorDetalheQuery(Guid EmpresaId, Guid FornecedorId);

public class ObterFornecedorDetalheUseCase(IFornecedorRepository fornecedorRepository)
{
    public async Task<FornecedorResult> ExecuteAsync(ObterFornecedorDetalheQuery query)
    {
        var fornecedor = await fornecedorRepository.GetByIdAsync(query.EmpresaId, query.FornecedorId);
        if (fornecedor is null)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        return new FornecedorResult(
            fornecedor.Id,
            fornecedor.EmpresaId,
            fornecedor.Nome,
            fornecedor.Ativo,
            fornecedor.Documento,
            fornecedor.Email,
            fornecedor.Telefone,
            fornecedor.Contato,
            fornecedor.Categoria,
            fornecedor.Tipo,
            fornecedor.LeadTimeEstimadoDias,
            fornecedor.LeadTimeRealMedioDias,
            fornecedor.SiteUrl,
            fornecedor.PedidoMinimo,
            fornecedor.FretePadrao,
            fornecedor.Observacoes);
    }
}
