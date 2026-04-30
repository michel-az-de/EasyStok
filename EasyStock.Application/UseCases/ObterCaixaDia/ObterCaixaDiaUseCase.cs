using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.ObterCaixaDia;

public sealed record ObterCaixaDiaQuery(Guid EmpresaId, DateOnly Data, Guid? LojaId = null);

/// <summary>
/// Resumo consolidado do caixa de um dia, mesmo que ainda não fechado.
/// Agrega: saldo inicial (movimento "abertura") + vendas + pagamentos de
/// pedidos + entradas/saídas extras = saldo esperado em caixa.
/// </summary>
public class ObterCaixaDiaUseCase(ICaixaRepository repo)
{
    public async Task<CaixaDiaResult> ExecuteAsync(ObterCaixaDiaQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);

        var movimentos = await repo.GetMovimentosDoDiaAsync(q.EmpresaId, q.Data, q.LojaId);
        var movList = movimentos.ToList();

        decimal saldoInicial = movList.Where(m => m.Tipo == "abertura").Sum(m => m.Valor);
        decimal totalEntradas = movList.Where(m => m.Tipo == "entrada").Sum(m => m.Valor);
        decimal totalSaidas   = movList.Where(m => m.Tipo == "saida").Sum(m => m.Valor);

        var totalVendas = await repo.GetTotalVendasDoDiaAsync(q.EmpresaId, q.Data, q.LojaId);
        var totalPagamentosPedidos = await repo.GetTotalPagamentosPedidosDoDiaAsync(q.EmpresaId, q.Data, q.LojaId);

        var saldoEsperado = saldoInicial + totalVendas + totalPagamentosPedidos + totalEntradas - totalSaidas;

        var fechamento = await repo.GetFechamentoDoDiaAsync(q.EmpresaId, q.Data, q.LojaId);
        var aberto = movList.Any(m => m.Tipo == "abertura");
        var fechado = fechamento != null;

        FechamentoCaixaResult? fechResult = fechamento == null ? null : new FechamentoCaixaResult(
            fechamento.Id, fechamento.EmpresaId, fechamento.LojaId, fechamento.Data,
            fechamento.SaldoInicial, fechamento.TotalVendas, fechamento.TotalPagamentosPedidos,
            fechamento.TotalEntradasExtras, fechamento.TotalSaidasExtras, fechamento.SaldoFinal,
            fechamento.FechadoPorUserId, fechamento.FechadoPorNome, fechamento.Observacoes,
            fechamento.FechadoEm);

        return new CaixaDiaResult(
            q.Data, q.EmpresaId, q.LojaId,
            saldoInicial, totalVendas, totalPagamentosPedidos, totalEntradas, totalSaidas,
            saldoEsperado, aberto, fechado, fechResult,
            movList.Select(AbrirCaixaUseCase.Map).ToList());
    }
}
