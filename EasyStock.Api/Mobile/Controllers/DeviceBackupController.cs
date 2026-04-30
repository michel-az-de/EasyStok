using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
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
///
/// Auditoria 2026-04-30 (CRITICAL fix tenant): List/Download agora fazem
/// JOIN com mobile_devices.empresa_id e validam contra o usuário logado.
/// Antes, qualquer Authorize listava/baixava snapshot completo de qualquer
/// device de qualquer empresa.
/// </summary>
[ApiController]
[Route("api/mobile")]
public class DeviceBackupController(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    ILogger<DeviceBackupController> log) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly ICurrentUserAccessor _currentUser = currentUser;
    private readonly ILogger<DeviceBackupController> _log = log;

    /// <summary>
    /// Verifica que o device pertence à empresa do usuário logado.
    /// SuperAdmin passa direto. Outros: device.EmpresaId == currentUser.EmpresaId.
    /// </summary>
    private async Task<bool> DeviceBelongsToCurrentTenantAsync(string deviceId, CancellationToken ct)
    {
        if (_currentUser.Nivel == NivelAcesso.SuperAdmin) return true;
        if (_currentUser.EmpresaId == Guid.Empty) return false;

        var deviceEmpresa = await _db.Set<MobileDevice>().AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => (Guid?)d.EmpresaId)
            .FirstOrDefaultAsync(ct);
        return deviceEmpresa.HasValue && deviceEmpresa.Value == _currentUser.EmpresaId;
    }

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
        if (!await DeviceBelongsToCurrentTenantAsync(id, ct)) return Forbid();

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
        if (!await DeviceBelongsToCurrentTenantAsync(id, ct)) return Forbid();

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
