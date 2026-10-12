using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Operacao.Atraso;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Aviso de atraso do pedido (S21). A cada 60 s lista os pedidos <c>aguardando</c> com o início previsto
/// vencido e sem aviso (cross-tenant) e processa cada um no próprio escopo, com o tenant dele ligado, pelo
/// <see cref="NotificarAtrasoPedidoUseCase"/>, que marca <c>AtrasoNotificadoEm</c> e publica
/// <c>pedido.atrasado</c> no SSE de operação.
///
/// <para>
/// Sem advisory lock: o use case trava o pedido (<c>SELECT FOR UPDATE</c>) e confere a marca no lock, então
/// duas instâncias não avisam duas vezes. Desligado por <c>BackgroundJobs:EnablePedidoAtraso=false</c>.
/// </para>
/// </summary>
public sealed class PedidoAtrasoJob(
    IServiceProvider serviceProvider,
    TimeProvider relogio,
    ILogger<PedidoAtrasoJob> logger) : BackgroundService
{
    private const int MaximoPorRodada = 100;
    private static readonly TimeSpan Intervalo = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PedidoAtrasoJob iniciado");
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessarRodadaAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "PedidoAtrasoJob: erro na rodada.");
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessarRodadaAsync(CancellationToken ct)
    {
        IReadOnlyList<PedidoAtrasoCandidato> candidatos;
        using (var scope = serviceProvider.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IPedidoStorefrontRepository>();
            candidatos = await repo.ListarAtrasoNaoNotificadoAsync(relogio.GetUtcNow().UtcDateTime, MaximoPorRodada, ct);
        }
        if (candidatos.Count == 0) return;

        var avisados = 0;
        var falhas = 0;
        foreach (var candidato in candidatos)
        {
            try
            {
                // Um escopo por pedido: DbContext limpo e o tenant do pedido só nele.
                using var scope = serviceProvider.CreateScope();
                var useCase = scope.ServiceProvider.GetRequiredService<NotificarAtrasoPedidoUseCase>();
                if (await useCase.ExecuteAsync(candidato, ct)) avisados++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                falhas++;
                logger.LogWarning(ex, "PedidoAtrasoJob: falha no pedido {PedidoId}", candidato.PedidoId);
                if (falhas >= 3)
                {
                    logger.LogWarning("PedidoAtrasoJob: 3 falhas na rodada — interrompendo.");
                    break;
                }
            }
        }

        logger.LogInformation("PedidoAtrasoJob: candidatos={Total} avisados={Avisados} falhas={Falhas}",
            candidatos.Count, avisados, falhas);
    }
}
