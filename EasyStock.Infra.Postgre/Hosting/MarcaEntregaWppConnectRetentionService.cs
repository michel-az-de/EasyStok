using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Infra.Postgre.Concurrency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Hosting;

/// <summary>
/// #3138: cada mensagem individual que a ponte WPPConnect entrega deixa uma marca em <c>webhook_recebidos</c> (provedor
/// <c>wppconnect_entrega</c>), que o vigia da ponte muda consulta numa janela de 6 horas. Sem expurgo a tabela só cresce.
/// Uma vez por dia apaga as marcas com mais de <see cref="RetencaoDias"/> dias.
/// <para>
/// Só este provedor: os demais registros da tabela são a idempotência do processamento de webhooks (inclusive de
/// pagamento) e apagá-los reabriria o reprocessamento de um evento reenviado. A tabela não tem coluna de empresa nem
/// RLS, então a rodada não pede bypass.
/// </para>
/// </summary>
public sealed class MarcaEntregaWppConnectRetentionService(
    IServiceProvider serviceProvider, TimeProvider relogio, ILogger<MarcaEntregaWppConnectRetentionService> logger)
    : BackgroundService
{
    public const int RetencaoDias = 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Não compete com a subida.
            await Task.Delay(TimeSpan.FromMinutes(7), relogio, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RodadaAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Expurgo da marca de entrega WPPConnect falhou; tenta de novo em 24 h.");
                }

                await Task.Delay(TimeSpan.FromHours(24), relogio, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Desligamento do host.
        }
    }

    public Task RodadaAsync(CancellationToken ct) => PostgresAdvisoryLock.TentarExecutarEmEscopoProprioAsync(
        serviceProvider.GetRequiredService<IServiceScopeFactory>(), LockKeys.RetencaoMarcaEntregaWppConnect, ExpurgarAsync, ct);

    private async Task ExpurgarAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var apagadas = await scope.ServiceProvider.GetRequiredService<IWebhookRecebidoRepository>().ExpurgarAsync(
            ProcessarWebhookWppConnectUseCase.ProvedorEntrega, relogio.GetUtcNow().UtcDateTime.AddDays(-RetencaoDias), ct);
        if (apagadas > 0)
            logger.LogInformation("Expurgo da marca de entrega WPPConnect: {Quantidade} registros removidos.", apagadas);
    }
}
