using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Infra.Postgre.Repositories
{
    /// <summary>
    /// Partial: Resumo do dia (Pulso de hoje) — pedidos entregues, faturamento,
    /// caixa aberta/fechada/saldo, Pix do dia, onboarding flag.
    /// Extraido do god-AnalyticsRepository (F12).
    /// </summary>
    public sealed partial class AnalyticsRepository
    {
        public async Task<ResumoDia> GetResumoDiaAsync(Guid empresaId, Guid? lojaId = null)
        {
            // JanelaDiaUtc: meia-noite BRT como UTC (03:00Z). Antes UtcNow.Date
            // (00:00Z) fazia o bucket resetar as 21h BRT (janela 21h-23h59).
            var (hojeIni, hojeFim) = HorarioBrasil.JanelaDiaUtc();
            var hojeBrt = HorarioBrasil.Hoje(); // data civil BRT para cache key

            // Cache key usa a data BRT — invalida naturalmente ao virar a meia-noite de Brasilia.
            var cacheKey = $"analytics:resumo-dia:{empresaId}:{lojaId?.ToString() ?? "all"}:{hojeBrt:yyyy-MM-dd}";
            var cached = await GetCachedAsync<ResumoDia>(cacheKey);
            if (cached is not null) return cached;

            // ── Onboarding ───────────────────────────────────────────────
            // Empresa.OnboardingCompleto controla banner "Termine o setup" no dashboard.
            var onboardingCompleto = await dbContext.Empresas.AsNoTracking()
                .Where(e => e.Id == empresaId)
                .Select(e => (bool?)e.OnboardingCompleto)
                .FirstOrDefaultAsync() ?? true;

            // ── Caixa: ultimo evento abertura/fechamento (resolve cross-day) ─
            // Se o operador abriu ontem 23h e nao fechou, ainda esta aberto.
            // Saldo acumula desde a ultima abertura (pode atravessar o dia).
            var ultimoEventoCaixa = await dbContext.MovimentosCaixa.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId
                         && (lojaId == null || m.LojaId == lojaId)
                         && m.EstornadoEm == null
                         && (m.Tipo == "abertura" || m.Tipo == "fechamento"))
                .OrderByDescending(m => m.DataMovimento)
                .Select(m => new { m.Tipo, m.DataMovimento })
                .FirstOrDefaultAsync();

            DateTime? aberturaEm = null;
            bool caixaAberta = false;
            bool caixaFechada = false;

            if (ultimoEventoCaixa != null)
            {
                if (ultimoEventoCaixa.Tipo == "abertura")
                {
                    caixaAberta = true;
                    aberturaEm = ultimoEventoCaixa.DataMovimento;
                }
                else if (ultimoEventoCaixa.DataMovimento >= hojeIni)
                {
                    caixaFechada = true;
                }
                // Senao: ultimo evento foi fechamento de outro dia => "sem caixa hoje"
            }

            // Saldo: desde a abertura (cross-day) se aberta, OU desde hoje 0h
            var saldoInicio = aberturaEm ?? hojeIni;

            var movsSaldo = await dbContext.MovimentosCaixa.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId
                         && (lojaId == null || m.LojaId == lojaId)
                         && m.DataMovimento >= saldoInicio && m.DataMovimento < hojeFim
                         && m.EstornadoEm == null)
                .Select(m => new { m.Tipo, m.Valor })
                .ToListAsync();

            decimal saldoCaixa = 0m;
            foreach (var m in movsSaldo)
            {
                saldoCaixa += m.Tipo switch
                {
                    "abertura" => +m.Valor,
                    "entrada" => +m.Valor,
                    "saida" => -m.Valor,
                    _ => 0m
                };
            }

            // ── Pedidos ────────────────────────────────────────────────────
            // Entregues hoje (= vendas consolidadas do dia)
            var entreguesHoje = await dbContext.Pedidos.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId
                         && (lojaId == null || p.LojaId == lojaId)
                         && p.EntreguEm != null
                         && p.EntreguEm >= hojeIni && p.EntreguEm < hojeFim)
                .Select(p => p.Total)
                .ToListAsync();
            var pedidosEntreguesHoje = entreguesHoje.Count;
            var faturamentoHoje = entreguesHoje.Sum(t => (decimal)t);
            var ticketMedioHoje = pedidosEntreguesHoje == 0
                ? 0m
                : Math.Round(faturamentoHoje / pedidosEntreguesHoje, 2);

            // Pendentes (qualquer status pre-entrega)
            var pendentes = await dbContext.Pedidos.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId
                         && (lojaId == null || p.LojaId == lojaId)
                         && p.Status != "entregue" && p.Status != "cancelado")
                .Select(p => p.Total)
                .ToListAsync();
            var pedidosPendentes = pendentes.Count;
            var valorPedidosPendentes = pendentes.Sum(t => (decimal)t);

            // ── Pagamentos: somam ao saldo + Pix do dia ─────────────────
            // PedidoPagamento nao e DbSet — acessa via navigation Pedido.Pagamentos.
            var pagamentosNoSaldo = await dbContext.Pedidos.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId
                         && (lojaId == null || p.LojaId == lojaId))
                .SelectMany(p => p.Pagamentos)
                .Where(pp => pp.PagoEm >= saldoInicio && pp.PagoEm < hojeFim)
                .Select(pp => new { pp.PagoEm, pp.Metodo, pp.Valor })
                .ToListAsync();
            saldoCaixa += pagamentosNoSaldo.Sum(p => p.Valor);

            // BUG-004: vendas diretas (POS/NFC-e, tabela Vendas) tambem compoem o caixa. O card do
            // dashboard somava so os pagamentos de pedidos e ignorava as Vendas, divergindo da tela
            // de Caixa, cujo saldoEsperado = saldoInicial + totalVendas + totalPagamentosPedidos +
            // entradas - saidas (ObterCaixaDiaUseCase). Mesma janela [saldoInicio, hojeFim) e mesma
            // regra de ValorTotal de CaixaRepository.GetTotalVendasNoIntervaloAsync — fonte unica.
            var vendasNoSaldo = await dbContext.Vendas.AsNoTracking()
                .Where(v => v.EmpresaId == empresaId
                         && (lojaId == null || v.LojaId == lojaId)
                         && v.DataVenda >= saldoInicio && v.DataVenda < hojeFim)
                .ToListAsync();
            saldoCaixa += vendasNoSaldo.Sum(v => v.ValorTotal == null ? 0m : v.ValorTotal.Valor);

            // Pix recebidos hoje — SO PedidoPagamento (decisao explicita pra evitar
            // double-count com MovimentoCaixa.Metodo=pix). Considera apenas hoje
            // (nao cross-day): "Pix de hoje" e metrica de dia, nao de caixa.
            var pixHoje = pagamentosNoSaldo
                .Where(p => p.Metodo == "pix" && p.PagoEm >= hojeIni && p.PagoEm < hojeFim)
                .ToList();
            var pixCount = pixHoje.Count;
            var pixValor = pixHoje.Sum(p => p.Valor);

            // ── Onboarding checklist counts (1.1-B) ──────────────────────────
            // Conta linhas reais do tenant (sem janela de data). A exclusao dos
            // Ids do conjunto de demonstracao entra junto com a feature de demo (1.3).
            var categoriasCount = await dbContext.Categorias.AsNoTracking()
                .CountAsync(c => c.EmpresaId == empresaId);
            var entradasCount = await dbContext.MovimentacoesEstoque.AsNoTracking()
                .CountAsync(m => m.EmpresaId == empresaId
                              && m.Tipo == TipoMovimentacaoEstoque.Entrada);

            var resumo = new ResumoDia(
                pedidosEntreguesHoje,
                faturamentoHoje,
                ticketMedioHoje,
                pedidosPendentes,
                valorPedidosPendentes,
                caixaAberta,
                caixaFechada,
                saldoCaixa,
                pixCount,
                pixValor,
                onboardingCompleto,
                categoriasCount,
                entradasCount);

            await SetCachedAsync(cacheKey, resumo, ResumoDiaTtl);
            return resumo;
        }
    }
}
