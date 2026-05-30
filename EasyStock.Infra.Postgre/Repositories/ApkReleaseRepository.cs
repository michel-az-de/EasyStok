using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre da persistência de releases de APK (F7).
/// </summary>
public sealed class ApkReleaseRepository(EasyStockDbContext db) : IApkReleaseRepository
{
    public async Task<ApkReleaseCriada> PublicarAsync(ApkReleaseNova nova, CancellationToken ct = default)
    {
        // Despromove releases anteriores do mesmo (AppId, IsCanaryOnly).
        await db.ApkReleases
            .Where(r => r.AppId == nova.AppId && r.IsCanaryOnly == nova.IsCanaryOnly && r.IsActive)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false), ct);

        var release = new ApkRelease
        {
            Id = Guid.NewGuid(),
            AppId = nova.AppId,
            Version = nova.Version,
            Sha256 = nova.Sha256,
            ReleaseNotes = nova.ReleaseNotes,
            FileContent = nova.FileContent,
            FileSizeBytes = nova.FileContent.LongLength,
            IsCanaryOnly = nova.IsCanaryOnly,
            IsActive = true,
            CriadoEm = DateTime.UtcNow
        };
        db.ApkReleases.Add(release);
        await db.SaveChangesAsync(ct);

        return new ApkReleaseCriada(
            release.Id, release.Version, release.Sha256, release.FileSizeBytes, release.IsCanaryOnly);
    }

    public async Task<IReadOnlyList<ApkReleaseListItem>> ListarAsync(string appId, int limite, CancellationToken ct = default)
        => await db.ApkReleases
            .AsNoTracking()
            .Where(r => r.AppId == appId)
            .OrderByDescending(r => r.CriadoEm)
            .Take(limite)
            .Select(r => new ApkReleaseListItem(
                r.Id, r.Version, r.Sha256, r.ReleaseNotes, r.FileSizeBytes, r.IsCanaryOnly, r.IsActive, r.CriadoEm))
            .ToListAsync(ct);
}
