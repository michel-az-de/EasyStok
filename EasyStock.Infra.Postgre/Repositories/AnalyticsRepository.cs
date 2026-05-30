using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Infra.Postgre.Repositories
{
    /// <summary>
    /// Implements aggregated analytics queries against PostgreSQL with optional Redis distributed cache (5â€“10 min TTL).
    /// Delegates to specialised query classes; store-intelligence methods live here.
    /// </summary>
    public sealed partial class AnalyticsRepository(EasyStockDbContext dbContext, IDistributedCache? cache = null)
        : IAnalyticsRepository
    {
        // â”€â”€ Specialised query objects â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private readonly DashboardAnalyticsQueries _dashboard = new(dbContext, cache);
        private readonly ReceitaAnalyticsQueries   _receita   = new(dbContext, cache);
        private readonly EstoqueAnalyticsQueries   _estoque   = new(dbContext, cache);

        // â”€â”€ Cache helpers (used only by store-intelligence methods below) â”€â”€â”€â”€

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private async Task<T?> GetCachedAsync<T>(string key)
        {
            if (cache is null) return default;
            var raw = await cache.GetStringAsync(key);
            return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw, JsonOptions);
        }

        private async Task SetCachedAsync<T>(string key, T value, TimeSpan ttl)
        {
            if (cache is null) return;
            var serialized = JsonSerializer.Serialize(value, JsonOptions);
            await cache.SetStringAsync(key, serialized, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }

        private static readonly TimeSpan ComparacaoTtl = TimeSpan.FromMinutes(5);
        // Resumo do dia muda quase em tempo real (cada pedido entregue altera faturamento).
        // TTL curto para nao mostrar dado defasado, mas o suficiente pra amortizar F5 frequente.
        private static readonly TimeSpan ResumoDiaTtl = TimeSpan.FromSeconds(30);

        // â”€â”€ Delegation â€” Dashboard â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
            => _dashboard.GetDashboardResumoAsync(empresaId, periodoDias, lojaId);

        public Task<IReadOnlyList<MovimentacaoResumo>> GetMovimentacoesResumoAsync(
            Guid empresaId, DateTime de, DateTime ate,
            TipoMovimentacaoEstoque? tipo = null, Guid? lojaId = null)
            => _dashboard.GetMovimentacoesResumoAsync(empresaId, de, ate, tipo, lojaId);

        // â”€â”€ Delegation â€” Dashboard Full â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public Task<DashboardKpis> GetDashboardKpisAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
            => _dashboard.GetDashboardKpisAsync(empresaId, de, ate, lojaId);

        public Task<EstoqueStatusDistribuicao> GetEstoqueStatusDistribuicaoAsync(Guid empresaId, Guid? lojaId = null)
            => _dashboard.GetEstoqueStatusDistribuicaoAsync(empresaId, lojaId);

        public Task<IReadOnlyList<PedidoPendenteResumo>> GetPedidosPendentesAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null, int pageSize = 50)
            => _dashboard.GetPedidosPendentesAsync(empresaId, periodoDias, lojaId, pageSize);

        public Task<int> GetEntreguesSemVendaCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
            => _dashboard.GetEntreguesSemVendaCountAsync(empresaId, periodoDias, lojaId);

        public Task<int> GetLotesFinalizadosCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
            => _dashboard.GetLotesFinalizadosCountAsync(empresaId, periodoDias, lojaId);

        public Task<int> GetClientesAtivosCountAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
            => _dashboard.GetClientesAtivosCountAsync(empresaId, periodoDias, lojaId);

        // â”€â”€ Delegation â€” Alertas â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public Task<IReadOnlyList<AlertaEstoqueResumo>> GetItensCriticosResumoAsync(Guid empresaId, int top = 20, Guid? lojaId = null)
            => _dashboard.GetItensCriticosResumoAsync(empresaId, top, lojaId);

        // â”€â”€ Delegation â€” Receita Ã— Custo â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public Task<IReadOnlyList<ReceitaCustoDia>> GetReceitaCustoSerieAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null, int timezoneOffsetMinutes = 0)
            => _dashboard.GetReceitaCustoSerieAsync(empresaId, de, ate, lojaId, timezoneOffsetMinutes);

        // â”€â”€ Delegation â€” Dashboard Extras â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public Task<IReadOnlyList<FluxoCaixaDia>> GetFluxoCaixaAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
            => _dashboard.GetFluxoCaixaAsync(empresaId, de, ate, lojaId);

        public Task<IReadOnlyList<ValidadeSemanaItem>> GetValidadeTimelineAsync(Guid empresaId, Guid? lojaId = null)
            => _dashboard.GetValidadeTimelineAsync(empresaId, lojaId);

        public Task<IReadOnlyList<TopProdutoDashboard>> GetTopProdutosAsync(Guid empresaId, DateTime de, DateTime ate, int top = 5, Guid? lojaId = null)
            => _dashboard.GetTopProdutosAsync(empresaId, de, ate, top, lojaId);

        public Task<IReadOnlyList<TopClienteDashboard>> GetTopClientesAsync(Guid empresaId, DateTime de, DateTime ate, int top = 5, Guid? lojaId = null)
            => _dashboard.GetTopClientesAsync(empresaId, de, ate, top, lojaId);

        public Task<IReadOnlyList<ProducaoPorOperador>> GetProducaoPorOperadorAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
            => _dashboard.GetProducaoPorOperadorAsync(empresaId, de, ate, lojaId);

        public Task<IReadOnlyList<EntradasSaidasSemana>> GetEntradasSaidasSemanalAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
            => _dashboard.GetEntradasSaidasSemanalAsync(empresaId, de, ate, lojaId);

        public Task<FornecedoresResumo> GetFornecedoresResumoAsync(Guid empresaId, Guid? lojaId = null)
            => _dashboard.GetFornecedoresResumoAsync(empresaId, lojaId);

        public Task<IReadOnlyList<NovosClientesMes>> GetNovosClientesPorMesAsync(Guid empresaId, int meses = 6, Guid? lojaId = null)
            => _dashboard.GetNovosClientesPorMesAsync(empresaId, meses, lojaId);

        // â”€â”€ Delegation â€” Receita â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public Task<IReadOnlyList<ReceitaPorPeriodo>> GetReceitaPorPeriodoAsync(Guid empresaId, int meses = 12, Guid? lojaId = null)
            => _receita.GetReceitaPorPeriodoAsync(empresaId, meses, lojaId);

        public Task<IReadOnlyList<MargemPorProduto>> GetMargemPorProdutoAsync(Guid empresaId, int dias = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _receita.GetMargemPorProdutoAsync(empresaId, dias, page, pageSize, lojaId);

        public Task<IReadOnlyList<VendaPorCanal>> GetVendasPorCanalAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
            => _receita.GetVendasPorCanalAsync(empresaId, de, ate, lojaId);

        // â”€â”€ Delegation â€” Estoque â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public Task<(IReadOnlyList<ValidadeAlerta> Items, int TotalCount)> GetAlertasValidadeAsync(
            Guid empresaId, int dias = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _estoque.GetAlertasValidadeAsync(empresaId, dias, page, pageSize, lojaId);

        public Task<(IReadOnlyList<ItemParadoDetalhe> Items, int TotalCount)> GetItensParadosDetalhadosAsync(
            Guid empresaId, int diasSemMovimento = 90, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _estoque.GetItensParadosDetalhadosAsync(empresaId, diasSemMovimento, page, pageSize, lojaId);

        public Task<IReadOnlyList<SazonalidadeMensal>> GetSazonalidadeAsync(Guid empresaId, Guid produtoId, int meses = 12, Guid? lojaId = null)
            => _estoque.GetSazonalidadeAsync(empresaId, produtoId, meses, lojaId);

        public Task<(IReadOnlyList<ReposicaoSugerida> Items, int TotalCount)> GetSugestaoReposicaoDetalhadaAsync(
            Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _estoque.GetSugestaoReposicaoDetalhadaAsync(empresaId, diasHistorico, page, pageSize, lojaId);

        public Task<(IReadOnlyList<ProjecaoRuptura> Items, int TotalCount)> GetProjecaoRupturaAsync(
            Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _estoque.GetProjecaoRupturaAsync(empresaId, diasHistorico, page, pageSize, lojaId);

    }
}
