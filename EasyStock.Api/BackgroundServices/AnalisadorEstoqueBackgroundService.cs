using EasyStock.Api.Configuration;
using Microsoft.Extensions.Options;
using EasyStock.Api.Services;

namespace EasyStock.Api.BackgroundServices
{
    public sealed class AnalisadorEstoqueBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AnalisadorEstoqueBackgroundService> logger)
        : BackgroundService
    {
        private readonly TimeSpan _intervalo = TimeSpan.FromMinutes(60);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("AnalisadorEstoqueBackgroundService iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await AnalisarAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Erro durante analise de estoque no background service.");
                }

                await Task.Delay(_intervalo, stoppingToken);
            }
        }

        private async Task AnalisarAsync(CancellationToken ct)
        {
            using var scope = scopeFactory.CreateScope();
            var gerador = scope.ServiceProvider.GetRequiredService<GeradorNotificacoesAutomaticas>();
            await gerador.ExecutarAsync(ct);
        }
    }
}
