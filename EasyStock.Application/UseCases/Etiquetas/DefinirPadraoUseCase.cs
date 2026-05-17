using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;

namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record DefinirPadraoCommand(
    Guid EmpresaId,
    string TemplateOrigem,  // "Sistema" | "Empresa"
    Guid TemplateId,
    Guid? OperadorId, string? Ip, string? UserAgent);

public class DefinirPadraoUseCase(
    IEtiquetaTemplateRepository repo,
    IAuditLogRepository auditRepo,
    IUnitOfWork uow)
{
    public async Task<EtiquetaEmpresaDefaultResult> ExecuteAsync(DefinirPadraoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        if (cmd.TemplateOrigem != "Sistema" && cmd.TemplateOrigem != "Empresa")
            throw new UseCaseValidationException("TemplateOrigem deve ser 'Sistema' ou 'Empresa'.");

        // Valida existência
        if (cmd.TemplateOrigem == "Sistema")
        {
            var t = await repo.GetSistemaByIdAsync(cmd.TemplateId);
            if (t == null) throw new UseCaseValidationException("Modelo de sistema não encontrado.");
        }
        else
        {
            var t = await repo.GetEmpresaByIdAsync(cmd.EmpresaId, cmd.TemplateId);
            if (t == null) throw new UseCaseValidationException("Modelo personalizado não encontrado.");
        }

        var agora = DateTime.UtcNow;
        var entry = new EtiquetaEmpresaDefault
        {
            EmpresaId      = cmd.EmpresaId,
            TemplateOrigem = cmd.TemplateOrigem,
            TemplateId     = cmd.TemplateId,
            AlteradoEm     = agora,
        };

        await repo.UpsertDefaultAsync(entry);

        if (cmd.OperadorId.HasValue)
        {
            await auditRepo.AddAsync(AuditLogEntity.Criar(
                cmd.OperadorId.Value, "etiqueta-template.padrao-definido", true,
                $"Origem: {cmd.TemplateOrigem}, Id: {cmd.TemplateId}", cmd.Ip, cmd.UserAgent));
        }

        await uow.CommitAsync();
        return new(cmd.EmpresaId, cmd.TemplateOrigem, cmd.TemplateId, agora);
    }
}
