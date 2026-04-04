using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.DesativarFornecedor;

public sealed record DesativarFornecedorCommand(Guid FornecedorId, Guid EmpresaId);

public class DesativarFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<DesativarFornecedorUseCase> logger)
{
    public async Task ExecuteAsync(DesativarFornecedorCommand command)
    {
        var fornecedor = await fornecedorRepository.GetByIdAsync(command.FornecedorId);
        if (fornecedor is null || fornecedor.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        fornecedor.Ativo = false;
        fornecedor.AlteradoEm = DateTime.UtcNow;

        await fornecedorRepository.UpdateAsync(fornecedor);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Fornecedor {FornecedorId} desativado.", fornecedor.Id);
    }
}
