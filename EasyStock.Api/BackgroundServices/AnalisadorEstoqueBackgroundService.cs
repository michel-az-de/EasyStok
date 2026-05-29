namespace EasyStock.Api.BackgroundServices
{
    public sealed class AnalisadorEstoqueBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AnalisadorEstoqueBackgroundService> logger)
        : BackgroundService
    {
        private readonly TimeSpan _intervalo = TimeSpan.FromMinutes(60);
        private readonly TimeSpan _retryDelay = TimeSpan.FromMinutes(5);
        private const int MaxRetries = 3;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("AnalisadorEstoqueBackgroundService iniciado.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ExecutarComRetryAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Defensivo: ExecutarComRetryAsync já captura exceções internas,
                    // mas um bug futuro que deixe vazar erro não deve derrubar o serviço.
                    logger.LogError(ex,
                        "Exceção inesperada escapou do retry loop em AnalisadorEstoqueBackgroundService. Continuando após delay.");
                }

                try
                {
                    await Task.Delay(_intervalo, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task ExecutarComRetryAsync(CancellationToken ct)
        {
            for (var tentativa = 1; tentativa <= MaxRetries; tentativa++)
            {
                try
                {
                    await AnalisarAsync(ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (tentativa == MaxRetries)
                    {
                        logger.LogError(ex,
                            "Analise de estoque falhou apos {MaxRetries} tentativas. Evento descartado (dead-letter). Proxima execucao em {Intervalo} minutos.",
                            MaxRetries, _intervalo.TotalMinutes);
                        return;
                    }

                    logger.LogWarning(ex,
                        "Erro na tentativa {Tentativa}/{MaxRetries} da analise de estoque. Aguardando {RetryDelay} minutos para nova tentativa.",
                        tentativa, MaxRetries, _retryDelay.TotalMinutes);

                    await Task.Delay(_retryDelay, ct);
                }
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
