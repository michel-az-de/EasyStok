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
public class ObterCaixaDiaUseCase(ICaixaSaldoCalculator calc, ICaixaRepository? repo = null)
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

        var fechResult = b.Fechamento == null ? null : FecharCaixa.FecharCaixaUseCase.Map(b.Fechamento);

        var dia = new CaixaDiaResult(
            b.Data, q.EmpresaId, q.LojaId,
            b.SaldoInicial, b.TotalVendas, b.TotalPagamentosPedidos, b.TotalEntradas, b.TotalSaidas,
            b.SaldoEsperado, b.Aberto, b.Fechado, fechResult,
            b.Movimentos.Select(AbrirCaixaUseCase.Map).ToList(),
            b.AberturaPendenteCrossDay, b.AbertoDesde, linhasExtras, b.PosFechamento,
            AberturaAConfirmar: b.Movimentos.Any(m => m.Tipo == "abertura" && m.EstornadoEm == null && m.ConfirmacaoPendente));

        return await ComGavetaEntreDiasAsync(dia, b, q);
    }

    /// <summary>
    /// #2084: os números de dinheiro que passam de um caixa para o outro.
    /// D1, caixa esquecido: a gaveta da sessão tem o dia da abertura (o que o fechamento retrata, ADR-0034) e o que
    /// entrou hoje (fica no caixa de hoje). Dias intermediários seguem o aviso do FecharCaixaUseCase.
    /// B4, dia fechado: o dinheiro que entrou depois de fechar, que as linhas do dia não mostram.
    /// </summary>
    private async Task<CaixaDiaResult> ComGavetaEntreDiasAsync(CaixaDiaResult dia, CaixaSaldoBreakdown b, ObterCaixaDiaQuery q)
    {
        if (b.AberturaPendenteCrossDay && b.AbertoDesde is { } diaDaAbertura)
        {
            var fimDoDiaDaAbertura = HorarioBrasil.JanelaDiaUtc(diaDaAbertura).FimUtc;
            var inicioDeHoje = HorarioBrasil.JanelaDiaUtc(b.Data).IniUtc;
            return dia with
            {
                DinheiroEsperadoNoFechamento = GavetaCaixa.DinheiroNaJanela(dia, null, fimDoDiaDaAbertura, comSaldoInicial: true),
                DinheiroEntrouDepois = GavetaCaixa.DinheiroNaJanela(dia, inicioDeHoje, null, comSaldoInicial: false)
            };
        }

        if (b.Fechamento is not { } fechamento) return dia;
        var fimDoDia = HorarioBrasil.JanelaDiaUtc(b.Data).FimUtc;
        var linhasDepois = fechamento.FechadoEm < fimDoDia
            ? await calc.GetLinhasExtrasAsync(q.EmpresaId, fechamento.FechadoEm, fimDoDia, q.LojaId)
            : [];
        // Os movimentos depois de fechar vêm do banco, não do retrato: com o corte do #2082 (D9) o dia fechado só
        // lista o que é até o FechadoEm; sem ele, a lista traz tudo. Nos dois casos, entra aqui só o que é depois.
        var movimentosDepois = repo is null || fechamento.FechadoEm >= fimDoDia
            ? []
            : (await repo.GetMovimentosNoIntervaloAsync(q.EmpresaId, fechamento.FechadoEm, fimDoDia, q.LojaId))
                .Where(m => m.DataMovimento > fechamento.FechadoEm)
                .Select(AbrirCaixaUseCase.Map).ToList();
        var depois = dia with { Movimentos = movimentosDepois, LinhasExtras = linhasDepois };
        var posFechamento = GavetaCaixa.DinheiroNaJanela(depois, null, null, comSaldoInicial: false);
        return dia with
        {
            DinheiroPosFechamento = posFechamento,
            Fechamento = dia.Fechamento! with { DinheiroPosFechamento = posFechamento }
        };
    }
}
