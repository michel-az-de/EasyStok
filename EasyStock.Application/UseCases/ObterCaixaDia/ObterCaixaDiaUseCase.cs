using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.Caixa;
using EasyStock.Application.UseCases.FecharCaixa;

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

        var fechResult = b.Fechamento == null ? null : FecharCaixaUseCase.Map(b.Fechamento);

        // F14 (#1244): o console mostra o saldo do atendimento (vendas do PDV à parte) e o resumo
        // por método. Aditivos: SaldoEsperado acima continua o de sempre para a PWA e o Web.
        var saldoAtendimento = b.SaldoInicial + b.TotalPagamentosPedidos + b.TotalEntradas - b.TotalSaidas;
        var porMetodo = ResumoPorMetodo(b.Movimentos, linhasExtras);

        return new CaixaDiaResult(
            b.Data, q.EmpresaId, q.LojaId,
            b.SaldoInicial, b.TotalVendas, b.TotalPagamentosPedidos, b.TotalEntradas, b.TotalSaidas,
            b.SaldoEsperado, b.Aberto, b.Fechado, fechResult,
            b.Movimentos.Select(AbrirCaixaUseCase.Map).ToList(),
            b.AberturaPendenteCrossDay, b.AbertoDesde, linhasExtras,
            saldoAtendimento, porMetodo);
    }

    private static readonly string[] OrdemMetodos = ["pix", "dinheiro", "credito", "debito", "transferencia", "outro"];

    /// <summary>Pagamentos de pedido + entradas − saídas, por método (a abertura não entra).
    /// Método vazio ou desconhecido cai em "outro"; método que somou zero some da lista.</summary>
    internal static IReadOnlyList<CaixaMetodoResult> ResumoPorMetodo(
        IEnumerable<MovimentoCaixa> movimentos, IEnumerable<CaixaLinhaExtraResult> linhas)
    {
        static string Chave(string? metodo)
        {
            var m = metodo?.Trim().ToLowerInvariant();
            return m is not null && OrdemMetodos.Contains(m) ? m : "outro";
        }

        var parcelas = linhas.Where(l => l.Tipo == "pagamento").Select(l => (Chave(l.Metodo), l.Valor))
            .Concat(movimentos.Where(m => m.Tipo == "entrada").Select(m => (Chave(m.Metodo), m.Valor)))
            .Concat(movimentos.Where(m => m.Tipo == "saida").Select(m => (Chave(m.Metodo), -m.Valor)));

        return parcelas
            .GroupBy(p => p.Item1)
            .Select(g => new CaixaMetodoResult(g.Key, g.Sum(p => p.Item2)))
            .Where(r => r.Valor != 0m)
            .OrderBy(r => Array.IndexOf(OrdemMetodos, r.Metodo))
            .ToList();
    }
}
