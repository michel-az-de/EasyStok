using EasyStock.Application.Ports.Output.Persistence.Atendimento;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Apaga as sessões do chat do site vencidas há mais de um dia (S36). A sessão vencida já não vale
/// (o acesso confere a validade); a limpeza só evita a tabela crescer. A conversa e as mensagens ficam.
/// </summary>
public sealed class LimpezaSessoesChatSiteBackgroundService(
    IServiceProvider serviceProvider,
    ILogger<LimpezaSessoesChatSiteBackgroundService> logger) : BackgroundService
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);
    public static readonly TimeSpan Carencia = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var removidas = await scope.ServiceProvider.GetRequiredService<ISessaoChatSiteRepository>()
                    .RemoverVencidasAsync(DateTime.UtcNow - Carencia, stoppingToken);
                if (removidas > 0)
                    logger.LogInformation("Chat do site: {Removidas} sessões vencidas apagadas.", removidas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LimpezaSessoesChatSiteBackgroundService: erro na rodada — tenta de novo no próximo tick.");
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}