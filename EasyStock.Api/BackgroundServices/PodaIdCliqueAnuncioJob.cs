using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// #2928, S6: uma vez por dia (04:00 UTC) anula o id de clique de anúncio (<c>IdClique</c> e <c>TipoIdClique</c>) de
/// pedidos e conversas capturados há mais de <see cref="OrigemAnuncio.RetencaoIdClique"/>. Plataforma, campanha, UTMs e
/// anúncio permanecem. Idempotente: rodar de novo não acha nada a podar.
/// </summary>
public sealed class PodaIdCliqueAnuncioJob(
    IServiceProvider serviceProvider,
    TimeProvider relogio,
    ILogger<PodaIdCliqueAnuncioJob> logger) : BackgroundService
{
    private static readonly TimeSpan HoraDaRodada = TimeSpan.FromHours(4);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var agora = relogio.GetUtcNow();
                var proxima = new DateTimeOffset(agora.UtcDateTime.Date, TimeSpan.Zero) + HoraDaRodada;
                if (proxima <= agora) proxima = proxima.AddDays(1);
                await Task.Delay(proxima - agora, relogio, stoppingToken);

                await PodarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "PodaIdCliqueAnuncioJob: erro na rodada — aguardando 1h.");
                try { await Task.Delay(TimeSpan.FromHours(1), relogio, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Uma rodada: devolve quantas linhas (pedidos e conversas) perderam o id de clique.</summary>
    public async Task<int> PodarAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var podados = await scope.ServiceProvider.GetRequiredService<IOrigemAnuncioRepository>()
            .PodarIdsDeCliqueAsync(relogio.GetUtcNow() - OrigemAnuncio.RetencaoIdClique, ct);
        if (podados > 0)
            logger.LogInformation("Origem de anúncio: {Podados} ids de clique com mais de 90 dias anulados.", podados);
        return podados;
    }
}
