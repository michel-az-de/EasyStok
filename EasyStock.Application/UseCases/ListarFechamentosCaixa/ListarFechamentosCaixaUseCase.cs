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

    /// <summary>
    /// #2084 (B1): o dinheiro que a abertura seguinte herda, pela mesma regra que a abertura manual sugere
    /// (<c>gavetaDoFechamento</c> no console lê <see cref="FechamentoCaixaResult.DinheiroNaGaveta"/> do último
    /// fechamento). A2: da própria loja; sem fechamento dela, o do caixa da empresa sem loja (tenant de uma loja só
    /// que fecha sem loja). Nulo sem fechamento anterior.
    /// </summary>
    public async Task<(DateOnly Data, decimal Dinheiro)?> GavetaDoUltimoFechamentoAsync(Guid empresaId, Guid? lojaId)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var ultimo = await repo.GetUltimoFechamentoDaLojaAsync(empresaId, lojaId)
                     ?? (lojaId is null ? null : await repo.GetUltimoFechamentoDaLojaAsync(empresaId, null));
        if (ultimo is null) return null;
        var resultado = await ComDinheiroNaGavetaAsync(empresaId, ultimo);
        return resultado.DinheiroNaGaveta is { } dinheiro ? (resultado.Data, dinheiro) : null;
    }

    /// <summary>
    /// A gaveta que fica para a abertura seguinte:
    ///   B3, quem contou: o contado, menos o que entrou hoje num caixa esquecido (D1: fica no caixa de hoje);
    ///   quem não contou: o calculado do dia até o fechamento;
    ///   B4, mais o dinheiro que entrou no mesmo dia depois de fechar.
    /// </summary>
    private async Task<FechamentoCaixaResult> ComDinheiroNaGavetaAsync(Guid empresaId, FechamentoCaixa fechamento)
    {
        var resultado = FecharCaixaUseCase.Map(fechamento);
        if (caixaDia is null) return resultado;
        var dia = await caixaDia.ExecuteAsync(new ObterCaixaDiaQuery(empresaId, fechamento.Data, fechamento.LojaId));
        var ateFechar = fechamento.ValorContado is { } contado
            ? contado - (fechamento.DinheiroEntrouDepois ?? 0m)
            // até o instante do fechamento, inclusive (o mesmo corte do retrato do dia fechado, #2082 D9)
            : GavetaCaixa.DinheiroNaJanela(dia, null, fechamento.FechadoEm.AddTicks(1), comSaldoInicial: true);
        return resultado with
        {
            DinheiroNaGaveta = ateFechar + dia.DinheiroPosFechamento,
            DinheiroPosFechamento = dia.DinheiroPosFechamento
        };
    }
}
