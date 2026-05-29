using EasyStock.Application.Validators;
using System.Text.Json;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;

namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record CriarTemplateCommand(
    Guid EmpresaId,
    string Nome,
    string LayoutJson,
    Guid? BaseSistemaId,
    bool DefinirComoPadrao,
    Guid? OperadorId,
    string? Ip,
    string? UserAgent);

public class CriarTemplateUseCase(
    IEtiquetaTemplateRepository repo,
    IAuditLogRepository auditRepo,
    IUnitOfWork uow)
{
    private static readonly LayoutJsonValidator Validator = new();

    public async Task<EtiquetaTemplateResult> ExecuteAsync(CriarTemplateCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (string.IsNullOrWhiteSpace(cmd.Nome))
            throw new UseCaseValidationException("Nome é obrigatório.");

        var doc = DeserializeLayout(cmd.LayoutJson);
        var validationResult = await Validator.ValidateAsync(doc);
        if (!validationResult.IsValid)
            throw new UseCaseValidationException(string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

        var agora = DateTime.UtcNow;
        var template = new EtiquetaTemplate
        {
            Id           = Guid.NewGuid(),
            EmpresaId    = cmd.EmpresaId,
            Nome         = cmd.Nome.Trim(),
            BaseSistemaId = cmd.BaseSistemaId,
            LayoutJson   = cmd.LayoutJson,
            IsDefault    = false,
            CriadoEm    = agora,
            AlteradoEm  = agora,
        };

        await repo.AddEmpresaAsync(template);

        if (cmd.DefinirComoPadrao)
        {
            await repo.UpsertDefaultAsync(new EtiquetaEmpresaDefault
            {
                EmpresaId      = cmd.EmpresaId,
                TemplateOrigem = "Empresa",
                TemplateId     = template.Id,
                AlteradoEm     = agora,
            });
        }

        if (cmd.OperadorId.HasValue)
        {
            await auditRepo.AddAsync(AuditLogEntity.Criar(
                cmd.OperadorId.Value, "etiqueta-template.criado", true,
                $"Nome: {template.Nome}", cmd.Ip, cmd.UserAgent));
        }

        await uow.CommitAsync();
        return Map(template);
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

    internal static EtiquetaTemplateResult Map(EtiquetaTemplate t) =>
        new(t.Id, t.EmpresaId, t.Nome, t.BaseSistemaId, t.LayoutJson, t.IsDefault, t.CriadoEm, t.AlteradoEm);
}
