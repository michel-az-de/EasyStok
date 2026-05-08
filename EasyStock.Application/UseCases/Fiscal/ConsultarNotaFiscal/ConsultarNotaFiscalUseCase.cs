using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Fiscal.ConsultarNotaFiscal;

public sealed class ConsultarNotaFiscalUseCase(INotaFiscalRepository repo)
    : IUseCase<ConsultarNotaFiscalQuery, ConsultarNotaFiscalResult>
{
    public async Task<ConsultarNotaFiscalResult> ExecuteAsync(ConsultarNotaFiscalQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);

        var (items, total) = await repo.ListarAsync(
            q.EmpresaId, q.LojaId, q.DesdeUtc, q.AteUtc, q.Status, q.ChaveAcesso,
            q.Pagina, q.TamanhoPagina, CancellationToken.None);

        var totalPaginas = (int)Math.Ceiling((double)total / Math.Max(1, q.TamanhoPagina));
        return new ConsultarNotaFiscalResult(
            Items: items.Select(NotaFiscalListItem.From).ToList(),
            Pagina: q.Pagina,
            TamanhoPagina: q.TamanhoPagina,
            TotalItens: total,
            TotalPaginas: totalPaginas);
    }
}
