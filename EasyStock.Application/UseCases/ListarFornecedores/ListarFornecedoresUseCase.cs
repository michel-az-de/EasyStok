using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Fornecedor;

namespace EasyStock.Application.UseCases.ListarFornecedores;

public sealed record ListarFornecedoresQuery(Guid EmpresaId, int Page = 1, int PageSize = 20, bool? Ativo = null, string? Search = null);

public class ListarFornecedoresUseCase(IFornecedorRepository fornecedorRepository)
{
    public async Task<(IEnumerable<FornecedorResult> Fornecedores, int Total)> ExecuteAsync(ListarFornecedoresQuery query)
    {
        var (fornecedores, total) = await fornecedorRepository.GetByEmpresaAsync(query.EmpresaId, query.Page, query.PageSize, query.Ativo, query.Search);
        return (fornecedores.Select(f => new FornecedorResult(
            f.Id,
            f.EmpresaId,
            f.Nome,
            f.Ativo,
            f.Documento,
            f.Email,
            f.Telefone,
            f.Contato,
            f.Categoria,
            f.Tipo,
            f.LeadTimeEstimadoDias,
            f.LeadTimeRealMedioDias,
            f.SiteUrl,
            f.PedidoMinimo,
            f.FretePadrao,
            f.Observacoes)), total);
    }
}
