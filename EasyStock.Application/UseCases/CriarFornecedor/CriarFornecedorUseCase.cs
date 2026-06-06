using EasyStock.Application.UseCases.Fornecedor;
using EasyStock.Domain.ValueObjects;
using FornecedorEntity = EasyStock.Domain.Entities.Fornecedor;

namespace EasyStock.Application.UseCases.CriarFornecedor;

public sealed record CriarFornecedorCommand(
    [property: Required] Guid EmpresaId,
    [property: Required][property: MaxLength(150)] string Nome,
    [property: MaxLength(30)] string? Documento,
    [property: MaxLength(255)] string? Email,
    [property: MaxLength(20)] string? Telefone,
    [property: MaxLength(150)] string? Contato,
    [property: MaxLength(120)] string? Categoria = null,
    [property: MaxLength(60)] string? Tipo = null,
    int? LeadTimeEstimadoDias = null,
    [property: MaxLength(255)] string? SiteUrl = null,
    [property: MaxLength(120)] string? PedidoMinimo = null,
    [property: MaxLength(120)] string? FretePadrao = null,
    string? Observacoes = null);

public class CriarFornecedorUseCase(
    IFornecedorRepository fornecedorRepository,
    IAssinaturaEmpresaRepository assinaturaRepository,
    IUnitOfWork unitOfWork,
    ILogger<CriarFornecedorUseCase> logger)
{
    public async Task<FornecedorResult> ExecuteAsync(CriarFornecedorCommand command)
    {
        _ = assinaturaRepository;
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
        UseCaseGuards.EnsureSemTagsHtml(command.Nome, "Nome do fornecedor");
        if (!string.IsNullOrWhiteSpace(command.Email))
            EmailValidator.EnsureValid(command.Email, "Email do fornecedor");

        // Normalização via VOs: se o formato for válido, armazena só os dígitos;
        // se for inválido, mantém o input original (tolerância para dados legados
        // ou fornecedores estrangeiros que não batem com CPF/CNPJ brasileiro).
        var documentoNormalizado = Cnpj.TryFrom(command.Documento)?.Value ?? command.Documento;
        var telefoneNormalizado  = Telefone.TryFrom(command.Telefone)?.Value ?? command.Telefone;

        var fornecedor = FornecedorEntity.Criar(command.EmpresaId, command.Nome.Trim());
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

        await fornecedorRepository.AddAsync(fornecedor);
        await unitOfWork.CommitAsync();

        logger.LogInformation("Fornecedor {FornecedorId} criado para empresa {EmpresaId}.", fornecedor.Id, fornecedor.EmpresaId);

        return new FornecedorResult(
            fornecedor.Id,
            fornecedor.EmpresaId,
            fornecedor.Nome,
            fornecedor.Ativo,
            fornecedor.Documento,
            fornecedor.Email,
            fornecedor.Telefone,
            fornecedor.Contato,
            fornecedor.Categoria,
            fornecedor.Tipo,
            fornecedor.LeadTimeEstimadoDias,
            fornecedor.LeadTimeRealMedioDias,
            fornecedor.SiteUrl,
            fornecedor.PedidoMinimo,
            fornecedor.FretePadrao,
            fornecedor.Observacoes);
    }
}
