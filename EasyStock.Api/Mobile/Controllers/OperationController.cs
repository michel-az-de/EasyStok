using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Common;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.IdentityModel.Tokens;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Onda 4 — Painel "Operação" no web + entrega de comandos pro PWA.
///
/// Dois lados:
///   - GET /api/mobile/operation/dashboard [Authorize] — KPIs do dia da loja
///     (vendas, pedidos abertos, lotes ativos, saldo de caixa, divergências
///     de estoque, devices ativos). Usado por /operacao no painel web.
///   - GET /api/mobile/operation/pending-commands [MobileApiKey] — device
///     puxa comandos pendentes na próxima requisição. Servidor marca como
///     entregue e zera fila.
/// </summary>
[ApiController]
[Route("api/mobile/operation")]
public class OperationController(
    EasyStockDbContext db,
    MobileEventBroker eventBroker,
    ICurrentUserAccessor currentUser,
    IConfiguration configuration,
    ILogger<OperationController> log) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly MobileEventBroker _eventBroker = eventBroker;
    private readonly ICurrentUserAccessor _currentUser = currentUser;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<OperationController> _log = log;

    private const string SseTokenType = "mobile-sse";
    private const int SseTokenExpiresInSeconds = 300; // 5 min

    /// <summary>
    /// Dashboard ao vivo da loja — KPIs agregados em tempo de request.
    /// Cache curto (10s) seria bom em produção; por ora é always-fresh.
    /// </summary>
    [HttpGet("dashboard")]
    [Authorize]
    public async Task<ActionResult<OperationDashboard>> GetDashboard(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        CancellationToken ct)
    {
        if (empresaId == Guid.Empty) return BadRequest(new { error = "empresaId obrigatório" });

        // Isolamento de tenant: usuários normais só podem ver dados da própria empresa.
        if (_currentUser.Nivel != NivelAcesso.SuperAdmin &&
            _currentUser.EmpresaId != Guid.Empty &&
            _currentUser.EmpresaId != empresaId)
            return StatusCode(403, new { error = "Acesso negado à empresa solicitada." });

        var now = DateTime.UtcNow;
        var (todayStart, _) = HorarioBrasil.JanelaDiaUtc();

        // Pedidos abertos (não entregue/cancelado) — criterio compartilhado com a frota.
        var openOrdersQ = _db.Set<Order>().AsNoTracking()
            .Where(o => o.EmpresaId == empresaId)
            .Where(OperacaoCriterios.Aberto());
        if (lojaId.HasValue) openOrdersQ = openOrdersQ.Where(o => o.LojaId == lojaId);
        var openOrders = await openOrdersQ.ToListAsync(ct);

        // Pedidos entregues hoje (vendas do dia, somatório) — criterio compartilhado.
        var deliveredQ = _db.Set<Order>().AsNoTracking()
            .Where(o => o.EmpresaId == empresaId)
            .Where(OperacaoCriterios.EntregueHoje(todayStart));
        if (lojaId.HasValue) deliveredQ = deliveredQ.Where(o => o.LojaId == lojaId);
        var delivered = await deliveredQ.ToListAsync(ct);

        // Lotes do dia
        var batchesQ = _db.Set<Batch>().AsNoTracking()
            .Where(b => b.EmpresaId == empresaId && b.CreatedAt >= todayStart);
        if (lojaId.HasValue) batchesQ = batchesQ.Where(b => b.LojaId == lojaId);
        var batches = await batchesQ.CountAsync(ct);

        // Saldo de caixa do dia (vendas entregues + entradas extras − saídas)
        var cashEntriesQ = _db.Set<CashEntry>().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.CreatedAt >= todayStart);
        if (lojaId.HasValue) cashEntriesQ = cashEntriesQ.Where(c => c.LojaId == lojaId);
        var cashEntries = await cashEntriesQ.ToListAsync(ct);

        var cashIn = cashEntries.Where(c => c.Type == "income").Sum(c => c.Amount);
        var cashOut = cashEntries.Where(c => c.Type == "expense").Sum(c => c.Amount);
        var sold = delivered.Sum(o => o.Total);
        var saldo = sold + cashIn - cashOut;

        // Devices ativos (last seen <30min) — criterio compartilhado com a frota.
        var devicesQ = _db.Set<MobileDevice>().AsNoTracking()
            .Where(d => d.EmpresaId == empresaId)
            .Where(OperacaoCriterios.DeviceContavel());
        if (lojaId.HasValue) devicesQ = devicesQ.Where(d => d.LojaId == lojaId);
        var devices = await devicesQ.ToListAsync(ct);
        var activeDevices = devices.Count(OperacaoCriterios.DeviceAtivo(now).Compile());

        // Pedidos travados (preparando há >30min) e conferência pendente — mesmos criterios da frota.
        var stuck = openOrders.Count(OperacaoCriterios.Travado(now).Compile());
        var conferPending = openOrders.Count(OperacaoCriterios.ConferenciaPendente().Compile());

        // Divergências de estoque (linkados onde mobile.Stock != ERP.Quantidade)
        // Faz uma estimativa rápida — full check fica em /produtos-mobile/divergencias
        var linkedProducts = await _db.Set<Product>().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.ErpProductId != null)
            .Select(p => new { p.Id, p.Stock, ErpId = p.ErpProductId!.Value, p.LojaId })
            .ToListAsync(ct);

        var produtoIds = linkedProducts.Select(p => p.ErpId).Distinct().ToList();
        var itensEstoque = produtoIds.Count == 0
            ? new List<ItemEstoque>()
            : await _db.Set<ItemEstoque>().AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && produtoIds.Contains(i.ProdutoId))
                .ToListAsync(ct);

        var divergences = linkedProducts.Count(p =>
        {
            var item = itensEstoque.FirstOrDefault(i =>
                i.ProdutoId == p.ErpId &&
                (i.LojaId == null || i.LojaId == p.LojaId));
            var erpStock = item?.QuantidadeAtual?.Value ?? 0;
            return p.Stock != erpStock;
        });

        // Produtos custom pendentes de aprovação
        var pendingApproval = await _db.Set<Product>().AsNoTracking()
            .CountAsync(p => p.EmpresaId == empresaId &&
                             p.IsCustom && !p.IsApproved && p.ErpProductId == null, ct);

        return Ok(new OperationDashboard(
            EmpresaId: empresaId,
            LojaId: lojaId,
            Generated: DateTime.UtcNow,
            // Vendas
            VendasHojeValor: sold,
            VendasHojeCount: delivered.Count,
            CaixaSaldoHoje: saldo,
            CaixaEntradasExtras: cashIn,
            CaixaSaidas: cashOut,
            // Pedidos
            PedidosAbertos: openOrders.Count,
            PedidosPreparando: openOrders.Count(o => o.Status == OperacaoCriterios.StatusPreparando),
            PedidosProntos: openOrders.Count(o => o.Status == OperacaoCriterios.StatusPronto),
            PedidosTravados: stuck,
            ConferenciaPendente: conferPending,
            // Producao
            LotesHoje: batches,
            // Catalogo
            DivergenciasEstoque: divergences,
            ProdutosPendenteAprovacao: pendingApproval,
            // Devices
            DevicesAtivos: activeDevices,
            DevicesTotal: devices.Count
        ));
    }

    /// <summary>
    /// Device puxa comandos pendentes (Onda 4). Marca como entregue e
    /// retorna lista. Tipos: flush_now, pull_now, reload, message.
    ///
    /// Endpoint protegido por <see cref="MobileApiKeyAttribute"/> — só
    /// devices pareados podem puxar seus próprios comandos.
    /// </summary>
    [HttpGet("pending-commands")]
    [MobileApiKey]
    [AllowAnonymous] // permite legado sem header (modo transição)
    public async Task<ActionResult<DeviceCommandDto[]>> GetPendingCommands(CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device == null) return Ok(Array.Empty<DeviceCommandDto>()); // legado/anônimo

        var now = DateTime.UtcNow;
        var pending = await _db.Set<DeviceCommand>()
            .Where(c => c.DeviceId == device.Id &&
                        c.DeliveredAt == null &&
                        (c.ExpiresAt == null || c.ExpiresAt > now))
            .OrderBy(c => c.CreatedAt)
            .Take(20)
            .ToListAsync(ct);

        if (pending.Count == 0) return Ok(Array.Empty<DeviceCommandDto>());

        // Marca como entregue (ack-on-pull). Se app crashar antes de
        // executar, comando perdido — aceitável pra esses tipos
        // idempotentes (flush/pull/reload).
        foreach (var c in pending) c.DeliveredAt = now;
        await _db.SaveChangesAsync(ct);

        return Ok(pending.Select(c => new DeviceCommandDto(
            c.Id, c.CommandType, c.PayloadJson, c.CreatedAt
        )).ToArray());
    }

    /// <summary>
    /// Onda 7 — Status agregado de saúde de cada device pareado da empresa.
    /// Painel /dispositivos usa pra mostrar coluna "Saúde" com badge colorido
    /// e tooltip explicando.
    ///
    /// Sinais de saúde:
    ///   - last_seen_at: nunca conectou / >24h / >2h / &lt;2h
    ///   - command queue: comandos pendentes não-entregues há &gt;1h
    ///   - revogado: status crítico
    /// </summary>
    [HttpGet("devices-health")]
    [Authorize]
    public async Task<ActionResult<DeviceHealthDto[]>> GetDevicesHealth(
        [FromQuery] Guid empresaId,
        CancellationToken ct)
    {
        if (empresaId == Guid.Empty) return BadRequest(new { error = "empresaId obrigatório" });

        // Isolamento de tenant: usuários normais só podem ver devices da própria empresa.
        if (_currentUser.Nivel != NivelAcesso.SuperAdmin &&
            _currentUser.EmpresaId != Guid.Empty &&
            _currentUser.EmpresaId != empresaId)
            return StatusCode(403, new { error = "Acesso negado à empresa solicitada." });

        var devices = await _db.Set<MobileDevice>().AsNoTracking()
            .Where(d => d.EmpresaId == empresaId)
            .ToListAsync(ct);

        if (devices.Count == 0) return Ok(Array.Empty<DeviceHealthDto>());

        // Comandos pendentes (delivered_at = null) por device.
        var deviceIds = devices.Select(d => d.Id).ToList();
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var pendingCommandsByDevice = await _db.Set<DeviceCommand>().AsNoTracking()
            .Where(c => deviceIds.Contains(c.DeviceId) && c.DeliveredAt == null
                        && (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow))
            .GroupBy(c => c.DeviceId)
            .Select(g => new { DeviceId = g.Key, Total = g.Count(), OldStuck = g.Count(c => c.CreatedAt < oneHourAgo) })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var result = devices.Select(d =>
        {
            var cmds = pendingCommandsByDevice.FirstOrDefault(p => p.DeviceId == d.Id);
            var cmdsPending = cmds?.Total ?? 0;
            var cmdsStuck = cmds?.OldStuck ?? 0;

            // Score de saúde:
            //   "ok" — pareado, ativo nas ultimas 2h, sem comando preso
            //   "warn" — inativo entre 2h-24h OU 1+ comando >1h preso
            //   "err"  — >24h sem ver OU revogado OU pendente pareamento expirado
            string status;
            string label;
            if (d.Revoked)
            {
                status = "err"; label = "revogado";
            }
            else if (d.PairingCode != null && d.PairingExpiresAt.HasValue && d.PairingExpiresAt < now)
            {
                status = "err"; label = "código expirado sem pareamento";
            }
            else if (d.PairingCode != null)
            {
                status = "warn"; label = "aguardando app conectar";
            }
            else if (!d.LastSeenAt.HasValue)
            {
                status = "warn"; label = "pareado mas nunca sincronizou";
            }
            else
            {
                var minutos = (now - d.LastSeenAt.Value).TotalMinutes;
                if (minutos > 24 * 60) { status = "err"; label = "inativo há " + Math.Round(minutos / 60) + "h"; }
                else if (minutos > 2 * 60 || cmdsStuck > 0) { status = "warn"; label = "atenção"; }
                else { status = "ok"; label = "ativo"; }
            }

            return new DeviceHealthDto(
                Id: d.Id,
                Label: d.Label,
                Status: status,
                StatusLabel: label,
                LastSeenAt: d.LastSeenAt,
                LastSeenIp: d.LastSeenIp,
                PendingCommands: cmdsPending,
                StuckCommands: cmdsStuck,
                Revoked: d.Revoked,
                PendingPair: d.PairingCode != null
            );
        }).OrderBy(d =>
            d.Status == "err" ? 0 : d.Status == "warn" ? 1 : 2
        ).ThenByDescending(d => d.LastSeenAt ?? DateTime.MinValue)
        .ToArray();

        return Ok(result);
    }

    /// <summary>
    /// Emite token efemero (JWT 5min) para o device usar como query param ?token=
    /// no /operation/stream — assim a apiKey nao vai mais pra URL, log de proxy,
    /// historico de browser etc. Endpoint exige apiKey via header [MobileApiKey],
    /// que nao deixa rasto em logs HTTP.
    /// </summary>
    [HttpPost("sse-token")]
    [MobileApiKey]
    public IActionResult IssueSseToken()
    {
        var device = HttpContext.GetMobileDevice();
        if (device == null) return Unauthorized();

        var secretKey = _configuration["Jwt:SecretKey"];
        if (string.IsNullOrEmpty(secretKey))
        {
            _log.LogError("Jwt:SecretKey ausente — nao consigo emitir SSE token.");
            return StatusCode(500, new { error = "Configuracao JWT ausente." });
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var agora = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("typ", SseTokenType),
                new Claim("deviceId", device.Id),
            }),
            NotBefore = agora,
            IssuedAt = agora,
            Expires = agora.AddSeconds(SseTokenExpiresInSeconds),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = credentials,
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(handler.CreateToken(descriptor));
        return Ok(new { token, expiresIn = SseTokenExpiresInSeconds });
    }

    /// <summary>
    /// Onda 5 — Server-Sent Events stream pro PWA receber notificações de
    /// outros devices em tempo real. Auth: <c>?token=jwt</c> emitido por
    /// <see cref="IssueSseToken"/>. Aceita tambem <c>?apiKey=mk_xxx</c> como
    /// fallback legado (APKs antigos antes do token efêmero) — remover
    /// depois que toda frota atualizar.
    ///
    /// Eventos emitidos:
    ///   - mutations-applied: outro device da mesma loja sincronizou →
    ///     PWA dispara <c>pull()</c> imediato.
    ///   - command-queued: gestor enfileirou comando pra este device →
    ///     PWA dispara <c>fetchAndProcessCommands()</c>.
    ///
    /// Connection fica aberta até o client desconectar. Heartbeat de 25s
    /// pra manter alive em proxies/load balancers que dropam idle &gt;30s.
    /// Falha = client volta pra polling 30s normal.
    /// </summary>
    [HttpGet("stream")]
    [AllowAnonymous]
    public async Task Stream([FromQuery] string? token, [FromQuery] string? apiKey, CancellationToken ct)
    {
        MobileDevice? device = null;

        if (!string.IsNullOrWhiteSpace(token))
        {
            var deviceId = TryReadSseTokenDeviceId(token);
            if (deviceId == null)
            {
                Response.StatusCode = 401;
                return;
            }
            device = await _db.Set<MobileDevice>().AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        }
        else if (!string.IsNullOrWhiteSpace(apiKey))
        {
            // Caminho legado — APKs antigos ainda mandam apiKey na URL.
            // Hash antes do lookup pra bater com o que está persistido.
            var apiKeyHash = TokenHashHelper.ComputeSha256Hash(apiKey);
            device = await _db.Set<MobileDevice>().AsNoTracking()
                .FirstOrDefaultAsync(d => d.ApiKeyHash == apiKeyHash, ct);
        }
        else
        {
            Response.StatusCode = 401;
            return;
        }

        if (device == null || device.Revoked)
        {
            Response.StatusCode = 401;
            return;
        }

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no"; // dica pra nginx não bufferizar
        await Response.Body.FlushAsync(ct);

        var connKey = device.Id + ":" + Guid.NewGuid().ToString("N");
        using var subscription = _eventBroker.Subscribe(connKey, device.EmpresaId, device.LojaId, device.Id);

        // Heartbeat task — comment lines `:` mantêm conexão viva
        var heartbeatTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !subscription.Slot.Cancelled)
            {
                try
                {
                    await Task.Delay(25_000, ct);
                    await Response.WriteAsync(": heartbeat\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
                catch { return; }
            }
        }, ct);

        // Dispatch events conforme chegam na fila
        try
        {
            // evento "ready" — confirmação inicial pro client tratar como "conectado"
            await Response.WriteAsync($"event: ready\ndata: {{\"deviceId\":\"{device.Id}\"}}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            while (!ct.IsCancellationRequested && !subscription.Slot.Cancelled)
            {
                await subscription.Slot.Signal.WaitAsync(ct);
                while (subscription.Slot.Queue.TryDequeue(out var data))
                {
                    await Response.WriteAsync($"data: {data}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // cliente desconectou — normal
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "SSE stream interrompido (device={DeviceId})", device.Id);
        }
    }

    private Guid? ResolveUserId()
    {
        var sub = User.FindFirstValue("sub")
                  ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var g) ? g : null;
    }

    /// <summary>
    /// Valida JWT efemero de SSE e devolve deviceId. Retorna null se token
    /// invalido, expirado, ou de tipo errado.
    /// </summary>
    private string? TryReadSseTokenDeviceId(string token)
    {
        var secretKey = _configuration["Jwt:SecretKey"];
        if (string.IsNullOrEmpty(secretKey)) return null;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            var principal = handler.ValidateToken(token, parameters, out _);
            if (principal.FindFirstValue("typ") != SseTokenType) return null;

            var deviceId = principal.FindFirstValue("deviceId");
            return string.IsNullOrEmpty(deviceId) ? null : deviceId;
        }
        catch
        {
            return null;
        }
    }
}

// ---------- DTOs ----------

/// <summary>KPIs agregados do painel /operacao.</summary>
public record OperationDashboard(
    Guid EmpresaId,
    Guid? LojaId,
    DateTime Generated,
    decimal VendasHojeValor,
    int VendasHojeCount,
    decimal CaixaSaldoHoje,
    decimal CaixaEntradasExtras,
    decimal CaixaSaidas,
    int PedidosAbertos,
    int PedidosPreparando,
    int PedidosProntos,
    int PedidosTravados,
    int ConferenciaPendente,
    int LotesHoje,
    int DivergenciasEstoque,
    int ProdutosPendenteAprovacao,
    int DevicesAtivos,
    int DevicesTotal
);

/// <summary>Item retornado no pull de comandos (PWA processa).</summary>
public record DeviceCommandDto(Guid Id, string CommandType, string? PayloadJson, DateTime CreatedAt);

/// <summary>Onda 7 — saúde agregada de um device pra coluna no painel /dispositivos.</summary>
public record DeviceHealthDto(
    string Id,
    string? Label,
    string Status,        // "ok" | "warn" | "err"
    string StatusLabel,   // texto curto explicando
    DateTime? LastSeenAt,
    string? LastSeenIp,
    int PendingCommands,  // comandos não-entregues
    int StuckCommands,    // comandos pendentes há >1h
    bool Revoked,
    bool PendingPair
);
