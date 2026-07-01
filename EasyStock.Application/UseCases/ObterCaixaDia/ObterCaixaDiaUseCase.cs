using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;

namespace EasyStock.Application.UseCases.ObterCaixaDia;

public sealed record ObterCaixaDiaQuery(Guid EmpresaId, DateOnly Data, Guid? LojaId = null);

/// <summary>
/// Resumo consolidado do caixa de um dia, mesmo que ainda não fechado.
/// Agrega: saldo inicial (movimento "abertura") + vendas + pagamentos de
/// pedidos + entradas/saídas extras = saldo esperado em caixa.
///
/// Resolução cross-day (issue #596, opção B): se o dia consultado é hoje e não teve
/// abertura nem fechamento próprios, mas existe uma sessão aberta de um dia anterior
/// (última abertura sem fechamento posterior), o caixa é exibido como aberto desde a
/// data dessa abertura e os totais agregam a sessão inteira [aberturaEm, fim do dia).
/// Espelha a fonte de verdade do card do dashboard (AnalyticsRepository.ResumoDia),
/// eliminando a divergência "dashboard diz aberto / caixa diz não aberto". Dias
/// históricos mantêm a semântica estrita do dia civil (não distorce relatório retroativo).
/// </summary>
public class ObterCaixaDiaUseCase(ICaixaSaldoCalculator calc)
{
    public async Task<CaixaDiaResult> ExecuteAsync(ObterCaixaDiaQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);

        // Saldo + janela + movimentos vêm da fonte ÚNICA (CaixaSaldoCalculator), a mesma que
        // o Dashboard/Admin consomem — garante que /caixa e o card "CAIXA" nunca divirjam.
        var b = await calc.CalcularAsync(q.EmpresaId, q.Data, q.LojaId);

        // Vendas e pagamentos de pedido do mesmo intervalo, como linhas, para a lista
        // "Movimentos do dia" reconciliar com o saldo (BUG-5): movimentos + linhas == saldo.
        var linhasExtras = await calc.GetLinhasExtrasAsync(q.EmpresaId, b.JanelaInicioUtc, b.JanelaFimUtc, q.LojaId);

        FechamentoCaixaResult? fechResult = b.Fechamento == null ? null : new FechamentoCaixaResult(
            b.Fechamento.Id, b.Fechamento.EmpresaId, b.Fechamento.LojaId, b.Fechamento.Data,
            b.Fechamento.SaldoInicial, b.Fechamento.TotalVendas, b.Fechamento.TotalPagamentosPedidos,
            b.Fechamento.TotalEntradasExtras, b.Fechamento.TotalSaidasExtras, b.Fechamento.SaldoFinal,
            b.Fechamento.FechadoPorUserId, b.Fechamento.FechadoPorNome, b.Fechamento.Observacoes,
            b.Fechamento.FechadoEm);

        return new CaixaDiaResult(
            b.Data, q.EmpresaId, q.LojaId,
            b.SaldoInicial, b.TotalVendas, b.TotalPagamentosPedidos, b.TotalEntradas, b.TotalSaidas,
            b.SaldoEsperado, b.Aberto, b.Fechado, fechResult,
            b.Movimentos.Select(AbrirCaixaUseCase.Map).ToList(),
            b.AberturaPendenteCrossDay, b.AbertoDesde, linhasExtras);
    }
}
