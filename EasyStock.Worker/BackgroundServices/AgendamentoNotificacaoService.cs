using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Notifications.Agendamento;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Lembretes de pedidos agendados (mobile_orders.scheduled_delivery_at). A cada tick chama
/// <see cref="LembretesPedidoAgendadoTick"/>, que varre pedidos com status aguardando/preparando e entrega agendada e
/// dispara ate 3 notificacoes por pedido:
///   1. No dia (a partir de 24h antes da entrega)
///   2. 1 hora antes
///   3. 10 minutos antes
///
/// Idempotencia via colunas agendamento_notificado_*_em (mesma estrategia do
/// SlaMonitorService com UltimoAlerta50PctEm/80PctEm).
///
/// Single-instance entre replicas via PostgresAdvisoryLock (0x5045_4147_4E00_0001
/// = "PEAGN" pedido agendamento notificacao). O lock so protege a exclusao: o tick publica cada pedido em escopo
/// proprio, com o tenant do pedido (N1), porque a conexao do lock nao tem tenant e a RLS recusaria o INSERT do evento.
/// </summary>
public sealed class AgendamentoNotificacaoService(
    IServiceProvider serviceProvider,
    LembretesPedidoAgendadoTick tick,
    IOptions<WorkerOptions> options,
    ILogger<AgendamentoNotificacaoService> logger) : BackgroundService
{
    private const long LockId = 0x5045_4147_4E00_0001L;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AgendamentoNotificacaoService iniciado");

        var intervaloSegundos = Math.Max(60, options.Value.AgendamentoNotificacaoIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutarTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro durante tick do AgendamentoNotificacaoService");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(intervaloSegundos), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ExecutarTickAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var advisoryLock = scope.ServiceProvider.GetRequiredService<PostgresAdvisoryLock>();

        await advisoryLock.TentarExecutarAsync(LockId, token => tick.ExecutarAsync(token), ct);
    }
}
