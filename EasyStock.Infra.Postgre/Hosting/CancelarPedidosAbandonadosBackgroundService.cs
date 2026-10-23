using EasyStock.Application.Events.Storefront.Handlers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Events.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Concurrency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Postgre.Hosting;

/// <summary>
/// Background service que cancela pedidos do checkout (site logado, site sem login e conversa) em
/// <c>AguardandoPagamento</c> ou <c>Rascunho</c> (fase 1 interrompida, #1623-a) sem cobrança há mais de 30 minutos — prevenindo vagas órfãs (ADR-0014 §Solução 2, #1291).
///
/// <para>
/// Intervalo: 5 min. Batch máximo: 50. Cada pedido cancelado invoca diretamente
/// <see cref="LiberarVagaOnPedidoCanceladoHandler"/> para liberar a vaga associada.
/// </para>
/// </summary>
public sealed class CancelarPedidosAbandonadosBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<CancelarPedidosAbandonadosBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TimeoutAbandonado = TimeSpan.FromMinutes(30);
    private const int MaxBatch = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Intervalo, stoppingToken).ConfigureAwait(false);

            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                await RodadaAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "CancelarPedidosAbandonados erro inesperado.");
            }
        }
    }

    /// <summary>
    /// Uma rodada com a trava (#1575): API e Worker registram este serviço e quem não pega a trava pula,
    /// para o mesmo pedido não ser cancelado pelos dois ao mesmo tempo. Pública para os testes de integração.
    /// </summary>
    public Task RodadaAsync(CancellationToken ct) => PostgresAdvisoryLock.TentarExecutarEmEscopoProprioAsync(
        scopeFactory, LockKeys.PedidosAbandonados, ProcessarBatchAsync, ct);

    private async Task ProcessarBatchAsync(CancellationToken ct)
    {
        IReadOnlyList<EasyStock.Domain.Entities.Pedido> pedidosExpirados;
        using (var scope = scopeFactory.CreateScope())
        {
            var pedidoRepo = scope.ServiceProvider.GetRequiredService<IPedidoStorefrontRepository>();
            var limite = DateTime.UtcNow - TimeoutAbandonado;
            pedidosExpirados = await pedidoRepo.GetAbandonadosNoCheckoutAsync(limite, MaxBatch, ct);
        }

        if (pedidosExpirados.Count == 0) return;

        logger.LogInformation(
            "CancelarPedidosAbandonados processando {Count} pedidos expirados.",
            pedidosExpirados.Count);

        foreach (var expirado in pedidosExpirados)
        {
            try
            {
                await CancelarAbandonadoAsync(expirado.Id, expirado.EmpresaId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "CancelarPedidosAbandonados erro ao cancelar pedidoId={PedidoId}.", expirado.Id);
            }
        }
    }

    /// <summary>
    /// Cancela um pedido no tenant dele (#1291): a varredura é cross-tenant, mas <c>pedidos</c> tem RLS com
    /// FORCE e a escrita precisa do tenant. Escopo próprio por pedido: a releitura rastreada traz o
    /// <c>xmin</c> (a lista da varredura é sem rastreio) e uma falha não contamina os seguintes. Pedido que
    /// saiu de <c>AguardandoPagamento</c> no meio do caminho (pago, cobrado) fica como está.
    /// </summary>
    /// <summary>Detalhe do evento gravado no pedido que a varredura cancela (#1726).</summary>
    public const string MotivoAbandonado = "abandonado_sem_cobranca";

    public async Task CancelarAbandonadoAsync(Guid pedidoId, Guid empresaId, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<ITenantContextAccessor>().SetCurrentTenant(empresaId);
        var pedidoRepo = services.GetRequiredService<IPedidoStorefrontRepository>();

        var pedido = await pedidoRepo.GetByIdAsync(pedidoId, ct);
        if (pedido is null || pedido.Status is not (StatusPedidoMapper.AguardandoPagamento or StatusPedidoMapper.Rascunho))
            return;
        var statusAntigo = pedido.Status;

        pedido.Status = StatusPedidoMapper.Cancelado;
        pedido.CanceladoEm = DateTime.UtcNow;
        pedido.AlteradoEm = DateTime.UtcNow;

        // #1302: a liberação só marca a entidade. Vem antes do UpdateAsync, o único SaveChanges
        // do escopo, para o pedido cancelado e a vaga liberada gravarem juntos.
        var handler = new LiberarVagaOnPedidoCanceladoHandler(
            services.GetRequiredService<IVagaOcupadaRepository>(),
            services.GetRequiredService<ILogger<LiberarVagaOnPedidoCanceladoHandler>>());
        await handler.HandleAsync(new PedidoCanceladoEvent(pedido.Id, Guid.Empty, "Timeout 30 min sem pagamento"), ct);
        // #1726: o cancelamento automático deixa trilha no pedido. O evento grava junto com o pedido e a vaga.
        await pedidoRepo.AddEventoAsync(new Domain.Entities.PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "cancelado",
            StatusAntigo = statusAntigo,
            StatusNovo = StatusPedidoMapper.Cancelado,
            Detalhes = MotivoAbandonado,
            UsuarioNome = "Sistema",
            Origem = "sistema",
            OcorridoEm = pedido.CanceladoEm.Value,
        }, ct);
        await pedidoRepo.UpdateAsync(pedido, ct);

        logger.LogInformation("CancelarPedidosAbandonados cancelado pedidoId={PedidoId}.", pedido.Id);
    }
}
