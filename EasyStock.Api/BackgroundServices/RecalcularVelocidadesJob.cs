using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;

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

        var empresas = await empresaRepo.GetAllAsync();
        foreach (var empresa in empresas)
        {
            await ProcessarEmpresaAsync(empresa, itemEstoqueRepo, movimentacaoRepo, cancellationToken);
        }
    }

    private async Task ProcessarEmpresaAsync(
        Empresa empresa,
        IItemEstoqueRepository itemEstoqueRepo,
        IMovimentacaoEstoqueRepository movimentacaoRepo,
        CancellationToken cancellationToken)
    {
        var (itens, _) = await itemEstoqueRepo.GetItensEstoquePaginadosAsync(empresa.Id, 1, 1000);
        if (!itens.Any()) return;

        var de = DateTime.UtcNow.AddDays(-30);
        var ate = DateTime.UtcNow;

        // Busca todas as taxas de saída em uma única query em vez de 1 por item
        var produtoIds = itens.Select(i => i.ProdutoId).Distinct();
        var taxasPorProduto = await movimentacaoRepo.GetTaxaSaidaDiariaPorProdutoAsync(empresa.Id, produtoIds, de, ate);

        foreach (var item in itens)
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

        logger.LogInformation("Velocidades recalculadas para {Total} itens da empresa {EmpresaId}", itens.Count(), empresa.Id);
    }
}
