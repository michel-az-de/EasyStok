using EasyStock.Application.UseCases.Loja;

namespace EasyStock.Application.UseCases.ListarLojas;

public sealed record ListarLojasQuery(Guid EmpresaId);

public class ListarLojasUseCase(ILojaRepository lojaRepository)
{
    public async Task<IEnumerable<LojaResult>> ExecuteAsync(ListarLojasQuery query)
    {
        var lojas = await lojaRepository.GetByEmpresaAsync(query.EmpresaId);
        return lojas.Select(l => new LojaResult(l.Id, l.EmpresaId, l.Nome, l.Ativa));
    }
}
