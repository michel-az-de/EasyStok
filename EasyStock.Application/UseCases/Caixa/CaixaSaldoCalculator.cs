namespace EasyStock.Application.UseCases.Caixa;

/// <summary>
/// Fonte ÚNICA do "saldo esperado do caixa" (fórmula + janela cross-day). Consumido pela
/// tela /caixa (ObterCaixaDiaUseCase) E pelo card "CAIXA" do Dashboard / Visão Geral do
/// tenant no Admin (AnalyticsRepository.ResumoDia), eliminando a divergência em que o
/// Dashboard derivava o saldo SEM somar as Vendas do período — mostrava saldo − vendas,
/// enquanto /caixa somava (diferença = total de vendas do dia).
///
/// Fórmula canônica:
///   saldoEsperado = saldoInicial(abertura) + vendas + pagamentosPedidos + entradas − saídas
///
/// Janela: dia civil BRT (<see cref="HorarioBrasil.JanelaDiaUtc(DateOnly)"/>). Resolução
/// cross-day (issue #596): se o dia consultado é hoje e não teve abertura/fechamento próprios
/// mas existe uma sessão aberta de um dia anterior, agrega [meia-noite BRT do dia da abertura,
/// fim de hoje) — o que entrou no dia da abertura antes de abrir também é da sessão (#1630-3).
///
/// Dia fechado (#1630-2): os totais e o saldo são os do <see cref="FechamentoCaixa"/>, o retrato
/// gravado ao fechar. O que entrou depois não reescreve o dia: vai em
/// <see cref="CaixaSaldoBreakdown.PosFechamento"/> (recálculo ao vivo menos o saldo fechado).
/// </summary>
public interface ICaixaSaldoCalculator
{
    Task<CaixaSaldoBreakdown> CalcularAsync(Guid empresaId, DateOnly data, Guid? lojaId = null);

    /// <summary>
    /// Vendas e pagamentos de pedido do intervalo como linhas para a lista "Movimentos do dia"
    /// (BUG-5). Use a janela do breakdown (<see cref="CaixaSaldoBreakdown.JanelaInicioUtc"/>..
    /// <see cref="CaixaSaldoBreakdown.JanelaFimUtc"/>) para a soma das linhas casar com o saldo.
    /// Só o /caixa precisa disto — o Dashboard usa apenas o total via <see cref="CalcularAsync"/>.
    /// </summary>
    Task<IReadOnlyList<CaixaLinhaExtraResult>> GetLinhasExtrasAsync(
        Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null);
}

/// <summary>
/// Decomposição do saldo do caixa de um dia + a janela (instante UTC) e os movimentos da
/// sessão. <see cref="JanelaInicioUtc"/>/<see cref="JanelaFimUtc"/> delimitam o intervalo
/// usado para somar vendas/pagamentos (mesma base usada para listá-los no /caixa — BUG-5).
/// <see cref="PosFechamento"/>: no dia fechado, o que mudou depois de fechar (#1630-2).
/// </summary>
public sealed record CaixaSaldoBreakdown(
    DateOnly Data,
    decimal SaldoInicial,
    decimal TotalVendas,
    decimal TotalPagamentosPedidos,
    decimal TotalEntradas,
    decimal TotalSaidas,
    decimal SaldoEsperado,
    bool Aberto,
    bool Fechado,
    bool AberturaPendenteCrossDay,
    DateOnly? AbertoDesde,
    DateTime JanelaInicioUtc,
    DateTime JanelaFimUtc,
    IReadOnlyList<MovimentoCaixa> Movimentos,
    FechamentoCaixa? Fechamento,
    decimal PosFechamento = 0m);

public sealed class CaixaSaldoCalculator(ICaixaRepository repo) : ICaixaSaldoCalculator
{
    public async Task<CaixaSaldoBreakdown> CalcularAsync(Guid empresaId, DateOnly data, Guid? lojaId = null)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);

        var fechamento = await repo.GetFechamentoDoDiaAsync(empresaId, data, lojaId);
        var fechado = fechamento != null;

        var movimentos = await repo.GetMovimentosDoDiaAsync(empresaId, data, lojaId);
        var movList = movimentos.ToList();
        var aberturaNoDia = movList.Any(m => m.Tipo == "abertura");

        var (iniDia, fimDia) = HorarioBrasil.JanelaDiaUtc(data);
        var janelaInicio = iniDia;
        var aberturaPendenteCrossDay = false;
        DateOnly? abertoDesde = null;

        // Sessão aberta de um dia anterior, ainda não fechada: só resolve para HOJE.
        if (!aberturaNoDia && !fechado && data == HorarioBrasil.Hoje())
        {
            var aberturaPendente = await repo.GetAberturaPendenteAsync(empresaId, lojaId);
            if (aberturaPendente != null)
            {
                aberturaPendenteCrossDay = true;
                var diaDaAbertura = HorarioBrasil.DataOperacional(aberturaPendente.DataMovimento);
                abertoDesde = diaDaAbertura;
                // #1630-3: a sessão é o dia operacional inteiro da abertura até o fim de hoje (ADR-0032:
                // instante real pela janela de Brasília). Começar no instante da abertura deixava de
                // fora a entrega das 9h de um caixa aberto às 11h.
                janelaInicio = HorarioBrasil.JanelaDiaUtc(diaDaAbertura).IniUtc;
                movList = (await repo.GetMovimentosNoIntervaloAsync(
                    empresaId, janelaInicio, fimDia, lojaId)).ToList();
            }
        }

        decimal saldoInicial = movList.Where(m => m.Tipo == "abertura").Sum(m => m.Valor);
        decimal totalEntradas = movList.Where(m => m.Tipo == "entrada").Sum(m => m.Valor);
        decimal totalSaidas   = movList.Where(m => m.Tipo == "saida").Sum(m => m.Valor);

        decimal totalVendas;
        decimal totalPagamentosPedidos;
        if (aberturaPendenteCrossDay)
        {
            totalVendas = await repo.GetTotalVendasNoIntervaloAsync(empresaId, janelaInicio, fimDia, lojaId);
            totalPagamentosPedidos = await repo.GetTotalPagamentosPedidosNoIntervaloAsync(empresaId, janelaInicio, fimDia, lojaId);
        }
        else
        {
            totalVendas = await repo.GetTotalVendasDoDiaAsync(empresaId, data, lojaId);
            totalPagamentosPedidos = await repo.GetTotalPagamentosPedidosDoDiaAsync(empresaId, data, lojaId);
        }

        var saldoEsperado = saldoInicial + totalVendas + totalPagamentosPedidos + totalEntradas - totalSaidas;
        var aberto = aberturaNoDia || aberturaPendenteCrossDay;

        if (fechamento != null)
        {
            // #1630-2: o dia fechado é o número do fechamento; Pix que entra depois de fechar
            // aparece à parte, sem reescrever o fechado.
            return new CaixaSaldoBreakdown(
                data, fechamento.SaldoInicial, fechamento.TotalVendas, fechamento.TotalPagamentosPedidos,
                fechamento.TotalEntradasExtras, fechamento.TotalSaidasExtras, fechamento.SaldoFinal,
                aberto, fechado, aberturaPendenteCrossDay, abertoDesde, janelaInicio, fimDia, movList, fechamento,
                PosFechamento: saldoEsperado - fechamento.SaldoFinal);
        }

        return new CaixaSaldoBreakdown(
            data, saldoInicial, totalVendas, totalPagamentosPedidos, totalEntradas, totalSaidas,
            saldoEsperado, aberto, fechado, aberturaPendenteCrossDay, abertoDesde,
            janelaInicio, fimDia, movList, fechamento);
    }

    public async Task<IReadOnlyList<CaixaLinhaExtraResult>> GetLinhasExtrasAsync(
        Guid empresaId, DateTime iniUtc, DateTime fimUtc, Guid? lojaId = null)
    {
        var vendas = await repo.GetVendasNoIntervaloAsync(empresaId, iniUtc, fimUtc, lojaId)
                     ?? (IReadOnlyList<Venda>)[];
        var pagamentos = await repo.GetPagamentosPedidosListaNoIntervaloAsync(empresaId, iniUtc, fimUtc, lojaId)
                         ?? (IReadOnlyList<PedidoPagamento>)[];
        var estornos = await repo.GetVendasEstornadasNoIntervaloAsync(empresaId, iniUtc, fimUtc, lojaId)
                       ?? (IReadOnlyList<VendaEstornadaCaixa>)[];

        var linhas = new List<CaixaLinhaExtraResult>(vendas.Count + pagamentos.Count + estornos.Count);
        linhas.AddRange(vendas.Select(v => new CaixaLinhaExtraResult(
            v.DataVenda, "venda", v.ValorTotal == null ? 0m : v.ValorTotal.Valor,
            v.Observacoes, v.FormaPagamentoPrincipal, "Venda")));
        linhas.AddRange(pagamentos.Select(p => new CaixaLinhaExtraResult(
            p.PagoEm, "pagamento", p.Valor, "Pagamento de pedido", p.Metodo, "Pedido")));
        // #1630-4: o total do dia desconta a venda estornada; a linha negativa na forma da venda faz
        // a soma por forma bater com ele (e o dinheiro devolvido sair da gaveta).
        linhas.AddRange(estornos.Select(e => new CaixaLinhaExtraResult(
            e.EstornadaEm, "estorno", -e.Valor, "Estorno de venda", e.FormaPagamento, "Venda")));

        return linhas.OrderBy(l => l.Hora).ToList();
    }
}
