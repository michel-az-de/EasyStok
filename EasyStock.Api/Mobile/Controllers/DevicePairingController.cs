using System.Security.Claims;
using System.Security.Cryptography;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Pareamento de dispositivos (Onda 1).
///
/// Fluxo:
///   1. Usuário web autenticado chama <c>POST /api/mobile/devices/pair-codes</c>
///      → recebe um código de 6 dígitos válido por 10 min, mostra na tela.
///   2. Operador no app digita o código, app chama
///      <c>POST /api/mobile/devices/pair</c> com <c>{ pairingCode, deviceId, label? }</c>
///      → servidor valida, persiste, retorna <c>{ apiKey, empresaId, lojaId, ... }</c>.
///   3. App passa a enviar <c>X-Mobile-Api-Key</c> em todo request.
///
/// Listagem e revoke são endpoints autenticados pelo painel web.
/// </summary>
[ApiController]
[Route("api/mobile/devices")]
public class DevicePairingController(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    ILogger<DevicePairingController> log) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly ICurrentUserAccessor _currentUser = currentUser;
    private readonly ILogger<DevicePairingController> _log = log;

    /// <summary>
    /// Auditoria 2026-04-30 — valida que o EmpresaId requerido pelo endpoint
    /// bate com o do usuário logado (SuperAdmin pode operar em qualquer).
    /// Antes existia TODO confessado, brecha crítica de cross-tenant.
    /// </summary>
    private bool RequestedEmpresaMatchesCurrentUser(Guid requestedEmpresaId)
    {
        if (_currentUser.Nivel == NivelAcesso.SuperAdmin) return true;
        return _currentUser.EmpresaId != Guid.Empty
            && _currentUser.EmpresaId == requestedEmpresaId;
    }

    /// <summary>Para endpoints `/{id}` (commands/revoke) — valida que o device pertence ao tenant.</summary>
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

    /// <summary>
    /// Web autenticado — gera código de pareamento de 6 dígitos válido por 10min.
    /// Operador mostra esse código na tela do painel pra digitar no celular.
    /// </summary>
    [HttpPost("pair-codes")]
    [Authorize]
    public async Task<ActionResult<PairCodeResponse>> CreatePairCode(
        [FromBody] CreatePairCodeRequest req,
        CancellationToken ct)
    {
        if (req == null) return BadRequest(new { error = "payload obrigatório" });
        if (req.EmpresaId == Guid.Empty) return BadRequest(new { error = "empresaId obrigatório" });
        if (req.LojaId == Guid.Empty) return BadRequest(new { error = "lojaId obrigatório" });

        // Auditoria 2026-04-30 (CRITICAL fix): TODO antigo era brecha real.
        // Agora valida que o usuário logado pode parear devices nessa empresa.
        if (!RequestedEmpresaMatchesCurrentUser(req.EmpresaId)) return Forbid();
        var userId = ResolveUserId();

        var code = GeneratePairingCode();
        var device = new MobileDevice
        {
            Id = req.DeviceId ?? "pending-" + Guid.NewGuid().ToString("N"),
            ApiKey = "pending-" + Guid.NewGuid().ToString("N"), // placeholder até pareamento efetivo
            EmpresaId = req.EmpresaId,
            LojaId = req.LojaId,
            PairedByUserId = userId,
            Label = req.Label,
            DefaultOperatorName = req.DefaultOperatorName,
            PairingCode = code,
            PairingExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Set<MobileDevice>().Add(device);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Pair code criado por user={UserId} empresa={EmpresaId} loja={LojaId} code={Code}",
            userId, req.EmpresaId, req.LojaId, code);

        return Ok(new PairCodeResponse(
            PairingCode: code,
            ExpiresAt: device.PairingExpiresAt!.Value,
            DeviceRecordId: device.Id
        ));
    }

    /// <summary>
    /// PWA anônimo — troca pairing code por api key.
    /// Single-use: ao consumir, limpa <c>pairing_code</c> e seta <c>paired_at</c>.
    /// Rate-limited pra mitigar tentativas de adivinhar código (6 dígitos =
    /// 1M combinações; com cap em 30 req/min/IP, brute-force levaria ~23 dias
    /// e cada código vence em 10 min — inviável).
    /// </summary>
    [HttpPost("pair")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("mobile-anonymous")]
    public async Task<ActionResult<PairResponse>> Pair(
        [FromBody] PairRequest req,
        CancellationToken ct)
    {
        if (req == null) return BadRequest(new { error = "payload obrigatório" });
        if (string.IsNullOrWhiteSpace(req.PairingCode)) return BadRequest(new { error = "pairingCode obrigatório" });
        if (string.IsNullOrWhiteSpace(req.DeviceId)) return BadRequest(new { error = "deviceId obrigatório" });

        var code = req.PairingCode.Trim();
        var deviceFromApp = req.DeviceId.Trim();

        var device = await _db.Set<MobileDevice>()
            .FirstOrDefaultAsync(d => d.PairingCode == code && !d.Revoked, ct);

        if (device == null)
        {
            _log.LogWarning("Pair tentado com código inválido. code={Code} ip={IP}",
                code, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "código inválido ou já usado" });
        }

        if (device.PairingExpiresAt == null || device.PairingExpiresAt < DateTime.UtcNow)
        {
            _log.LogWarning("Pair tentado com código expirado. code={Code}", code);
            return Unauthorized(new { error = "código expirado, gere um novo no painel" });
        }

        // Primeiro pareamento: troca o Id placeholder pelo deviceId vindo do app.
        // Caso o registro tenha sido criado com deviceId conhecido (pre-cadastrado),
        // só limpa código + gera key.
        var apiKey = GenerateApiKey();
        var now = DateTime.UtcNow;

        if (device.Id.StartsWith("pending-"))
        {
            // Como Id é PK, não podemos UPDATE — vamos inserir novo + remover antigo
            // num único batch transacional. Em compensação, mantém EmpresaId etc.
            var newDevice = new MobileDevice
            {
                Id = deviceFromApp,
                ApiKey = apiKey,
                EmpresaId = device.EmpresaId,
                LojaId = device.LojaId,
                PairedByUserId = device.PairedByUserId,
                Label = !string.IsNullOrWhiteSpace(req.Label) ? req.Label : device.Label,
                DefaultOperatorName = device.DefaultOperatorName,
                PairingCode = null,
                PairingExpiresAt = null,
                PairedAt = now,
                LastSeenAt = now,
                LastSeenIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                CreatedAt = device.CreatedAt,
                UpdatedAt = now
            };
            _db.Set<MobileDevice>().Remove(device);
            _db.Set<MobileDevice>().Add(newDevice);
            await _db.SaveChangesAsync(ct);
            device = newDevice;
        }
        else
        {
            device.ApiKey = apiKey;
            device.PairingCode = null;
            device.PairingExpiresAt = null;
            device.PairedAt = now;
            device.LastSeenAt = now;
            device.LastSeenIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            device.UpdatedAt = now;
            if (!string.IsNullOrWhiteSpace(req.Label)) device.Label = req.Label;
            await _db.SaveChangesAsync(ct);
        }

        _log.LogInformation("Device pareado: id={DeviceId} empresa={EmpresaId} loja={LojaId}",
            device.Id, device.EmpresaId, device.LojaId);

        return Ok(new PairResponse(
            DeviceId: device.Id,
            ApiKey: apiKey,
            EmpresaId: device.EmpresaId,
            LojaId: device.LojaId,
            Label: device.Label,
            DefaultOperatorName: device.DefaultOperatorName,
            PairedAt: device.PairedAt!.Value
        ));
    }

    /// <summary>Web autenticado — lista devices pareados de uma empresa.</summary>
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<DeviceSummary[]>> List(
        [FromQuery] Guid empresaId,
        CancellationToken ct)
    {
        if (empresaId == Guid.Empty) return BadRequest(new { error = "empresaId obrigatório" });
        // Auditoria 2026-04-30 (CRITICAL fix): só listo devices da empresa do usuário.
        if (!RequestedEmpresaMatchesCurrentUser(empresaId)) return Forbid();

        var devices = await _db.Set<MobileDevice>()
            .AsNoTracking()
            .Where(d => d.EmpresaId == empresaId)
            .OrderBy(d => d.Revoked).ThenByDescending(d => d.LastSeenAt ?? d.CreatedAt)
            .ToListAsync(ct);

        var summaries = devices.Select(d => new DeviceSummary(
            Id: d.Id,
            Label: d.Label,
            EmpresaId: d.EmpresaId,
            LojaId: d.LojaId,
            DefaultOperatorName: d.DefaultOperatorName,
            PairedAt: d.PairedAt,
            LastSeenAt: d.LastSeenAt,
            LastSeenIp: d.LastSeenIp,
            Revoked: d.Revoked,
            RevokedAt: d.RevokedAt,
            // Pendente = ainda tem código de pareamento ativo; útil pra UI
            // mostrar "aguardando app conectar".
            PendingPair: d.PairingCode != null
        )).ToArray();

        return Ok(summaries);
    }

    /// <summary>
    /// Onda 4 — Web autenticado: enfileira um comando remoto pro device.
    /// Tipos suportados: <c>flush_now</c>, <c>pull_now</c>, <c>reload</c>, <c>message</c>.
    /// Device executa na próxima chamada de /sync ou /sync/pull.
    /// </summary>
    [HttpPost("{id}/commands")]
    [Authorize]
    public async Task<ActionResult<object>> EnqueueCommand(
        string id,
        [FromBody] EnqueueCommandRequest req,
        CancellationToken ct)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.CommandType))
            return BadRequest(new { error = "commandType obrigatório" });

        // Auditoria 2026-04-30 (CRITICAL fix): só comanda devices da própria empresa.
        if (!await DeviceBelongsToCurrentTenantAsync(id, ct)) return Forbid();

        var device = await _db.Set<MobileDevice>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device == null) return NotFound();
        if (device.Revoked) return BadRequest(new { error = "device revogado" });

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "flush_now", "pull_now", "reload", "message" };
        if (!allowed.Contains(req.CommandType))
            return BadRequest(new { error = "commandType inválido. Use: " + string.Join(", ", allowed) });

        var cmd = new DeviceCommand
        {
            Id = Guid.NewGuid(),
            DeviceId = id,
            EmpresaId = device.EmpresaId,
            CommandType = req.CommandType.ToLowerInvariant(),
            PayloadJson = req.PayloadJson,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = ResolveUserId(),
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
        _db.Set<DeviceCommand>().Add(cmd);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Comando enfileirado: {Cmd} pra device {DeviceId} by {User}",
            cmd.CommandType, id, cmd.CreatedByUserId);
        return Ok(new { id = cmd.Id, commandType = cmd.CommandType, expiresAt = cmd.ExpiresAt });
    }

    /// <summary>
    /// Onda 6 — App pareado pega lista de lojas da empresa pra trocar.
    /// Apenas lojas <c>Ativa</c>. Empresa do device é a única visível.
    /// </summary>
    [HttpGet("me/lojas-disponiveis")]
    [MobileApiKey]
    public async Task<ActionResult<LojaDisponivelDto[]>> ListarLojasDisponiveis(CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device == null) return Unauthorized(new { error = "device não pareado" });

        var lojas = await _db.Set<Loja>().AsNoTracking()
            .Where(l => l.EmpresaId == device.EmpresaId && l.Ativa)
            .OrderBy(l => l.Nome)
            .Select(l => new LojaDisponivelDto(
                l.Id, l.Nome, l.Descricao, l.Endereco, l.Id == device.LojaId
            ))
            .ToListAsync(ct);

        return Ok(lojas);
    }

    /// <summary>
    /// Onda 6 — Operador troca a loja do próprio device. Empresa fixa.
    /// Audita a mudança em <c>created_by_user_id</c>=null + log.
    /// </summary>
    [HttpPost("me/switch-loja")]
    [MobileApiKey]
    public async Task<IActionResult> SwitchLoja(
        [FromBody] SwitchLojaRequest req,
        CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device == null) return Unauthorized(new { error = "device não pareado" });
        if (req == null || req.LojaId == Guid.Empty)
            return BadRequest(new { error = "lojaId obrigatório" });
        if (req.LojaId == device.LojaId)
            return Ok(new { changed = false });

        // Valida que a loja pertence à empresa do device + está ativa.
        var loja = await _db.Set<Loja>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == req.LojaId && l.EmpresaId == device.EmpresaId && l.Ativa, ct);
        if (loja == null)
            return BadRequest(new { error = "loja não encontrada nesta empresa ou inativa" });

        // Atualiza via SQL pra evitar tracking issues (device veio AsNoTracking).
        var oldLojaId = device.LojaId;
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE mobile_devices
            SET loja_id = {loja.Id},
                updated_at = {DateTime.UtcNow}
            WHERE ""Id"" = {device.Id}", ct);

        _log.LogInformation("Device {DeviceId} trocou de loja {Old} → {New} ({LojaNome})",
            device.Id, oldLojaId, loja.Id, loja.Nome);

        return Ok(new { changed = true, lojaId = loja.Id, lojaNome = loja.Nome });
    }

    /// <summary>Web autenticado — revoga device. App correspondente para de funcionar.</summary>
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Revoke(string id, CancellationToken ct)
    {
        // Auditoria 2026-04-30 (CRITICAL fix): só revoga devices da própria empresa.
        if (!await DeviceBelongsToCurrentTenantAsync(id, ct)) return Forbid();

        var device = await _db.Set<MobileDevice>().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device == null) return NotFound();

        device.Revoked = true;
        device.RevokedAt = DateTime.UtcNow;
        device.RevokedByUserId = ResolveUserId();
        device.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Device revogado: id={DeviceId} by={UserId}", id, device.RevokedByUserId);
        return NoContent();
    }

    // ---------- Helpers ----------

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue("sub")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var g) ? g : null;
    }

    /// <summary>Código de 6 dígitos pseudo-aleatório (entropia ~20 bits, suficiente pra short-lived).</summary>
    private static string GeneratePairingCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        var n = BitConverter.ToUInt32(bytes, 0) % 1_000_000u;
        return n.ToString("D6");
    }

    /// <summary>API key opaca de 32 bytes em base64url (43 chars). Inclui prefixo legível pra debug.</summary>
    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var b64 = Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "mk_" + b64;
    }
}

// ---------- DTOs ----------

public record CreatePairCodeRequest(
    Guid EmpresaId,
    Guid LojaId,
    string? DeviceId,
    string? Label,
    string? DefaultOperatorName
);

public record PairCodeResponse(string PairingCode, DateTime ExpiresAt, string DeviceRecordId);

public record PairRequest(string PairingCode, string DeviceId, string? Label);

public record PairResponse(
    string DeviceId,
    string ApiKey,
    Guid EmpresaId,
    Guid LojaId,
    string? Label,
    string? DefaultOperatorName,
    DateTime PairedAt
);

public record DeviceSummary(
    string Id,
    string? Label,
    Guid EmpresaId,
    Guid LojaId,
    string? DefaultOperatorName,
    DateTime? PairedAt,
    DateTime? LastSeenAt,
    string? LastSeenIp,
    bool Revoked,
    DateTime? RevokedAt,
    bool PendingPair
);

/// <summary>Onda 4 — request pra enfileirar comando remoto.</summary>
public record EnqueueCommandRequest(string CommandType, string? PayloadJson);

/// <summary>Onda 6 — item da lista de lojas disponíveis pro device.</summary>
public record LojaDisponivelDto(
    Guid Id, string Nome, string? Descricao, string? Endereco, bool Atual
);

/// <summary>Onda 6 — request pra trocar de loja.</summary>
public record SwitchLojaRequest(Guid LojaId);
