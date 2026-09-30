using EasyStock.Application.UseCases.Atendimento.Programadas;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Disparador das mensagens programadas (S39). A cada rodada: reserva as vencidas num escopo com
/// bypass de RLS (cross-tenant, <c>FOR UPDATE SKIP LOCKED</c>) e envia cada uma num escopo próprio
/// com o tenant da mensagem. Vários processos da API podem rodar juntos: a reserva não duplica.
/// Uma mensagem presa em <c>Enviando</c> por queda do processo fica para revisão da dona (não é
/// reenviada sozinha, para não mandar duas vezes).
/// </summary>
public sealed class MensagensProgramadasBackgroundService(
    IServiceProvider serviceProvider,
    IConfiguration configuration,
    ILogger<MensagensProgramadasBackgroundService> logger) : BackgroundService
{
    private const int LotePorRodada = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalo = TimeSpan.FromSeconds(
            configuration.GetValue("Atendimento:MensagensProgramadas:PollingIntervalSeconds", defaultValue: 30));
        logger.LogInformation("MensagensProgramadasBackgroundService iniciado — polling={Intervalo}.", intervalo);

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
                logger.LogError(ex, "MensagensProgramadasBackgroundService: erro na rodada — continua no próximo tick.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RodadaAsync(CancellationToken ct)
    {
        IReadOnlyList<MensagemReservada> reservadas;
        using (var scope = serviceProvider.CreateScope())
        {
            using var _ = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().UseRowLevelSecurityBypass();
            reservadas = await scope.ServiceProvider.GetRequiredService<ReservarMensagensProgramadasUseCase>()
                .ExecuteAsync(LotePorRodada, ct);
        }

        foreach (var reservada in reservadas)
        {
            using var scope = serviceProvider.CreateScope();
            scope.ServiceProvider.GetRequiredService<EasyStock.Application.Ports.Output.ITenantContextAccessor>()
                .SetCurrentTenant(reservada.EmpresaId);
            await scope.ServiceProvider.GetRequiredService<DispararMensagemProgramadaUseCase>()
                .ExecuteAsync(reservada.EmpresaId, reservada.Id, ct);
        }
    }
}
