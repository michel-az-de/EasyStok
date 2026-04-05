namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job legado de processamento de recebimentos.
/// A lógica efetiva fica atrás de uma abstração explícita para evitar acoplamento
/// com modelos experimentais ou incompletos.
/// </summary>
public sealed class ProcessarRecebimentoJob(
    IPedidoFornecedorRecebimentoProcessor processor,
    ILogger<ProcessarRecebimentoJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job de processamento de recebimentos iniciado");

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var totalProcessado = await processor.ProcessAsync(stoppingToken);
                logger.LogInformation(
                    "Processamento de recebimentos concluido. Total processado: {TotalProcessado}",
                    totalProcessado);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no processamento de recebimentos");
            }
        }
    }
}
