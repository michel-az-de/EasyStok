using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.ReativarFornecedor;

public sealed record ReativarFornecedorCommand(Guid FornecedorId, Guid EmpresaId);

public class ReativarFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<ReativarFornecedorUseCase> logger)
{
    public async Task ExecuteAsync(ReativarFornecedorCommand command)
    {
        var fornecedor = await fornecedorRepository.GetByIdAsync(command.EmpresaId, command.FornecedorId);
        if (fornecedor is null)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        fornecedor.Ativo = true;
        fornecedor.AlteradoEm = DateTime.UtcNow;

        await fornecedorRepository.UpdateAsync(fornecedor);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Fornecedor {FornecedorId} reativado.", fornecedor.Id);
    }
}
