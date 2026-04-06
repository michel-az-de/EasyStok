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
    string? Contato,
    string? Categoria,
    string? Tipo,
    int? LeadTimeEstimadoDias,
    string? SiteUrl,
    string? PedidoMinimo,
    string? FretePadrao,
    string? Observacoes);

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

        fornecedor.AtualizarCadastro(
            command.Nome,
            command.Documento,
            command.Email,
            command.Telefone,
            command.Contato,
            command.Categoria,
            command.Tipo,
            command.LeadTimeEstimadoDias,
            command.SiteUrl,
            command.PedidoMinimo,
            command.FretePadrao,
            command.Observacoes);

        await fornecedorRepository.UpdateAsync(fornecedor);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Fornecedor {FornecedorId} atualizado.", fornecedor.Id);
    }
}
