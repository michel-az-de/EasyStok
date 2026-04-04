using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AtualizarFornecedor;

public sealed record AtualizarFornecedorCommand(
    Guid FornecedorId,
    Guid EmpresaId,
    string Nome,
    string? Documento,
    string? Email,
    string? Telefone,
    string? Contato);

public class AtualizarFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IUnitOfWork unitOfWork,
    ILogger<AtualizarFornecedorUseCase> logger)
{
    public async Task ExecuteAsync(AtualizarFornecedorCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Nome))
            throw new UseCaseValidationException("Nome do fornecedor e obrigatorio.");

        var fornecedor = await fornecedorRepository.GetByIdAsync(command.FornecedorId);
        if (fornecedor is null || fornecedor.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        fornecedor.Nome = command.Nome.Trim();
        fornecedor.Documento = command.Documento?.Trim();
        fornecedor.Email = command.Email?.Trim();
        fornecedor.Telefone = command.Telefone?.Trim();
        fornecedor.Contato = command.Contato?.Trim();
        fornecedor.AlteradoEm = DateTime.UtcNow;

        await fornecedorRepository.UpdateAsync(fornecedor);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Fornecedor {FornecedorId} atualizado.", fornecedor.Id);
    }
}
