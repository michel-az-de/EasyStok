using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.ValueObjects;
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
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        if (string.IsNullOrWhiteSpace(command.Nome))
            throw new UseCaseValidationException("Nome do fornecedor é obrigatório.");
        if (!string.IsNullOrWhiteSpace(command.Email))
            EmailValidator.EnsureValid(command.Email, "Email do fornecedor");

        var fornecedor = await fornecedorRepository.GetByIdAsync(command.FornecedorId);
        if (fornecedor is null || fornecedor.EmpresaId != command.EmpresaId)
            throw new UseCaseValidationException("Fornecedor nao encontrado.");

        // Normalização via VOs: se o formato for válido, armazena só os dígitos;
        // se for inválido, mantém o input original (tolerância para dados legados).
        var documentoNormalizado = Cnpj.TryFrom(command.Documento)?.Value ?? command.Documento;
        var telefoneNormalizado  = Telefone.TryFrom(command.Telefone)?.Value ?? command.Telefone;

        fornecedor.AtualizarCadastro(
            command.Nome,
            documentoNormalizado,
            command.Email,
            telefoneNormalizado,
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
