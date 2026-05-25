using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Application.UseCases.Analytics.DashboardExtras;

public class GetDashboardExtrasUseCase(IAnalyticsRepository analyticsRepository)
{
    public async Task<DashboardExtrasResult> ExecuteAsync(GetDashboardExtrasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var now = DateTime.UtcNow.AddMinutes(-cmd.TimezoneOffsetMinutes);
        var ate = now;
        var de = ate.AddDays(-cmd.PeriodoDias);

        // DbContext scoped não é thread-safe — queries executadas em sequência.
        // A maioria responde do cache Redis (5min TTL), então o custo total fica baixo.
        var fluxoCaixa = await analyticsRepository.GetFluxoCaixaAsync(cmd.EmpresaId, de, ate, cmd.LojaId);
        var validade = await analyticsRepository.GetValidadeTimelineAsync(cmd.EmpresaId, cmd.LojaId);
        var topProdutos = await analyticsRepository.GetTopProdutosAsync(cmd.EmpresaId, de, ate, 5, cmd.LojaId);
        var topClientes = await analyticsRepository.GetTopClientesAsync(cmd.EmpresaId, de, ate, 5, cmd.LojaId);
        var producao = await analyticsRepository.GetProducaoPorOperadorAsync(cmd.EmpresaId, de, ate, cmd.LojaId);
        var entradasSaidas = await analyticsRepository.GetEntradasSaidasSemanalAsync(cmd.EmpresaId, de, ate, cmd.LojaId);
        var fornecedores = await analyticsRepository.GetFornecedoresResumoAsync(cmd.EmpresaId, cmd.LojaId);
        var novosClientes = await analyticsRepository.GetNovosClientesPorMesAsync(cmd.EmpresaId, 6, cmd.LojaId);

        return new DashboardExtrasResult(
            FluxoCaixa: fluxoCaixa,
            ValidadeTimeline: validade,
            TopProdutos: topProdutos,
            TopClientes: topClientes,
            ProducaoPorOperador: producao,
            EntradasSaidasSemanal: entradasSaidas,
            Fornecedores: fornecedores,
            NovosClientes: novosClientes);
    }
}
