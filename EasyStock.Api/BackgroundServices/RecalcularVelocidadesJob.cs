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

        foreach (var item in itens)
        {
            try
            {
                var de = DateTime.UtcNow.AddDays(-30);
                var ate = DateTime.UtcNow;
                var velocidade = await movimentacaoRepo.GetTaxaSaidaDiariaAsync(empresa.Id, item.ProdutoId, de, ate);

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

        logger.LogInformation("Velocidades recalculadas para empresa {EmpresaId}", empresa.Id);
    }
}
