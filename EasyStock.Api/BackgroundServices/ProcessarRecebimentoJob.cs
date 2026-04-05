using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Job para processar recebimentos de pedidos de fornecedor.
/// Executa periodicamente para atualizar status de pedidos e gerar movimentações.
/// </summary>
public sealed class ProcessarRecebimentoJob(
    IServiceProvider serviceProvider,
    ILogger<ProcessarRecebimentoJob> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30); // Executa a cada 30 minutos

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Job de processamento de recebimentos iniciado");

        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessarRecebimentosAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no processamento de recebimentos");
            }
        }
    }

    private async Task ProcessarRecebimentosAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var pedidoRepo = scope.ServiceProvider.GetRequiredService<IPedidoFornecedorRepository>();
        var itemEstoqueRepo = scope.ServiceProvider.GetRequiredService<IItemEstoqueRepository>();
        var movimentacaoRepo = scope.ServiceProvider.GetRequiredService<IMovimentacaoEstoqueRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Buscar pedidos aguardando recebimento
        var pedidosAguardando = await pedidoRepo.GetPedidosAguardandoRecebimentoAsync();

        foreach (var pedido in pedidosAguardando)
        {
            try
            {
                await ProcessarPedidoAsync(pedido, pedidoRepo, itemEstoqueRepo, movimentacaoRepo, unitOfWork, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao processar pedido {PedidoId}", pedido.Id);
            }
        }
    }

    private async Task ProcessarPedidoAsync(
        PedidoFornecedor pedido,
        IPedidoFornecedorRepository pedidoRepo,
        IItemEstoqueRepository itemEstoqueRepo,
        IMovimentacaoEstoqueRepository movimentacaoRepo,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        // Simulação: marcar como recebido se passou da data prevista
        if (pedido.DataPrevisaoEntrega.HasValue && DateTime.UtcNow > pedido.DataPrevisaoEntrega.Value)
        {
            pedido.MarcarComoRecebido(DateTime.UtcNow);
            await pedidoRepo.UpdateAsync(pedido);

            // Gerar movimentações de entrada para cada item do pedido
            foreach (var item in pedido.Itens)
            {
                // Encontrar ou criar item de estoque
                var itemEstoque = await itemEstoqueRepo.GetByIdAsync(pedido.EmpresaId, item.ItemEstoqueId);
                if (itemEstoque != null)
                {
                    // Registrar entrada
                    var movimentacao = MovimentacaoEstoque.CriarEntrada(
                        Guid.NewGuid(),
                        pedido.EmpresaId,
                        itemEstoque,
                        NaturezaMovimentacaoEstoque.Compra,
                        item.QuantidadeRecebida,
                        item.CustoUnitario,
                        DateTime.UtcNow,
                        $"Recebimento pedido {pedido.Numero}",
                        pedido.Numero,
                        DateTime.UtcNow);

                    await movimentacaoRepo.InsertAsync(movimentacao);

                    // Atualizar quantidade no estoque
                    itemEstoque.RegistrarReposicao(item.QuantidadeRecebida, DateTime.UtcNow);
                    await itemEstoqueRepo.UpdateAsync(itemEstoque);
                }
            }

            await unitOfWork.CommitAsync();

            logger.LogInformation("Pedido {PedidoId} processado com sucesso", pedido.Id);
        }
    }
}