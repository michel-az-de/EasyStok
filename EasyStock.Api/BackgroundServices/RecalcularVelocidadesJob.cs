using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job para recalcular velocidades de saída dos itens de estoque.
/// Executa periodicamente para manter as métricas de velocidade atualizadas.
/// </summary>
public sealed class RecalcularVelocidadesJob(
    IServiceProvider serviceProvider,
    ILogger<RecalcularVelocidadesJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job de recálculo de velocidades iniciado");

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RecalcularVelocidadesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no recálculo de velocidades");
            }
        }
    }

    private async Task RecalcularVelocidadesAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var itemEstoqueRepo = scope.ServiceProvider.GetRequiredService<IItemEstoqueRepository>();
        var movimentacaoRepo = scope.ServiceProvider.GetRequiredService<IMovimentacaoEstoqueRepository>();
        var empresaRepo = scope.ServiceProvider.GetRequiredService<IEmpresaRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        var empresas = await empresaRepo.GetAllAsync();
        foreach (var empresa in empresas)
        {
            await ProcessarEmpresaAsync(empresa, itemEstoqueRepo, movimentacaoRepo, dbContext, cancellationToken);
        }
    }

    private async Task ProcessarEmpresaAsync(
        Empresa empresa,
        IItemEstoqueRepository itemEstoqueRepo,
        IMovimentacaoEstoqueRepository movimentacaoRepo,
        EasyStockDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var de = DateTime.UtcNow.AddDays(-30);
        var ate = DateTime.UtcNow;

        const int pageSize = 500;
        var page = 1;
        var totalProcessados = 0;
        List<ItemEstoque> itensList;

        do
        {
            (var itens, _) = await itemEstoqueRepo.GetItensEstoquePaginadosAsync(empresa.Id, page, pageSize);
            itensList = itens.ToList();
            if (itensList.Count == 0) break;

            // Busca todas as taxas de saída em uma única query em vez de 1 por item
            var produtoIds = itensList.Select(i => i.ProdutoId).Distinct();
            var taxasPorProduto = await movimentacaoRepo.GetTaxaSaidaDiariaPorProdutoAsync(empresa.Id, produtoIds, de, ate);

            foreach (var item in itensList)
            {
                try
                {
                    var velocidade = taxasPorProduto.GetValueOrDefault(item.ProdutoId, 0m);
                    item.AtualizarVelocidadeSaida(velocidade, DateTime.UtcNow);
                    await itemEstoqueRepo.UpdateAsync(item);

                    logger.LogDebug(
                        "Velocidade atualizada para item {ItemId}: {Velocidade} unidades/dia",
                        item.Id,
                        velocidade);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Erro ao recalcular velocidade para item {ItemId}", item.Id);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            totalProcessados += itensList.Count;
            page++;
        }
        while (itensList.Count == pageSize);

        logger.LogInformation("Velocidades recalculadas para {Total} itens da empresa {EmpresaId}", totalProcessados, empresa.Id);
    }
}
