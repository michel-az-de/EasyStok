using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job para gerar relatórios mensais automaticamente.
/// Executa no primeiro dia de cada mês para gerar resumos consolidados.
/// </summary>
public sealed class RelatorioMensalJob(
    IServiceProvider serviceProvider,
    ILogger<RelatorioMensalJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job de relatório mensal iniciado");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var nextRun = new DateTime(now.Year, now.Month, 1).AddMonths(1);
                var delay = nextRun - now;

                if (delay > TimeSpan.Zero)
                {
                    logger.LogInformation("Próximo relatório mensal em {Delay}", delay);
                    await Task.Delay(delay, stoppingToken);
                }

                await GerarRelatoriosMensaisAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro na geração de relatórios mensais");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }
    }

    private async Task GerarRelatoriosMensaisAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var analyticsRepo = scope.ServiceProvider.GetRequiredService<IAnalyticsRepository>();
        var empresaRepo = scope.ServiceProvider.GetRequiredService<IEmpresaRepository>();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();

        var empresas = await empresaRepo.GetAllAsync();
        foreach (var empresa in empresas)
        {
            await GerarRelatorioEmpresaAsync(empresa, analyticsRepo, storageService, cancellationToken);
        }
    }

    private async Task GerarRelatorioEmpresaAsync(
        Empresa empresa,
        IAnalyticsRepository analyticsRepo,
        IStorageService storageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var mesAnterior = DateTime.UtcNow.AddMonths(-1);
            var ano = mesAnterior.Year;
            var mes = mesAnterior.Month;

            var receita = await analyticsRepo.GetReceitaPorPeriodoAsync(empresa.Id, 1);
            var margem = await analyticsRepo.GetMargemPorProdutoAsync(empresa.Id, 30, 1, 50);
            var dashboard = await analyticsRepo.GetDashboardResumoAsync(empresa.Id, 30);

            var htmlReport = GerarHtmlRelatorio(empresa, dashboard, receita.FirstOrDefault(), margem, ano, mes);

            var fileName = $"relatorio-{empresa.Id}-{ano}-{mes:00}.html";
            await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(htmlReport));
            var fileUrl = await storageService.UploadAsync("relatorios", fileName, stream, "text/html");

            logger.LogInformation("Relatório mensal gerado para empresa {EmpresaId}: {FileUrl}", empresa.Id, fileUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao gerar relatório para empresa {EmpresaId}", empresa.Id);
        }
    }

    private static string GerarHtmlRelatorio(
        Empresa empresa,
        DashboardResumo dashboard,
        ReceitaPorPeriodo? receita,
        IReadOnlyList<MargemPorProduto> margem,
        int ano,
        int mes)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <title>Relatório Mensal - {empresa.Nome}</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; }}
        .header {{ background: #f0f0f0; padding: 20px; border-radius: 5px; }}
        .metric {{ margin: 10px 0; }}
        .metric strong {{ display: inline-block; width: 200px; }}
        table {{ border-collapse: collapse; width: 100%; margin-top: 20px; }}
        th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
        th {{ background-color: #f2f2f2; }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>Relatório Mensal - {empresa.Nome}</h1>
        <p>Período: {ano}/{mes:00}</p>
    </div>

    <h2>Resumo Geral</h2>
    <div class='metric'><strong>Total de SKUs:</strong> {dashboard.TotalSkus}</div>
    <div class='metric'><strong>Quantidade em Estoque:</strong> {dashboard.QuantidadeTotalEmEstoque}</div>
    <div class='metric'><strong>Valor Total do Estoque:</strong> R$ {dashboard.ValorTotalEstoque:F2}</div>
    <div class='metric'><strong>Média Vendas Diárias:</strong> {dashboard.MediaVendasDiaria} unidades</div>
    <div class='metric'><strong>Receita Estimada (30 dias):</strong> R$ {dashboard.ReceitaEstimadaPeriodo:F2}</div>
    <div class='metric'><strong>Alertas Ativos:</strong> {dashboard.AlertasEstoqueBaixo + dashboard.AlertasVencimento + dashboard.AlertasItensParados}</div>

    <h2>Receita do Mês</h2>
    {(receita != null ? $@"
    <div class='metric'><strong>Receita Bruta:</strong> R$ {receita.ReceitaBruta:F2}</div>
    <div class='metric'><strong>Total de Vendas:</strong> {receita.TotalVendas}</div>
    <div class='metric'><strong>Total de Itens Vendidos:</strong> {receita.TotalItensVendidos}</div>
    <div class='metric'><strong>Ticket Médio:</strong> R$ {receita.TicketMedio:F2}</div>
    " : "<p>Sem dados de receita para o período.</p>")}

    <h2>Top 5 Produtos por Margem</h2>
    <table>
        <thead>
            <tr>
                <th>Produto</th>
                <th>Custo Médio</th>
                <th>Preço Médio</th>
                <th>Margem (%)</th>
                <th>Qtd Vendida</th>
            </tr>
        </thead>
        <tbody>
            {string.Join("", margem.Take(5).Select(p => $@"
            <tr>
                <td>{p.NomeProduto}</td>
                <td>R$ {p.CustoMedio:F2}</td>
                <td>R$ {p.PrecoMedioVenda:F2}</td>
                <td>{p.MargemPercentual:F1}%</td>
                <td>{p.QuantidadeVendida}</td>
            </tr>"))}
        </tbody>
    </table>
</body>
</html>";
    }
}
