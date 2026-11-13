namespace EasyStock.Api.Mobile.DTOs;

public record LegacySnapshotRequest(Guid ImportId, string SnapshotJson, string SnapshotSha256,
    List<string> OriginMutationIds, bool SourceNeverSynced, Guid ExpectedEmpresaId, Guid ExpectedLojaId,
    string? OperatorName = null);

public record LegacySnapshotResponse(Guid ImportId, string SnapshotSha256, Guid EmpresaId, Guid LojaId,
    List<string> CoveredMutationIds, Dictionary<string, int> Counts, bool Replayed,
    List<string> ArchiveOnlyCollections, List<string> Warnings, Guid BackupId);
