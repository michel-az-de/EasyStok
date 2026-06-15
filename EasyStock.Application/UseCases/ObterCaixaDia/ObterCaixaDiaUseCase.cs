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
public class ObterCaixaDiaUseCase(ICaixaRepository repo)
{
    public async Task<CaixaDiaResult> ExecuteAsync(ObterCaixaDiaQuery q)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);

        var fechamento = await repo.GetFechamentoDoDiaAsync(q.EmpresaId, q.Data, q.LojaId);
        var fechado = fechamento != null;

        var movimentos = await repo.GetMovimentosDoDiaAsync(q.EmpresaId, q.Data, q.LojaId);
        var movList = movimentos.ToList();
        var aberturaNoDia = movList.Any(m => m.Tipo == "abertura");

        var (iniDia, fimDia) = HorarioBrasil.JanelaDiaUtc(q.Data);
        var aberturaPendenteCrossDay = false;
        DateOnly? abertoDesde = null;

        // Sessão aberta de um dia anterior, ainda não fechada: só resolve para HOJE.
        if (!aberturaNoDia && !fechado && q.Data == HorarioBrasil.Hoje())
        {
            var aberturaPendente = await repo.GetAberturaPendenteAsync(q.EmpresaId, q.LojaId);
            if (aberturaPendente != null)
            {
                aberturaPendenteCrossDay = true;
                abertoDesde = HorarioBrasil.DataOperacional(aberturaPendente.DataMovimento);
                // Re-agrega os movimentos da sessão: da abertura pendente até o fim de hoje.
                movList = (await repo.GetMovimentosNoIntervaloAsync(
                    q.EmpresaId, aberturaPendente.DataMovimento, fimDia, q.LojaId)).ToList();
            }
        }

        decimal saldoInicial = movList.Where(m => m.Tipo == "abertura").Sum(m => m.Valor);
        decimal totalEntradas = movList.Where(m => m.Tipo == "entrada").Sum(m => m.Valor);
        decimal totalSaidas   = movList.Where(m => m.Tipo == "saida").Sum(m => m.Valor);

        decimal totalVendas;
        decimal totalPagamentosPedidos;
        if (aberturaPendenteCrossDay)
        {
            var iniAgg = movList.Where(m => m.Tipo == "abertura")
                                .Select(m => m.DataMovimento)
                                .DefaultIfEmpty(iniDia)
                                .Min();
            totalVendas = await repo.GetTotalVendasNoIntervaloAsync(q.EmpresaId, iniAgg, fimDia, q.LojaId);
            totalPagamentosPedidos = await repo.GetTotalPagamentosPedidosNoIntervaloAsync(q.EmpresaId, iniAgg, fimDia, q.LojaId);
        }
        else
        {
            totalVendas = await repo.GetTotalVendasDoDiaAsync(q.EmpresaId, q.Data, q.LojaId);
            totalPagamentosPedidos = await repo.GetTotalPagamentosPedidosDoDiaAsync(q.EmpresaId, q.Data, q.LojaId);
        }

        var saldoEsperado = saldoInicial + totalVendas + totalPagamentosPedidos + totalEntradas - totalSaidas;
        var aberto = aberturaNoDia || aberturaPendenteCrossDay;

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
            movList.Select(AbrirCaixaUseCase.Map).ToList(),
            aberturaPendenteCrossDay, abertoDesde);
    }
}
