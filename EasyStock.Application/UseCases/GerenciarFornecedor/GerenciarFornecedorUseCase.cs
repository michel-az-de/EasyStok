using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.GerenciarFornecedor
{
    public sealed record CriarFornecedorCommand(
        Guid EmpresaId,
        string Nome,
        string? Documento,
        string? Email,
        string? Telefone,
        string? Contato);

    public sealed record AtualizarFornecedorCommand(
        Guid FornecedorId,
        Guid EmpresaId,
        string Nome,
        string? Documento,
        string? Email,
        string? Telefone,
        string? Contato);

    public sealed record FornecedorResult(
        Guid Id,
        Guid EmpresaId,
        string Nome,
        bool Ativo);

    public class GerenciarFornecedorUseCase(
        IFornecedorRepository fornecedorRepository,
        IUnitOfWork unitOfWork,
        ILogger<GerenciarFornecedorUseCase> logger)
    {
        public async Task<FornecedorResult> CriarAsync(CriarFornecedorCommand command)
        {
            if (command.EmpresaId == Guid.Empty)
                throw new UseCaseValidationException("EmpresaId e obrigatorio.");
            if (string.IsNullOrWhiteSpace(command.Nome))
                throw new UseCaseValidationException("Nome do fornecedor e obrigatorio.");

            var fornecedor = Fornecedor.Criar(command.EmpresaId, command.Nome.Trim());
            fornecedor.Documento = command.Documento?.Trim();
            fornecedor.Email = command.Email?.Trim();
            fornecedor.Telefone = command.Telefone?.Trim();
            fornecedor.Contato = command.Contato?.Trim();

            await fornecedorRepository.AddAsync(fornecedor);
            await unitOfWork.CommitAsync();

            logger.LogInformation("Fornecedor {FornecedorId} criado para empresa {EmpresaId}.", fornecedor.Id, fornecedor.EmpresaId);

            return ToResult(fornecedor);
        }

        public async Task<FornecedorResult> AtualizarAsync(AtualizarFornecedorCommand command)
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

            return ToResult(fornecedor);
        }

        public async Task DesativarAsync(Guid fornecedorId, Guid empresaId)
        {
            var fornecedor = await fornecedorRepository.GetByIdAsync(fornecedorId);
            if (fornecedor is null || fornecedor.EmpresaId != empresaId)
                throw new UseCaseValidationException("Fornecedor nao encontrado.");

            fornecedor.Ativo = false;
            fornecedor.AlteradoEm = DateTime.UtcNow;

            await fornecedorRepository.UpdateAsync(fornecedor);
            await unitOfWork.CommitAsync();

            logger.LogInformation("Fornecedor {FornecedorId} desativado.", fornecedor.Id);
        }

        public async Task<(IEnumerable<FornecedorResult>, int)> ListarAsync(Guid empresaId, int page, int pageSize)
        {
            var (fornecedores, total) = await fornecedorRepository.GetByEmpresaAsync(empresaId, page, pageSize);
            return (fornecedores.Select(ToResult), total);
        }

        private static FornecedorResult ToResult(Fornecedor f) => new(f.Id, f.EmpresaId, f.Nome, f.Ativo);
    }
}
