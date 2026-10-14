using EasyStock.Application.UseCases.Financeiro.Integracao;

namespace EasyStock.Application.UseCases.Pedido;

public sealed record ReceberPedidoFornecedorCommand(Guid PedidoId, Guid EmpresaId, DateTime? DataRecebimento, string? Tracking);

public class ReceberPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoRepository,
    IPedidoFornecedorItemRepository itemRepository,
    ReceberPedidoCompletoUseCase receberCompletoUseCase,
    GerarContaPagarDePedidoFornecedorUseCase gerarContaPagarUseCase,
    IUnitOfWork unitOfWork,
    ILogger<ReceberPedidoFornecedorUseCase> logger)
{
    public async Task ExecuteAsync(ReceberPedidoFornecedorCommand command)
    {
        var pedido = await pedidoRepository.GetByIdAsync(command.PedidoId)
            ?? throw new RegraDeDominioVioladaException("Pedido não encontrado.");

        if (pedido.EmpresaId != command.EmpresaId)
            throw new RegraDeDominioVioladaException("Pedido não pertence a esta empresa.");

        if (pedido.Status == StatusPedidoFornecedor.Cancelado)
            throw new RegraDeDominioVioladaException("Pedido cancelado não pode ser recebido.");

        if (pedido.Status == StatusPedidoFornecedor.Recebido)
            throw new RegraDeDominioVioladaException("Pedido já foi recebido.");

        // RecebidoParcial: este UseCase nao processa entrada de estoque por item.
        // Forcar uso de ProcessarRecebimentoPedidoFornecedorUseCase para fechar.
        if (pedido.Status == StatusPedidoFornecedor.RecebidoParcial)
            throw new RegraDeDominioVioladaException(
                "Pedido com recebimento parcial — use ProcessarRecebimento com itens para concluir.");

        // Pedido com itens: só mudar o status deixava o estoque de fora e, como já constava
        // Recebido, o receber-tudo respondia "já recebido" e a entrada nunca acontecia.
        // Delegar ao recebimento completo dá entrada por item e fecha o status.
        var temItens = (await itemRepository.GetByPedidoIdAsync(pedido.Id)).Any();
        if (temItens)
        {
            await receberCompletoUseCase.ExecuteAsync(
                new ReceberPedidoCompletoCommand(pedido.Id, pedido.EmpresaId, command.DataRecebimento));
            pedido = await pedidoRepository.GetByIdAsync(command.PedidoId) ?? pedido;
        }
        else
        {
            pedido.Status = StatusPedidoFornecedor.Recebido;
            pedido.DataRecebimento = command.DataRecebimento ?? DateTime.UtcNow;
        }

        if (!string.IsNullOrWhiteSpace(command.Tracking))
            pedido.Tracking = command.Tracking;
        pedido.AlteradoEm = DateTime.UtcNow;

        await pedidoRepository.UpdateAsync(pedido);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Pedido {PedidoId} marcado como recebido.", pedido.Id);

        // Integracao automatica CAP/CAR (P1): best-effort — falha aqui nao reverte
        // recebimento (idempotencia via OrigemRefId permite retry).
        try
        {
            await gerarContaPagarUseCase.ExecuteAsync(
                new GerarContaPagarDePedidoFornecedorCommand(pedido.EmpresaId, pedido));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Falha ao gerar ContaPagar automatica pra PedidoFornecedor {PedidoId} — recebimento mantido.",
                pedido.Id);
        }
    }
}
