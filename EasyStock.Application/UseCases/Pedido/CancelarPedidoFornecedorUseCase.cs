namespace EasyStock.Application.UseCases.Pedido;

public sealed record CancelarPedidoFornecedorCommand(Guid PedidoId, Guid EmpresaId);

public class CancelarPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoRepository,
    IUnitOfWork unitOfWork,
    ILogger<CancelarPedidoFornecedorUseCase> logger)
{
    public async Task ExecuteAsync(CancelarPedidoFornecedorCommand command)
    {
        var pedido = await pedidoRepository.GetByIdAsync(command.PedidoId)
            ?? throw new RegraDeDominioVioladaException("Pedido não encontrado.");

        if (pedido.EmpresaId != command.EmpresaId)
            throw new RegraDeDominioVioladaException("Pedido não pertence a esta empresa.");

        if (pedido.Status == StatusPedidoFornecedor.Recebido)
            throw new RegraDeDominioVioladaException("Pedido já recebido não pode ser cancelado.");

        if (pedido.Status == StatusPedidoFornecedor.RecebidoParcial)
            throw new RegraDeDominioVioladaException(
                "Pedido com recebimento parcial não pode ser cancelado — parte ja entrou no estoque. " +
                "Faca devolucao/estorno antes.");

        if (pedido.Status == StatusPedidoFornecedor.Cancelado)
            throw new RegraDeDominioVioladaException("Pedido já está cancelado.");

        pedido.Status = StatusPedidoFornecedor.Cancelado;
        pedido.AlteradoEm = DateTime.UtcNow;

        await pedidoRepository.UpdateAsync(pedido);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Pedido {PedidoId} cancelado.", pedido.Id);
    }
}
