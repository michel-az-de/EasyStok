using System.Text.Json;
using AuditLogEntity = EasyStock.Domain.Entities.AuditLog;

namespace EasyStock.Application.UseCases.Etiquetas;

public sealed record MarcarEtiquetasImpressasCommand(
    Guid EmpresaId,
    Guid LoteId,
    IReadOnlyList<Guid> Ids,
    string LayoutJson,
    LayoutSnapshotMetaDto LayoutMeta,
    string Status,
    bool OverwriteSnapshot,
    Guid? OperadorId, string? Ip, string? UserAgent);

public sealed record MarcarEtiquetasImpressasResult(
    int Atualizadas,
    int IgnoradasSnapshotDivergente);

public class MarcarEtiquetasImpressasUseCase(
    ILoteRepository loteRepo,
    IAuditLogRepository auditRepo,
    IUnitOfWork uow)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public async Task<MarcarEtiquetasImpressasResult> ExecuteAsync(MarcarEtiquetasImpressasCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        if (cmd.Status != LoteEtiquetaStatus.EnviadaImpressao &&
            cmd.Status != LoteEtiquetaStatus.Impressa &&
            cmd.Status != LoteEtiquetaStatus.Pendente)
            throw new UseCaseValidationException($"Status '{cmd.Status}' inválido para esta operação.");

        var idSet = cmd.Ids.ToHashSet();
        var etiquetas = (await loteRepo.GetEtiquetasForRenderAsync(cmd.EmpresaId, cmd.LoteId))
            .Where(e => idSet.Contains(e.Id))
            .ToList();

        var snapshotMeta = JsonSerializer.Serialize(new
        {
            origem    = cmd.LayoutMeta.Origem,
            id        = cmd.LayoutMeta.Id,
            nome      = cmd.LayoutMeta.Nome,
            snapshotAt = DateTime.UtcNow
        }, JsonOpts);

        // Separa: etiquetas sem snapshot (gravar) vs com snapshot diferente (requer overwrite)
        var semSnapshot = etiquetas.Where(e => string.IsNullOrEmpty(e.LayoutSnapshotJson)).Select(e => e.Id).ToList();
        var comSnapshotDivergente = etiquetas
            .Where(e => !string.IsNullOrEmpty(e.LayoutSnapshotJson) && !SnapshotIdIgual(e.LayoutSnapshotMeta, cmd.LayoutMeta.Id))
            .ToList();

        int ignoradas = 0;

        // Etiquetas sem snapshot: sempre grava
        if (semSnapshot.Count > 0)
            await loteRepo.UpdateEtiquetasSnapshotAsync(semSnapshot, cmd.LayoutJson, snapshotMeta, cmd.Status);

        // Etiquetas com snapshot diferente: só grava se overwrite autorizado
        if (comSnapshotDivergente.Count > 0)
        {
            if (cmd.OverwriteSnapshot)
            {
                var ids = comSnapshotDivergente.Select(e => e.Id).ToList();
                await loteRepo.UpdateEtiquetasSnapshotAsync(ids, cmd.LayoutJson, snapshotMeta, cmd.Status);

                if (cmd.OperadorId.HasValue)
                {
                    await auditRepo.AddAsync(AuditLogEntity.Criar(
                        cmd.OperadorId.Value, "etiquetas.reimpressas-modelo-diferente", true,
                        $"LoteId: {cmd.LoteId}, Ids: {ids.Count}, NovoModelo: {cmd.LayoutMeta.Nome}",
                        cmd.Ip, cmd.UserAgent));
                }
            }
            else
            {
                ignoradas = comSnapshotDivergente.Count;
            }
        }

        // Etiquetas com mesmo snapshot: só atualiza status
        var comSnapshotIgual = etiquetas
            .Where(e => !string.IsNullOrEmpty(e.LayoutSnapshotJson) && SnapshotIdIgual(e.LayoutSnapshotMeta, cmd.LayoutMeta.Id))
            .Select(e => e.Id)
            .ToList();
        if (comSnapshotIgual.Count > 0)
            await loteRepo.UpdateEtiquetasStatusAsync(comSnapshotIgual, cmd.Status);

        if (cmd.OperadorId.HasValue && cmd.Status == LoteEtiquetaStatus.Impressa)
        {
            await auditRepo.AddAsync(AuditLogEntity.Criar(
                cmd.OperadorId.Value, "etiquetas.impressas", true,
                $"LoteId: {cmd.LoteId}, Count: {etiquetas.Count - ignoradas}",
                cmd.Ip, cmd.UserAgent));
        }

        await uow.CommitAsync();
        return new(etiquetas.Count - ignoradas, ignoradas);
    }

    private static bool SnapshotIdIgual(string? snapshotMetaJson, Guid templateId)
    {
        if (string.IsNullOrEmpty(snapshotMetaJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(snapshotMetaJson);
            if (doc.RootElement.TryGetProperty("id", out var idEl))
                return idEl.TryGetGuid(out var g) && g == templateId;
        }
        catch (JsonException)
        {
            // Snapshot malformado = tratado como "não-igual" (segue o fluxo de divergência).
            // Só JsonException é engolida (no try, apenas Parse lança); qualquer outra exceção
            // propaga em vez de ser silenciada.
        }
        return false;
    }
}
