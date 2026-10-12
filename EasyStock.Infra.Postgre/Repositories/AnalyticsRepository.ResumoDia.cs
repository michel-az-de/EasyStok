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

            bool caixaAberta = false;
            bool caixaFechada = false;

            if (ultimoEventoCaixa != null)
            {
                if (ultimoEventoCaixa.Tipo == "abertura")
                {
                    caixaAberta = true;
                }
                else if (ultimoEventoCaixa.DataMovimento >= hojeIni)
                {
                    caixaFechada = true;
                }
                // Senao: ultimo evento foi fechamento de outro dia => "sem caixa hoje"
            }

            // Saldo esperado do caixa: fonte ÚNICA (CaixaSaldoCalculator), a MESMA consumida
            // pela tela /caixa. Antes este bloco somava abertura+entrada-saída (+pagamentos) e
            // OMITIA as Vendas do período, divergindo do /caixa pelo total de vendas (BUG-1).
            // issue 988: guarda o breakdown inteiro em vez de so o escalar. Os componentes ja
            // vinham calculados nesta MESMA chamada e eram descartados; o dashboard exibia um
            // saldo que o lojista nao conseguia explicar.
            var caixaBreakdown = await caixaSaldo.CalcularAsync(empresaId, hojeBrt, lojaId);
            decimal saldoCaixa = caixaBreakdown.SaldoEsperado;

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

            // Pix é uma métrica do dia civil, inclusive antes da abertura do caixa.
            // Contagem e soma ficam no PostgreSQL, sem materializar cada recebimento.
            var pixHoje = await dbContext.Pedidos.AsNoTracking()
                .Where(p => p.EmpresaId == empresaId
                         && (lojaId == null || p.LojaId == lojaId))
                .SelectMany(p => p.Pagamentos)
                .Where(pp => pp.Metodo == "pix" && pp.PagoEm >= hojeIni && pp.PagoEm < hojeFim)
                .GroupBy(pp => 1)
                .Select(g => new { Quantidade = g.Count(), Valor = g.Sum(pp => pp.Valor) })
                .FirstOrDefaultAsync();
            var pixCount = pixHoje?.Quantidade ?? 0;
            var pixValor = pixHoje?.Valor ?? 0m;

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
                entradasCount,
                caixaBreakdown.TotalVendas,
                caixaBreakdown.TotalPagamentosPedidos);

            await SetCachedAsync(cacheKey, resumo, ResumoDiaTtl);
            return resumo;
        }
    }
}
