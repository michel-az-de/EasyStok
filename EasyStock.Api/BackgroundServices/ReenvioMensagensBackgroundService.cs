using EasyStock.Application.UseCases.Atendimento.Reenvio;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Reenvio automático das mensagens que falharam por motivo temporário (S57, #1355). A cada rodada: reserva
/// as vencidas num escopo com bypass de RLS (cross-tenant, <c>FOR UPDATE SKIP LOCKED</c>) e reenvia cada uma
/// num escopo próprio com o tenant dela. Vários processos da API podem rodar juntos: a reserva não duplica.
/// </summary>
public sealed class ReenvioMensagensBackgroundService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<ReenvioMensagensBackgroundService> logger) : BackgroundService
{
    private const int LotePorRodada = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(
            configuration.GetValue("Atendimento:ReenvioMensagens:PollingIntervalSeconds", defaultValue: 30));
        logger.LogInformation("ReenvioMensagensBackgroundService iniciado — polling={Intervalo}.", intervalo);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RodadaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReenvioMensagensBackgroundService: erro na rodada — continua no próximo tick.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RodadaAsync(CancellationToken ct)
    {
        IReadOnlyList<ReenvioReservado> reservados;
        using (var scope = serviceProvider.CreateScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().UseRowLevelSecurityBypass();
            reservados = await scope.ServiceProvider.GetRequiredService<ReservarReenviosUseCase>()
                .ExecuteAsync(LotePorRodada, ct);
        }

        foreach (var reservado in reservados)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                scope.ServiceProvider.GetRequiredService<EasyStock.Application.Ports.Output.ITenantContextAccessor>()
                    .SetCurrentTenant(reservado.EmpresaId);
                var r = await scope.ServiceProvider.GetRequiredService<ReenviarMensagemUseCase>()
                    .ExecuteAsync(reservado.EmpresaId, reservado.ConversaId, reservado.MensagemId, ct);
                logger.LogInformation("Reenvio da mensagem {MensagemId}: {Status}.", reservado.MensagemId, r.Status);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Reenvio da mensagem {MensagemId} não rodou.", reservado.MensagemId);
            }
        }
    }
}
