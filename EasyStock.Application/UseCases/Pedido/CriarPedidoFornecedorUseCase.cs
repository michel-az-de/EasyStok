using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Pedido;

public sealed record CriarPedidoFornecedorCommand(
    Guid EmpresaId,
    Guid FornecedorId,
    DateTime DataPedido,
    DateTime? PrevisaoEntrega,
    decimal? ValorEstimado,
    string? Canal,
    string? Observacoes);

public sealed record CriarPedidoFornecedorResult(Guid PedidoId);

public class CriarPedidoFornecedorUseCase(
    IPedidoFornecedorRepository pedidoRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarPedidoFornecedorUseCase> logger)
{
    public async Task<CriarPedidoFornecedorResult> ExecuteAsync(CriarPedidoFornecedorCommand command)
    {
        var pedido = new PedidoFornecedor
        {
            Id = Guid.NewGuid(),
            EmpresaId = command.EmpresaId,
            FornecedorId = command.FornecedorId,
            DataPedido = command.DataPedido,
            PrevisaoEntrega = command.PrevisaoEntrega,
            ValorEstimado = command.ValorEstimado,
            Status = StatusPedidoFornecedor.Aberto,
            Canal = command.Canal,
            Observacoes = command.Observacoes,
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };

        await pedidoRepository.AddAsync(pedido);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Pedido {PedidoId} criado para fornecedor {FornecedorId}.", pedido.Id, pedido.FornecedorId);
        return new CriarPedidoFornecedorResult(pedido.Id);
    }
}
