using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Services.Notifications;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Avalia EventoNotificacao pendentes e os converte em OutboxMensagemNotificacao.
/// Também detecta rotinas Cron que deveriam ter disparado e gera seus eventos.
/// </summary>
public sealed class AvaliadorRotinasService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<AvaliadorRotinasService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AvaliadorRotinasService iniciado.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutarRodadaAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Erro no AvaliadorRotinasService — continuando próxima rodada.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(options.Value.AvaliadoresIntervalSeconds),
                stoppingToken);
        }
    }

    internal async Task ExecutarRodadaAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var notificadorService = sp.GetRequiredService<INotificadorService>();
        var eventoRepo = sp.GetRequiredService<IEventoNotificacaoRepository>();
        var rotinaRepo = sp.GetRequiredService<IRotinaRepository>();
        var rotinaScheduler = sp.GetRequiredService<RotinaScheduler>();

        // 1. Processa eventos pendentes (criados por coletores de estado)
        var pendentes = await eventoRepo.ListarPendentesAsync(limit: 200, ct);
        foreach (var evento in pendentes)
        {
            try
            {
                await notificadorService.AvaliarEventoAsync(evento, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao avaliar evento {EventoId}", evento.Id);
            }
        }

        if (pendentes.Count > 0)
            logger.LogInformation("AvaliadorRotinas: processados {Count} eventos pendentes.", pendentes.Count);

        // 2. Dispara rotinas Cron que deveriam ter executado
        var agora = DateTime.UtcNow;
        var ultimaExecucao = agora.AddSeconds(-options.Value.AvaliadoresIntervalSeconds * 2);
        var rotinasAtivas = await rotinaRepo.ListarAtivasAsync(ct: ct);

        foreach (var rotina in rotinasAtivas.Where(r => r.TriggerTipo == Domain.Enums.Notifications.TriggerTipoRotina.Cron))
        {
            if (!rotinaScheduler.DeveriasExecutar(rotina, ultimaExecucao, agora))
                continue;

            logger.LogInformation("Rotina cron {Codigo} disparada.", rotina.Codigo);
            // Rotinas Cron com dados globais são tratadas pelos coletores de estado (PR4).
            // Aqui apenas logamos — o ColetorEventosDeEstadoService criará os eventos.
        }
    }
}
