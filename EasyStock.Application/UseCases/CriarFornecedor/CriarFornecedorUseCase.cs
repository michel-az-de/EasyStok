using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Fornecedor;
using Microsoft.Extensions.Logging;
using FornecedorEntity = EasyStock.Domain.Entities.Fornecedor;

namespace EasyStock.Application.UseCases.CriarFornecedor;

public sealed record CriarFornecedorCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(150)] string Nome,
    [property: MaxLength(30)] string? Documento,
    [property: MaxLength(255)] string? Email,
    [property: MaxLength(20)] string? Telefone,
    [property: MaxLength(150)] string? Contato);

public class CriarFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IAssinaturaEmpresaRepository assinaturaRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarFornecedorUseCase> logger)
{
    public async Task<FornecedorResult> ExecuteAsync(CriarFornecedorCommand command)
    {
        var fornecedor = FornecedorEntity.Criar(command.EmpresaId, command.Nome.Trim());
        fornecedor.Documento = command.Documento?.Trim();
        fornecedor.Email = command.Email?.Trim();
        fornecedor.Telefone = command.Telefone?.Trim();
        fornecedor.Contato = command.Contato?.Trim();

        await fornecedorRepository.AddAsync(fornecedor);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Fornecedor {FornecedorId} criado para empresa {EmpresaId}.", fornecedor.Id, fornecedor.EmpresaId);

        return new FornecedorResult(fornecedor.Id, fornecedor.EmpresaId, fornecedor.Nome, fornecedor.Ativo);
    }
}
