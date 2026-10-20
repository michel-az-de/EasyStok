using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;

namespace EasyStock.Application.UseCases.ListarFechamentosCaixa;

public sealed record ListarFechamentosCaixaQuery(
    Guid EmpresaId,
    int Page = 1,
    int PageSize = 30,
    DateOnly? Desde = null,
    DateOnly? Ate = null);

public sealed record ListarFechamentosCaixaResult(
    IReadOnlyList<FechamentoCaixaResult> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>
/// Histórico de fechamentos. #1776-11: cada fechamento informa também o dinheiro que ficou na gaveta
/// (<see cref="GavetaCaixa.DinheiroNaGaveta"/> sobre o dia do fechamento), porque o <c>SaldoFinal</c> soma Pix e cartão e a
/// abertura seguinte sugeria esse total como dinheiro contado. Custa a leitura do dia por fechamento da página.
/// </summary>
public class ListarFechamentosCaixaUseCase(ICaixaRepository repo, ObterCaixaDiaUseCase? caixaDia = null)
{
    public async Task<ListarFechamentosCaixaResult> ExecuteAsync(ListarFechamentosCaixaQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 200);

        var (items, total) = await repo.ListFechamentosAsync(q.EmpresaId, page, size, q.Desde, q.Ate);

        var resultados = new List<FechamentoCaixaResult>();
        foreach (var fechamento in items)
            resultados.Add(await ComDinheiroNaGavetaAsync(q.EmpresaId, fechamento));

        return new ListarFechamentosCaixaResult(resultados, total, page, size);
    }

    private async Task<FechamentoCaixaResult> ComDinheiroNaGavetaAsync(Guid empresaId, FechamentoCaixa fechamento)
    {
        var resultado = FecharCaixaUseCase.Map(fechamento);
        if (caixaDia is null) return resultado;
        var dia = await caixaDia.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, fechamento.Data, fechamento.LojaId));
        return resultado with { DinheiroNaGaveta = GavetaCaixa.DinheiroNaGaveta(dia) };
    }
}
