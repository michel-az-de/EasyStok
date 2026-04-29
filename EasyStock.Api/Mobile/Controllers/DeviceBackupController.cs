using EasyStock.Api.Mobile.Security;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Onda 8 — Backup e restore do localStorage do PWA.
///
/// Mobile envia (POST /devices/me/backup) snapshot JSON quando o app
/// considerar oportuno (auto-diário ou manual via Diagnóstico).
/// Web (gestor) lista (GET /devices/{id}/backups) e baixa
/// (GET /devices/{id}/backups/{backupId}).
/// </summary>
[ApiController]
[Route("api/mobile")]
public class DeviceBackupController(
    EasyStockDbContext db,
    ILogger<DeviceBackupController> log) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly ILogger<DeviceBackupController> _log = log;

    /// <summary>Quantos backups manter por device. Rotaciona em FIFO.</summary>
    private const int RetainPerDevice = 7;

    /// <summary>Tamanho máximo aceitável (10MB de JSON cobre tudo + folga).</summary>
    private const int MaxSnapshotBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Mobile envia snapshot. Auto-rotaciona pra manter só os últimos N.
    /// </summary>
    [HttpPost("devices/me/backup")]
    [MobileApiKey]
    public async Task<ActionResult<object>> Upload(
        [FromBody] UploadBackupRequest req,
        CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device == null) return Unauthorized(new { error = "device não pareado" });
        if (req == null || string.IsNullOrWhiteSpace(req.SnapshotJson))
            return BadRequest(new { error = "snapshotJson obrigatório" });

        var size = System.Text.Encoding.UTF8.GetByteCount(req.SnapshotJson);
        if (size > MaxSnapshotBytes)
            return BadRequest(new { error = $"snapshot muito grande: {size} bytes (máx {MaxSnapshotBytes})" });

        var backup = new DeviceBackup
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            EmpresaId = device.EmpresaId,
            SnapshotJson = req.SnapshotJson,
            SizeBytes = size,
            CreatedAt = DateTime.UtcNow,
            BundleVersion = req.BundleVersion,
            OperatorName = req.OperatorName,
            Note = string.IsNullOrWhiteSpace(req.Note) ? "auto" : req.Note.Trim()
        };
        _db.Add(backup);
        await _db.SaveChangesAsync(ct);

        // Rotaciona — mantém os RetainPerDevice mais novos do device.
        var oldIds = await _db.Set<DeviceBackup>().AsNoTracking()
            .Where(b => b.DeviceId == device.Id)
            .OrderByDescending(b => b.CreatedAt)
            .Skip(RetainPerDevice)
            .Select(b => b.Id)
            .ToListAsync(ct);

        if (oldIds.Count > 0)
        {
            await _db.Set<DeviceBackup>()
                .Where(b => oldIds.Contains(b.Id))
                .ExecuteDeleteAsync(ct);
            _log.LogInformation("Backup rotation: device={DeviceId} removidos={Count}",
                device.Id, oldIds.Count);
        }

        _log.LogInformation("Backup upload: device={DeviceId} size={Size} note={Note}",
            device.Id, size, backup.Note);

        return Ok(new
        {
            id = backup.Id,
            createdAt = backup.CreatedAt,
            sizeBytes = size,
            kept = RetainPerDevice
        });
    }

    /// <summary>Web: lista backups de um device (sem o JSON pra economizar bandwidth).</summary>
    [HttpGet("devices/{id}/backups")]
    [Authorize]
    public async Task<ActionResult<DeviceBackupSummary[]>> List(string id, CancellationToken ct)
    {
        var backups = await _db.Set<DeviceBackup>().AsNoTracking()
            .Where(b => b.DeviceId == id)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new DeviceBackupSummary(
                b.Id, b.CreatedAt, b.SizeBytes, b.BundleVersion, b.OperatorName, b.Note
            ))
            .ToListAsync(ct);
        return Ok(backups);
    }

    /// <summary>Web: baixa o JSON inteiro pra restaurar manualmente.</summary>
    [HttpGet("devices/{id}/backups/{backupId}")]
    [Authorize]
    public async Task<IActionResult> Download(string id, Guid backupId, CancellationToken ct)
    {
        var backup = await _db.Set<DeviceBackup>().AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == backupId && b.DeviceId == id, ct);
        if (backup == null) return NotFound();

        var bytes = System.Text.Encoding.UTF8.GetBytes(backup.SnapshotJson);
        var filename = $"backup-{id}-{backup.CreatedAt:yyyyMMdd-HHmmss}.json";
        return File(bytes, "application/json", filename);
    }
}

public record UploadBackupRequest(
    string SnapshotJson,
    string? BundleVersion,
    string? OperatorName,
    string? Note
);

public record DeviceBackupSummary(
    Guid Id, DateTime CreatedAt, int SizeBytes,
    string? BundleVersion, string? OperatorName, string? Note
);
