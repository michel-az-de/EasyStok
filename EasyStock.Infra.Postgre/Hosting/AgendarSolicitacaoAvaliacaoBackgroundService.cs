using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Events.Storefront;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Hosting;

/// <summary>
/// Background service que envia link de avaliação WhatsApp +24h após entrega do pedido.
///
/// <para>
/// Intervalo: 1h. Batch máximo: 50. Cada pedido elegível recebe a notificação
/// exatamente uma vez: o campo <c>Pedido.AvaliacaoSolicitadaEm</c> é setado imediatamente
/// antes do envio, servindo como flag anti-duplicata.
/// </para>
/// </summary>
public sealed class AgendarSolicitacaoAvaliacaoBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AgendarSolicitacaoAvaliacaoBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);
    private static readonly TimeSpan JanelaEntrega = TimeSpan.FromHours(24);
    private const int MaxBatch = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Intervalo, stoppingToken).ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await ProcessarBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "AgendarSolicitacaoAvaliacao erro inesperado.");
            }
        }
    }

    private async Task ProcessarBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var pedidoRepo = scope.ServiceProvider.GetRequiredService<IPedidoStorefrontRepository>();
        var storefrontRepo = scope.ServiceProvider.GetRequiredService<IStorefrontRepository>();
        var handler = scope.ServiceProvider.GetRequiredService<EnviarLinkAvaliacaoWhatsAppHandler>();

        var limite = DateTime.UtcNow - JanelaEntrega;
        var pedidos = await pedidoRepo.GetEntreguesElegiveisPraAvaliacaoAsync(limite, MaxBatch, ct);

        if (pedidos.Count == 0) return;

        logger.LogInformation(
            "AgendarSolicitacaoAvaliacao processando {Count} pedidos elegíveis.", pedidos.Count);

        foreach (var pedido in pedidos)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pedido.ClienteTelefone))
                {
                    logger.LogWarning(
                        "Pedido sem telefone — pulando avaliação. pedidoId={PedidoId}", pedido.Id);
                    // Marca como solicitado para não tentar de novo
                    pedido.AvaliacaoSolicitadaEm = DateTime.UtcNow;
                    await pedidoRepo.UpdateAsync(pedido, ct);
                    continue;
                }

                // Resolve slug via EmpresaId
                var storefront = await storefrontRepo.GetByEmpresaAsync(pedido.EmpresaId, ct);
                if (storefront is null)
                {
                    logger.LogWarning(
                        "Storefront não encontrado para empresaId={EmpresaId}. pedidoId={PedidoId}",
                        pedido.EmpresaId, pedido.Id);
                    continue;
                }

                // Marca ANTES do envio (flag anti-duplicata — even if send fails, won't re-send)
                pedido.AvaliacaoSolicitadaEm = DateTime.UtcNow;
                await pedidoRepo.UpdateAsync(pedido, ct);

                var evento = new NotificarClienteSolicitarAvaliacaoEvent(
                    PedidoId: pedido.Id,
                    ClienteId: pedido.ClienteId ?? Guid.Empty,
                    EmpresaId: pedido.EmpresaId,
                    TelefoneCliente: pedido.ClienteTelefone,
                    NomeCliente: pedido.ClienteNome ?? "Cliente",
                    Slug: storefront.Slug);

                await handler.HandleAsync(evento, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "AgendarSolicitacaoAvaliacao erro ao processar pedidoId={PedidoId}.", pedido.Id);
            }
        }
    }
}
