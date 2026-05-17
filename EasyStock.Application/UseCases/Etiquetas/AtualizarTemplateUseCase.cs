using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.Validators;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;

namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record AtualizarTemplateCommand(
    Guid EmpresaId,
    Guid Id,
    string Nome,
    string LayoutJson,
    Guid? OperadorId,
    string? Ip,
    string? UserAgent);

public class AtualizarTemplateUseCase(
    IEtiquetaTemplateRepository repo,
    IAuditLogRepository auditRepo,
    IUnitOfWork uow)
{
    private static readonly LayoutJsonValidator Validator = new();

    public async Task<EtiquetaTemplateResult?> ExecuteAsync(AtualizarTemplateCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var template = await repo.GetEmpresaByIdAsync(cmd.EmpresaId, cmd.Id);
        if (template == null) return null;

        var doc = DeserializeLayout(cmd.LayoutJson);
        var validationResult = await Validator.ValidateAsync(doc);
        if (!validationResult.IsValid)
            throw new UseCaseValidationException(string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

        template.Nome       = cmd.Nome.Trim();
        template.LayoutJson = cmd.LayoutJson;
        template.AlteradoEm = DateTime.UtcNow;

        try
        {
            await repo.UpdateEmpresaAsync(template);

            if (cmd.OperadorId.HasValue)
            {
                await auditRepo.AddAsync(AuditLogEntity.Criar(
                    cmd.OperadorId.Value, "etiqueta-template.editado", true,
                    $"Id: {template.Id}, Nome: {template.Nome}", cmd.Ip, cmd.UserAgent));
            }

            await uow.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new UseCaseConcurrencyException("Outro usuário modificou este modelo. Recarregue e tente novamente.");
        }

        return CriarTemplateUseCase.Map(template);
    }

    private static LayoutJsonDocument DeserializeLayout(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<LayoutJsonDocument>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new UseCaseValidationException("LayoutJson inválido.");
        }
        catch (JsonException ex)
        {
            throw new UseCaseValidationException($"LayoutJson malformado: {ex.Message}");
        }
    }
}
