using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.CompletarOnboarding;

public sealed record CompletarOnboardingCommand(
    Guid EmpresaId,
    string? NomeFantasia,
    string? Telefone,
    string Segmento,
    string LojaNome,
    string? LojaEndereco,
    string? LojaTelefone);

public sealed record CompletarOnboardingResult(
    Guid EmpresaId,
    Guid LojaId,
    bool JaEstavaCompleto);

public class CompletarOnboardingUseCase(
    IEmpresaRepository empresaRepository,
    ILojaRepository lojaRepository,
    IUnitOfWork unitOfWork,
    ILogger<CompletarOnboardingUseCase> logger)
{
    public async Task<CompletarOnboardingResult> ExecuteAsync(CompletarOnboardingCommand command)
    {
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

        if (string.IsNullOrWhiteSpace(command.LojaNome))
            throw new UseCaseValidationException("Nome da loja e obrigatorio.");

        if (!SegmentoEmpresa.EhValido(command.Segmento))
            throw new UseCaseValidationException("Segmento invalido. Valores aceitos: " + string.Join(", ", SegmentoEmpresa.Validos));

        var empresa = await empresaRepository.GetByIdAsync(command.EmpresaId)
            ?? throw new UseCaseValidationException("Empresa nao encontrada.");

        // Idempotente: chamadas repetidas nao recriam loja nem sobreescrevem dados.
        if (empresa.OnboardingCompleto)
        {
            var lojas = await lojaRepository.GetByEmpresaAsync(empresa.Id);
            var primeira = lojas.FirstOrDefault();
            return new CompletarOnboardingResult(empresa.Id, primeira?.Id ?? Guid.Empty, JaEstavaCompleto: true);
        }

        empresa.NomeFantasia = string.IsNullOrWhiteSpace(command.NomeFantasia) ? null : command.NomeFantasia.Trim();
        empresa.Telefone = string.IsNullOrWhiteSpace(command.Telefone) ? null : command.Telefone.Trim();
        empresa.Segmento = command.Segmento.Trim().ToLowerInvariant();
        empresa.MarcarOnboardingCompleto();

        await empresaRepository.UpdateAsync(empresa);

        var loja = Domain.Entities.Loja.Criar(empresa.Id, command.LojaNome.Trim());
        loja.Endereco = string.IsNullOrWhiteSpace(command.LojaEndereco) ? null : command.LojaEndereco.Trim();
        loja.Telefone = string.IsNullOrWhiteSpace(command.LojaTelefone) ? null : command.LojaTelefone.Trim();
        await lojaRepository.AddAsync(loja);

        await unitOfWork.CommitAsync();

        logger.LogInformation(
            "Onboarding completo. EmpresaId={EmpresaId} Segmento={Segmento} LojaId={LojaId}",
            empresa.Id, empresa.Segmento, loja.Id);

        return new CompletarOnboardingResult(empresa.Id, loja.Id, JaEstavaCompleto: false);
    }
}
