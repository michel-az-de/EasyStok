using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.GerenciarLoja
{
    public sealed record CriarLojaCommand(
        Guid EmpresaId,
        string Nome,
        string? Descricao,
        string? Documento,
        string? Endereco,
        string? Telefone);

    public sealed record AtualizarLojaCommand(
        Guid LojaId,
        Guid EmpresaId,
        string Nome,
        string? Descricao,
        string? Documento,
        string? Endereco,
        string? Telefone);

    public sealed record LojaResult(
        Guid Id,
        Guid EmpresaId,
        string Nome,
        bool Ativa);

    public class GerenciarLojaUseCase(
        ILojaRepository lojaRepository,
        IAssinaturaEmpresaRepository assinaturaRepository,
        IUnitOfWork unitOfWork,
        ILogger<GerenciarLojaUseCase> logger)
    {
        public async Task<LojaResult> CriarAsync(CriarLojaCommand command)
        {
            if (command.EmpresaId == Guid.Empty)
                throw new UseCaseValidationException("EmpresaId e obrigatorio.");
            if (string.IsNullOrWhiteSpace(command.Nome))
                throw new UseCaseValidationException("Nome da loja e obrigatorio.");

            var assinatura = await assinaturaRepository.GetAtivaAsync(command.EmpresaId);

            if (assinatura is not null && assinatura.Plano!.LimiteLojas != -1)
            {
                var count = await lojaRepository.CountByEmpresaAsync(command.EmpresaId);
                if (count >= assinatura.Plano.LimiteLojas)
                    throw new PlanoLimiteAtingidoException("lojas");
            }

            var loja = Loja.Criar(command.EmpresaId, command.Nome.Trim());
            loja.Descricao = command.Descricao?.Trim();
            loja.Documento = command.Documento?.Trim();
            loja.Endereco = command.Endereco?.Trim();
            loja.Telefone = command.Telefone?.Trim();

            await lojaRepository.AddAsync(loja);
            await unitOfWork.CommitAsync();

            logger.LogInformation("Loja {LojaId} criada para empresa {EmpresaId}.", loja.Id, loja.EmpresaId);

            return ToResult(loja);
        }

        public async Task<LojaResult> AtualizarAsync(AtualizarLojaCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.Nome))
                throw new UseCaseValidationException("Nome da loja e obrigatorio.");

            var loja = await lojaRepository.GetByIdAsync(command.LojaId);
            if (loja is null || loja.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("Loja nao encontrada.");

            loja.Nome = command.Nome.Trim();
            loja.Descricao = command.Descricao?.Trim();
            loja.Documento = command.Documento?.Trim();
            loja.Endereco = command.Endereco?.Trim();
            loja.Telefone = command.Telefone?.Trim();
            loja.AlteradoEm = DateTime.UtcNow;

            await lojaRepository.UpdateAsync(loja);
            await unitOfWork.CommitAsync();

            logger.LogInformation("Loja {LojaId} atualizada.", loja.Id);

            return ToResult(loja);
        }

        public async Task DesativarAsync(Guid lojaId, Guid empresaId)
        {
            var loja = await lojaRepository.GetByIdAsync(lojaId);
            if (loja is null || loja.EmpresaId != empresaId)
                throw new UseCaseValidationException("Loja nao encontrada.");

            loja.Ativa = false;
            loja.AlteradoEm = DateTime.UtcNow;

            await lojaRepository.UpdateAsync(loja);
            await unitOfWork.CommitAsync();

            logger.LogInformation("Loja {LojaId} desativada.", loja.Id);
        }

        public async Task<IEnumerable<LojaResult>> ListarAsync(Guid empresaId)
        {
            var lojas = await lojaRepository.GetByEmpresaAsync(empresaId);
            return lojas.Select(ToResult);
        }

        private static LojaResult ToResult(Loja l) => new(l.Id, l.EmpresaId, l.Nome, l.Ativa);
    }
}
