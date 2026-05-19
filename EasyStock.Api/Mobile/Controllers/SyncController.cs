using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Endpoint unificado de sincronização do módulo Casa da Baba Mobile.
/// Recebe mutations do PWA e espelha no banco via last-write-wins com
/// <c>LastDeviceId</c> para auditoria.
///
/// Rotas:
/// <list type="bullet">
///   <item>POST /api/mobile/sync         — envia mutations pendentes</item>
///   <item>GET  /api/mobile/sync/pull    — busca mudanças feitas por outros devices</item>
/// </list>
///
/// Proteção: <c>[MobileApiKey]</c> exige header <c>X-Mobile-Api-Key</c>
/// válido (configurado em <c>Mobile:ApiKey</c>).
/// </summary>
[ApiController]
[Route("api/mobile/sync")]
[MobileApiKey]
[AllowAnonymous]
public class SyncController(
    EasyStockDbContext db,
    SyncMutationDispatcher dispatcher,
    SyncAutoLinker autoLinker,
    SyncReversePullService reversePull,
    MobileEventBroker eventBroker,
    IProdutoRepository produtoRepo,
    IConfiguration appConfig,
    ILogger<SyncController> log) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly SyncMutationDispatcher _dispatcher = dispatcher;
    private readonly SyncAutoLinker _autoLinker = autoLinker;
    private readonly SyncReversePullService _reversePull = reversePull;
    private readonly MobileEventBroker _eventBroker = eventBroker;
    private readonly IProdutoRepository _produtoRepo = produtoRepo;
    private readonly IConfiguration _appConfig = appConfig;
    private readonly ILogger<SyncController> _log = log;

    [HttpPost]
    public async Task<ActionResult<SyncPushResponse>> Push([FromBody] SyncPushRequest req)
    {
        if (req == null || req.Mutations == null) return BadRequest("Payload invalido.");

        var device = HttpContext.GetMobileDevice();
        var empresaId = device?.EmpresaId;
        var lojaId = device?.LojaId;

        var accepted = new List<string>();
        var rejected = new List<SyncConflict>();
        var autoLinkProductIds = new HashSet<string>(StringComparer.Ordinal);
        var autoLinkClientIds  = new HashSet<string>(StringComparer.Ordinal);
        var autoLinkOrderIds   = new HashSet<string>(StringComparer.Ordinal);
        var autoLinkBatchIds   = new HashSet<string>(StringComparer.Ordinal);
        var autoLinkCashIds    = new HashSet<string>(StringComparer.Ordinal);

        // F10-C-3 — Idempotency: pre-check mutations já processadas.
        var mutationIds = req.Mutations.Select(m => m.Id).Where(id => !string.IsNullOrEmpty(id)).ToList();
        var alreadyProcessed = new Dictionary<string, MobileProcessedMutation>(StringComparer.Ordinal);
        if (mutationIds.Count > 0 && !string.IsNullOrEmpty(req.DeviceId))
        {
            var existing = await _db.MobileProcessedMutations
                .AsNoTracking()
                .Where(p => mutationIds.Contains(p.MutationId) && p.DeviceId == req.DeviceId)
                .ToListAsync();
            foreach (var e in existing)
                alreadyProcessed[e.MutationId] = e;
        }

        foreach (var m in req.Mutations)
        {
            if (!string.IsNullOrEmpty(m.Id) && alreadyProcessed.TryGetValue(m.Id, out var prev))
            {
                if (prev.Outcome == "accepted")
                    accepted.Add(m.Id);
                else
                    rejected.Add(new SyncConflict(m.Id, prev.Outcome));
                continue;
            }

            try
            {
                await _dispatcher.ApplyMutationAsync(m, req.DeviceId, req.OperatorName, empresaId, lojaId);
                accepted.Add(m.Id);
                var typePrefix = m.Type?.Split('.')[0];
                if (typePrefix == "product" || typePrefix == "client" || typePrefix == "order"
                    || typePrefix == "batch" || typePrefix == "cashEntry")
                {
                    try
                    {
                        if (m.Payload.TryGetProperty("id", out var idEl) && idEl.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var payloadId = idEl.GetString();
                            if (!string.IsNullOrEmpty(payloadId))
                            {
                                if (typePrefix == "product")        autoLinkProductIds.Add(payloadId);
                                else if (typePrefix == "client")    autoLinkClientIds.Add(payloadId);
                                else if (typePrefix == "order")     autoLinkOrderIds.Add(payloadId);
                                else if (typePrefix == "batch")     autoLinkBatchIds.Add(payloadId);
                                else                                 autoLinkCashIds.Add(payloadId);
                            }
                        }
                    }
                    catch (Exception ex) { _log.LogWarning(ex, "Falha extraindo id de payload pra auto-link"); }
                }
            }
            catch (ConflictException cex)
            {
                rejected.Add(new SyncConflict(m.Id, "conflict: " + cex.Message, cex.WinningPayload));
                _log.LogInformation(
                    "SyncMutation REJECTED conflict deviceId={DeviceId} empresaId={EmpresaId} type={Type} mutationId={MutationId}: {Mensagem}",
                    req.DeviceId, empresaId, m.Type, m.Id, cex.Message);
            }
            catch (Exception ex)
            {
                rejected.Add(new SyncConflict(m.Id, ex.Message));
                // Antes: silencioso (so adicionava em rejected). Agora: log explicito
                // pra diagnose. Exception aqui = mutation invalida (tenant guard,
                // validacao de payload, FK quebrada). PWA mostra como rejected mas
                // sem este log o operador/admin nao tem como descobrir o motivo.
                _log.LogError(ex,
                    "SyncMutation REJECTED error deviceId={DeviceId} empresaId={EmpresaId} type={Type} mutationId={MutationId} exType={ExType}: {Mensagem}",
                    req.DeviceId, empresaId, m.Type, m.Id, ex.GetType().Name, ex.Message);
            }
        }

        // F10-C-3 — Registra mutations processadas (antes do SaveChanges pra mesma tx).
        if (!string.IsNullOrEmpty(req.DeviceId))
        {
            var now = DateTime.UtcNow;
            foreach (var aId in accepted)
            {
                if (string.IsNullOrEmpty(aId) || alreadyProcessed.ContainsKey(aId)) continue;
                _db.MobileProcessedMutations.Add(new MobileProcessedMutation
                {
                    MutationId = aId,
                    DeviceId = req.DeviceId,
                    EmpresaId = empresaId ?? Guid.Empty,
                    Outcome = "accepted",
                    CriadoEm = now,
                });
            }
            foreach (var r in rejected)
            {
                if (string.IsNullOrEmpty(r.MutationId) || alreadyProcessed.ContainsKey(r.MutationId)) continue;
                _db.MobileProcessedMutations.Add(new MobileProcessedMutation
                {
                    MutationId = r.MutationId,
                    DeviceId = req.DeviceId,
                    EmpresaId = empresaId ?? Guid.Empty,
                    Outcome = (r.Reason?.StartsWith("conflict:") == true ? "rejected:conflict" : "rejected:validation"),
                    ResponseMeta = r.Reason,
                    CriadoEm = now,
                });
            }
        }

        await _db.SaveChangesAsync();

        // F0/F1/F2/F3 — auto-link Product/Client/Order/Batch/CashEntry ↔ ERP.
        if (autoLinkProductIds.Count > 0 || autoLinkClientIds.Count > 0
            || autoLinkOrderIds.Count > 0 || autoLinkBatchIds.Count > 0
            || autoLinkCashIds.Count  > 0)
        {
            try
            {
                await _autoLinker.RunAsync(
                    autoLinkProductIds, autoLinkClientIds,
                    autoLinkOrderIds, autoLinkBatchIds, autoLinkCashIds,
                    empresaId);
            }
            catch (Exception ex)
            {
                // Outer catch: alguma exception ESCAPOU dos try/catch internos do SyncAutoLinker
                // (cada TryAutoLink* tem catch por entity). Indica falha catastrofica
                // como SaveChangesAsync abortando a transacao toda. LogError com
                // contadores pra diagnose: ver quantos ficaram orfaos por tipo.
                _log.LogError(ex,
                    "AutoLink batch FALHOU empresaId={EmpresaId} deviceId={DeviceId} prods={P} clients={C} orders={O} batches={B} cash={CE} exType={ExType}: {Mensagem}",
                    empresaId, req.DeviceId,
                    autoLinkProductIds.Count, autoLinkClientIds.Count,
                    autoLinkOrderIds.Count, autoLinkBatchIds.Count, autoLinkCashIds.Count,
                    ex.GetType().Name, ex.Message);
            }
        }

        // Onda 5: notifica outros devices da mesma loja em realtime.
        if (accepted.Count > 0)
        {
            await _eventBroker.NotifyMutationsAppliedAsync(empresaId, lojaId, req.DeviceId, accepted.Count);
        }

        return Ok(new SyncPushResponse(accepted, rejected.Count > 0 ? rejected : null));
    }

    /// <summary>
    /// F5 — Backfill auto-link mobile→ERP de entidades pre-existentes. Idempotente.
    /// </summary>
    [HttpPost("backfill-erp-link")]
    [MobileApiKey]
    public async Task<IActionResult> BackfillErpLink(CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "device não pareado" });

        var counts = await _autoLinker.BackfillAsync(device.EmpresaId, ct);
        return Ok(new
        {
            empresaId = device.EmpresaId,
            processed = new
            {
                products = counts.Products,
                clients = counts.Clients,
                orders = counts.Orders,
                batches = counts.Batches,
                cashEntries = counts.CashEntries
            }
        });
    }

    [HttpGet("pull")]
    public async Task<ActionResult<SyncPullResponse>> Pull([FromQuery] long since, [FromQuery] string deviceId)
    {
        var sinceDate = DateTimeOffset.FromUnixTimeMilliseconds(since).UtcDateTime;

        var device = HttpContext.GetMobileDevice();
        var lojaId = device?.LojaId;
        var empresaId = device?.EmpresaId;

        var mutations = new List<MutationDto>();

        var productsQ = _db.Set<Product>().Where(p => p.UpdatedAt > sinceDate && p.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            productsQ = productsQ.Where(p => p.LojaId == lojaId || p.LojaId == null);
        var products = await productsQ.ToListAsync();
        // C2 (RDC 727): batch lookup TipoEmbalagem dos Produtos ERP linkados.
        var erpProductIds = products.Where(p => p.ErpProductId.HasValue)
            .Select(p => p.ErpProductId!.Value).Distinct().ToList();
        var tipoEmbMap = (empresaId.HasValue && erpProductIds.Count > 0)
            ? await _produtoRepo.GetTipoEmbalagemMapAsync(empresaId.Value, erpProductIds)
            : new Dictionary<Guid, TipoEmbalagem>();
        foreach (var p in products)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), p.LastDeviceId ?? "server",
                "product.upsert", SyncDtoConverters.Serialize(SyncDtoConverters.ToDto(p, tipoEmbMap)),
                new DateTimeOffset(p.UpdatedAt).ToUnixTimeMilliseconds()));

        var clientsQ = _db.Set<Client>().Where(c => c.UpdatedAt > sinceDate && c.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            clientsQ = clientsQ.Where(c => c.LojaId == lojaId || c.LojaId == null);
        var clients = await clientsQ.ToListAsync();
        foreach (var c in clients)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), c.LastDeviceId ?? "server",
                "client.upsert", SyncDtoConverters.Serialize(SyncDtoConverters.ToDto(c)),
                new DateTimeOffset(c.UpdatedAt).ToUnixTimeMilliseconds()));

        var ordersQ = _db.Set<Order>().Include(o => o.Items)
            .Where(o => o.UpdatedAt > sinceDate && o.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            ordersQ = ordersQ.Where(o => o.LojaId == lojaId || o.LojaId == null);
        var orders = await ordersQ.ToListAsync();
        foreach (var o in orders)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), o.LastDeviceId ?? "server",
                "order.upsert", SyncDtoConverters.Serialize(SyncDtoConverters.ToDto(o)),
                new DateTimeOffset(o.UpdatedAt).ToUnixTimeMilliseconds()));

        var batchesQ = _db.Set<Batch>().Include(b => b.Items)
            .Where(b => b.CreatedAt > sinceDate && b.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            batchesQ = batchesQ.Where(b => b.LojaId == lojaId || b.LojaId == null);
        var batches = await batchesQ.ToListAsync();
        foreach (var b in batches)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), b.LastDeviceId ?? "server",
                "batch.upsert", SyncDtoConverters.Serialize(SyncDtoConverters.ToDto(b)),
                new DateTimeOffset(b.CreatedAt).ToUnixTimeMilliseconds()));

        var cashQ = _db.Set<CashEntry>().Where(c => c.CreatedAt > sinceDate && c.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            cashQ = cashQ.Where(c => c.LojaId == lojaId || c.LojaId == null);
        var cash = await cashQ.ToListAsync();
        foreach (var c in cash)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), c.LastDeviceId ?? "server",
                "cashEntry.upsert", SyncDtoConverters.Serialize(SyncDtoConverters.ToDto(c)),
                new DateTimeOffset(c.CreatedAt).ToUnixTimeMilliseconds()));

        // F6 — sync reverso web→mobile.
        if (empresaId.HasValue && _appConfig.GetValue<bool>("MobileSync:PullReverse:Enabled", true))
        {
            try
            {
                await _reversePull.AppendAsync(mutations, sinceDate, empresaId.Value, lojaId);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha pull reverso web→mobile");
            }
        }

        return Ok(new SyncPullResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), mutations));
    }
}

