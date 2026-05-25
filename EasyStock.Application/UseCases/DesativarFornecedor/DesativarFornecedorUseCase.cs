using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.DesativarFornecedor;

public sealed record DesativarFornecedorCommand(Guid FornecedorId, Guid EmpresaId);

public class DesativarFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IPedidoFornecedorRepository pedidoFornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<DesativarFornecedorUseCase> logger)
{
    public async Task ExecuteAsync(DesativarFornecedorCommand command)
    {
        var fornecedor = await fornecedorRepository.GetByIdAsync(command.EmpresaId, command.FornecedorId);
        if (fornecedor is null)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        var pedidosEmAberto = await pedidoFornecedorRepository.CountPedidosAbertosOuEmTransitoAsync(command.EmpresaId, command.FornecedorId);
        if (pedidosEmAberto > 0)
            throw new UseCaseValidationException("Nao e permitido desativar fornecedor com pedido aberto ou em transito.");

        fornecedor.Desativar();

        await fornecedorRepository.UpdateAsync(fornecedor);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Fornecedor {FornecedorId} desativado.", fornecedor.Id);
    }
}
