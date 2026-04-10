using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Pedido;

public sealed record ReceberPedidoFornecedorCommand(Guid PedidoId, Guid EmpresaId, DateTime? DataRecebimento, string? Tracking);

public class ReceberPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoRepository,
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

        pedido.Status = StatusPedidoFornecedor.Recebido;
        pedido.DataRecebimento = command.DataRecebimento ?? DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(command.Tracking))
            pedido.Tracking = command.Tracking;
        pedido.AlteradoEm = DateTime.UtcNow;

        await pedidoRepository.UpdateAsync(pedido);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Pedido {PedidoId} marcado como recebido.", pedido.Id);
    }
}
