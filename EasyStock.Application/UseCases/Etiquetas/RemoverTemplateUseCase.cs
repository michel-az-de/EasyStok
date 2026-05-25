using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;

namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record RemoverTemplateCommand(
    Guid EmpresaId, Guid Id,
    Guid? OperadorId, string? Ip, string? UserAgent);

public sealed record RemoverTemplateResult(bool Removido, int SnapshotsExistentes);

public class RemoverTemplateUseCase(
    IEtiquetaTemplateRepository repo,
    IAuditLogRepository auditRepo,
    IUnitOfWork uow)
{
    public async Task<RemoverTemplateResult> ExecuteAsync(RemoverTemplateCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var template = await repo.GetEmpresaByIdAsync(cmd.EmpresaId, cmd.Id);
        if (template == null) return new(false, 0);

        var snapshots = await repo.CountSnapshotsByTemplateIdAsync(cmd.Id);

        await repo.RemoveEmpresaAsync(template);

        if (cmd.OperadorId.HasValue)
        {
            await auditRepo.AddAsync(AuditLogEntity.Criar(
                cmd.OperadorId.Value, "etiqueta-template.excluido", true,
                $"Id: {template.Id}, Nome: {template.Nome}, Snapshots: {snapshots}",
                cmd.Ip, cmd.UserAgent));
        }

        await uow.CommitAsync();
        return new(true, snapshots);
    }
}
