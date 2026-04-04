using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Fornecedor;

namespace EasyStock.Application.UseCases.ListarFornecedores;

public sealed record ListarFornecedoresQuery(Guid EmpresaId, int Page = 1, int PageSize = 20);

public class ListarFornecedoresUseCase(IFornecedorRepository fornecedorRepository)
{
    public async Task<(IEnumerable<FornecedorResult> Fornecedores, int Total)> ExecuteAsync(ListarFornecedoresQuery query)
    {
        var (fornecedores, total) = await fornecedorRepository.GetByEmpresaAsync(query.EmpresaId, query.Page, query.PageSize);
        return (fornecedores.Select(f => new FornecedorResult(f.Id, f.EmpresaId, f.Nome, f.Ativo)), total);
    }
}
