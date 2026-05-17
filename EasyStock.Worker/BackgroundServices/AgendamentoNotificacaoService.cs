using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Lembretes de pedidos agendados (mobile_orders.scheduled_delivery_at). A cada
/// tick varre pedidos com status aguardando/preparando que tem ScheduledDeliveryAt
/// preenchido e dispara ate 3 notificacoes por pedido:
///   1. No dia (a partir de 24h antes da entrega)
///   2. 1 hora antes
///   3. 10 minutos antes
///
/// Idempotencia via colunas agendamento_notificado_*_em (mesma estrategia do
/// SlaMonitorService com UltimoAlerta50PctEm/80PctEm).
///
/// Single-instance entre replicas via PostgresAdvisoryLock (0x5045_4147_4E00_0001
/// = "PEAGN" pedido agendamento notificacao).
/// </summary>
public sealed class AgendamentoNotificacaoService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<AgendamentoNotificacaoService> logger) : BackgroundService
{
    private const long LockId = 0x5045_4147_4E00_0001L;

    private static readonly string[] StatusAtivos = ["aguardando", "preparando"];

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
        var sp = scope.ServiceProvider;
        var advisoryLock = sp.GetRequiredService<PostgresAdvisoryLock>();

        await advisoryLock.TentarExecutarAsync(LockId, async token =>
        {
            ct = token;
            var db = sp.GetRequiredService<EasyStockDbContext>();
            var notificador = sp.GetRequiredService<INotificadorService>();

            var agora = DateTime.UtcNow;

            // Candidatos: pedidos com agendamento + status ativo + ao menos
            // uma das 3 notificacoes pendente. EmpresaId nao pode ser null
            // (PublicarEventoAsync exige).
            var candidatos = await db.Set<Order>()
                .AsNoTracking()
                .Where(o => o.ScheduledDeliveryAt != null
                         && o.EmpresaId != null
                         && StatusAtivos.Contains(o.Status)
                         && (o.AgendamentoNotificadoDiaEm == null
                             || o.AgendamentoNotificado1hEm == null
                             || o.AgendamentoNotificado10minEm == null))
                .Select(o => new AgendamentoSnapshot(
                    o.Id, o.EmpresaId!.Value, o.ClientSnapshotName,
                    o.ScheduledDeliveryAt!.Value,
                    o.AgendamentoNotificadoDiaEm,
                    o.AgendamentoNotificado1hEm,
                    o.AgendamentoNotificado10minEm))
                .ToListAsync(token);

            foreach (var snap in candidatos)
            {
                await ProcessarAgendamentoAsync(snap, agora, db, notificador, token);
            }
        }, ct);
    }

    private async Task ProcessarAgendamentoAsync(
        AgendamentoSnapshot snap,
        DateTime agora,
        EasyStockDbContext db,
        INotificadorService notificador,
        CancellationToken ct)
    {
        // Lembrete "no dia": dispara 24h antes do horario agendado. Janela
        // ampla cobre fuso BR (UTC-3) sem precisar saber TZ da empresa —
        // pedido pra 19h BR (22h UTC) ja entra no radar a partir das 22h UTC
        // do dia anterior, que cobre todo o expediente do dia local.
        if (snap.NotificadoDiaEm is null && agora >= snap.ScheduledDeliveryAt.AddHours(-24))
        {
            await DispararAsync(snap, TipoEventoNotificacao.PedidoAgendadoHoje, "Dia", notificador, ct);
            await db.Set<Order>()
                .Where(o => o.Id == snap.OrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.AgendamentoNotificadoDiaEm, agora), ct);
        }

        if (snap.Notificado1hEm is null && agora >= snap.ScheduledDeliveryAt.AddHours(-1))
        {
            await DispararAsync(snap, TipoEventoNotificacao.PedidoAgendadoEm1Hora, "1h", notificador, ct);
            await db.Set<Order>()
                .Where(o => o.Id == snap.OrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.AgendamentoNotificado1hEm, agora), ct);
        }

        if (snap.Notificado10minEm is null && agora >= snap.ScheduledDeliveryAt.AddMinutes(-10))
        {
            await DispararAsync(snap, TipoEventoNotificacao.PedidoAgendadoEm10Minutos, "10min", notificador, ct);
            await db.Set<Order>()
                .Where(o => o.Id == snap.OrderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.AgendamentoNotificado10minEm, agora), ct);
        }
    }

    private static Task DispararAsync(
        AgendamentoSnapshot snap,
        TipoEventoNotificacao tipo,
        string kind,
        INotificadorService notificador,
        CancellationToken ct) =>
        notificador.PublicarEventoAsync(
            tipo,
            snap.EmpresaId,
            usuarioDestinoId: null, // empresa toda — sem dono dedicado no pedido mobile
            payloadJson: JsonSerializer.Serialize(new
            {
                orderId = snap.OrderId,
                clienteNome = snap.ClienteNome,
                scheduledFor = snap.ScheduledDeliveryAt,
                kind
            }),
            ct: ct);

    private sealed record AgendamentoSnapshot(
        string OrderId,
        Guid EmpresaId,
        string ClienteNome,
        DateTime ScheduledDeliveryAt,
        DateTime? NotificadoDiaEm,
        DateTime? Notificado1hEm,
        DateTime? Notificado10minEm);
}
