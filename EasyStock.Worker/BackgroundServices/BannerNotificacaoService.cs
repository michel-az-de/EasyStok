using System.Text.Json;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Concurrency;
using Microsoft.Extensions.Options;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Fan-out da notificação opcional de banners de plataforma (#869). A cada tick, varre banners
/// com <c>NotificarAoPublicar=true</c>, ativos e ainda não notificados; para cada um faz o guard
/// atômico (<c>MarcarNotificadoSeIneditoAsync</c> — UPDATE ... WHERE NotificadoEm IS NULL) e, se
/// venceu a corrida, enfileira um evento <see cref="TipoEventoNotificacao.BroadcastSuperAdmin"/>
/// (InApp) por empresa (<c>usuarioDestinoId=null</c> = empresa toda), reusando a rotina/template
/// já semeados. O fan-out roda AQUI, no worker — nunca no request (Admin só persiste o flag).
///
/// <para>
/// Guard-first (at-most-once): marca antes de enfileirar. Se o processo cair no meio do fan-out,
/// o banner fica marcado e não re-broadcasta a todas as empresas no próximo tick (evita spam);
/// a contrapartida é entrega parcial nessa falha rara. Single-instance entre réplicas via
/// <see cref="PostgresAdvisoryLock"/>.
/// </para>
/// </summary>
public sealed class BannerNotificacaoService(
    IServiceProvider serviceProvider,
    IOptions<WorkerOptions> options,
    ILogger<BannerNotificacaoService> logger) : BackgroundService
{
    private const long LockId = 0x4241_4E4E_4552_0001L; // "BANNER"

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BannerNotificacaoService iniciado");

        var intervaloSegundos = Math.Max(60, options.Value.BannerNotificacaoIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecutarTickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro durante tick do BannerNotificacaoService");
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
            var bannerRepo = sp.GetRequiredService<IBannerRepository>();
            var empresaRepo = sp.GetRequiredService<IEmpresaRepository>();
            var notificador = sp.GetRequiredService<INotificadorService>();

            var agora = DateTime.UtcNow;
            var pendentes = await bannerRepo.ListarPendentesDeNotificacaoAsync(agora, token);
            if (pendentes.Count == 0) return;

            // Materializa os ids de empresa uma vez (contagem = tenants, pequena). Evita abrir
            // um reader de streaming enquanto PublicarEventoAsync faz SaveChanges na mesma conexão.
            var empresaIds = (await empresaRepo.GetAllAsync()).Select(e => e.Id).ToList();

            foreach (var banner in pendentes)
            {
                var venceu = await bannerRepo.MarcarNotificadoSeIneditoAsync(banner.Id, agora, token);
                if (!venceu) continue; // outra réplica/tick já pegou

                var payload = JsonSerializer.Serialize(new
                {
                    titulo = banner.TituloInterno,
                    mensagem = string.IsNullOrWhiteSpace(banner.Corpo) ? banner.TituloInterno : banner.Corpo
                });

                foreach (var empresaId in empresaIds)
                {
                    await notificador.PublicarEventoAsync(
                        TipoEventoNotificacao.BroadcastSuperAdmin,
                        empresaId,
                        usuarioDestinoId: null, // empresa toda
                        payloadJson: payload,
                        ct: token);
                }

                logger.LogInformation(
                    "Banner {BannerId} notificado para {Total} empresa(s).", banner.Id, empresaIds.Count);
            }
        }, ct);
    }
}
