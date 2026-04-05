using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.PedidoFornecedor;

public sealed record TransicionarStatusPedidoFornecedorCommand(
    Guid PedidoId,
    Guid EmpresaId,
    StatusPedidoFornecedor NovoStatus,
    string? Tracking = null);

public class TransicionarStatusPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoFornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<TransicionarStatusPedidoFornecedorUseCase> logger)
{
    public async Task ExecuteAsync(TransicionarStatusPedidoFornecedorCommand command)
    {
        if (command.NovoStatus == StatusPedidoFornecedor.Recebido)
            throw new UseCaseValidationException(
                "Para registrar o recebimento utilize o endpoint dedicado POST /receber.");

        var pedido = await pedidoFornecedorRepository.GetByIdAsync(command.PedidoId)
            ?? throw new UseCaseValidationException("Pedido de fornecedor nao encontrado.");

        if (pedido.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Pedido de fornecedor nao encontrado.");

        try
        {
            switch (command.NovoStatus)
            {
                case StatusPedidoFornecedor.EmTransito:
                    pedido.IniciarTransito(command.Tracking);
                    break;
                case StatusPedidoFornecedor.Cancelado:
                    pedido.Cancelar();
                    break;
                default:
                    throw new UseCaseValidationException($"Status '{command.NovoStatus}' nao e suportado neste endpoint.");
            }
        }
        catch (RegraDeDominioVioladaException ex)
        {
            throw new UseCaseValidationException(ex.Message);
        }

        await pedidoFornecedorRepository.UpdateAsync(pedido);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Pedido {PedidoId} transicionado para {Status}.", pedido.Id, pedido.Status);
    }
}
