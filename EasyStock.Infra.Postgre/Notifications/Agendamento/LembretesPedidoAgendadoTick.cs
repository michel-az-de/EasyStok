using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Notifications.Agendamento;

/// <summary>
/// Um tick dos lembretes de pedidos agendados (<c>mobile_orders.scheduled_delivery_at</c>): varre pedidos
/// <c>aguardando</c> ou <c>preparando</c> com entrega agendada e dispara até 3 eventos por pedido (no dia, 1 hora antes,
/// 10 minutos antes), no máximo um por tick: o mais próximo da entrega; os anteriores vencidos e os de entrega passada
/// só são carimbados. Idempotência pelas colunas <c>agendamento_notificado_*_em</c>.
/// <para>
/// N1: cada pedido roda num escopo de DI próprio, com o tenant do pedido fixado antes da primeira conexão. Antes o tick
/// publicava dentro do advisory lock, numa conexão sem tenant: o INSERT em <c>notif_eventos</c> violava o WITH CHECK
/// da RLS (42501) no commit, a exceção abortava o laço de candidatos e se repetia a cada tick. A falha de um pedido
/// não para os outros, e o carimbo de dedup só vem depois de o evento ter sido gravado.
/// </para>
/// <para>
/// O advisory lock (exclusão entre réplicas do Worker) fica no <c>BackgroundService</c> que chama o tick.
/// </para>
/// </summary>
public sealed class LembretesPedidoAgendadoTick(
    IServiceProvider serviceProvider,
    ILogger<LembretesPedidoAgendadoTick> logger)
{
    private static readonly string[] StatusAtivos = ["aguardando", "preparando"];

    public async Task ExecutarAsync(CancellationToken ct)
    {
        var agora = DateTime.UtcNow;
        var candidatos = await ListarCandidatosAsync(ct);

        foreach (var snap in candidatos)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ProcessarAgendamentoAsync(snap, agora, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Sem carimbo: o lembrete tenta de novo no próximo tick e os outros pedidos seguem.
                logger.LogError(ex, "Lembrete de pedido agendado {OrderId} falhou — tenta no próximo tick.", snap.OrderId);
            }
        }
    }

    private async Task<IReadOnlyList<AgendamentoSnapshot>> ListarCandidatosAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        // Candidatos: pedidos com agendamento + status ativo + ao menos
        // uma das 3 notificacoes pendente. EmpresaId nao pode ser null
        // (PublicarEventoAsync exige).
        return await db.Set<Order>()
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
            .ToListAsync(ct);
    }

    private async Task ProcessarAgendamentoAsync(AgendamentoSnapshot snap, DateTime agora, CancellationToken ct)
    {
        // Lembrete "no dia": dispara 24h antes do horario agendado. Janela
        // ampla cobre fuso BR (UTC-3) sem precisar saber TZ da empresa —
        // pedido pra 19h BR (22h UTC) ja entra no radar a partir das 22h UTC
        // do dia anterior, que cobre todo o expediente do dia local.
        var diaVencido = snap.NotificadoDiaEm is null && agora >= snap.ScheduledDeliveryAt.AddHours(-24);
        var umaHoraVencido = snap.Notificado1hEm is null && agora >= snap.ScheduledDeliveryAt.AddHours(-1);
        var dezMinutosVencido = snap.Notificado10minEm is null && agora >= snap.ScheduledDeliveryAt.AddMinutes(-10);
        if (!diaVencido && !umaHoraVencido && !dezMinutosVencido) return;

        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;
        // Antes da primeira conexão: o interceptor emite SET app.empresa_id na abertura.
        sp.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(snap.EmpresaId);
        var db = sp.GetRequiredService<EasyStockDbContext>();
        var notificador = sp.GetRequiredService<INotificadorService>();

        // #1507: sem limite superior, um pedido criado perto da entrega (ou o tick voltando de uma indisponibilidade)
        // disparava os 3 lembretes de uma vez, até para entrega já passada. Agora só o lembrete mais próximo da entrega
        // sai; os anteriores são carimbados sem disparar. Entrega passada: só carimba.
        if (agora < snap.ScheduledDeliveryAt)
        {
            if (dezMinutosVencido)
                await DispararAsync(snap, TipoEventoNotificacao.PedidoAgendadoEm10Minutos, "10min", notificador, ct);
            else if (umaHoraVencido)
                await DispararAsync(snap, TipoEventoNotificacao.PedidoAgendadoEm1Hora, "1h", notificador, ct);
            else
                await DispararAsync(snap, TipoEventoNotificacao.PedidoAgendadoHoje, "Dia", notificador, ct);
        }

        // Carimbo depois de o evento ter sido gravado (N1), num UPDATE só para todos os vencidos.
        await db.Set<Order>()
            .Where(o => o.Id == snap.OrderId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.AgendamentoNotificadoDiaEm,
                    o => diaVencido ? (DateTime?)agora : o.AgendamentoNotificadoDiaEm)
                .SetProperty(o => o.AgendamentoNotificado1hEm,
                    o => umaHoraVencido ? (DateTime?)agora : o.AgendamentoNotificado1hEm)
                .SetProperty(o => o.AgendamentoNotificado10minEm,
                    o => dezMinutosVencido ? (DateTime?)agora : o.AgendamentoNotificado10minEm), ct);
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
