using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
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
/// válido (configurado em <c>Mobile:ApiKey</c>). Atualmente desabilitado —
/// ver comentário no atributo <c>[AllowAnonymous]</c> abaixo.
/// </summary>
[ApiController]
[Route("api/mobile/sync")]
// Onda 1: middleware MobileApiKey resolve device pelo header X-Mobile-Api-Key.
// Quando Mobile:RequireApiKey=true, requests sem header viram 401.
// Quando false (modo de transição), aceita anônimo e segue como pré-Onda-1.
[MobileApiKey]
[AllowAnonymous]
public class SyncController(
    EasyStockDbContext db,
    MobileStockReconciler stockReconciler,
    MobileSaleSyncService saleSync,
    MobileEventBroker eventBroker) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly MobileStockReconciler _stockReconciler = stockReconciler;
    private readonly MobileSaleSyncService _saleSync = saleSync;
    private readonly MobileEventBroker _eventBroker = eventBroker;

    [HttpPost]
    public async Task<ActionResult<SyncPushResponse>> Push([FromBody] SyncPushRequest req)
    {
        if (req == null || req.Mutations == null) return BadRequest("Payload invalido.");

        // Onda 1 — escopo multi-tenant. Se device pareado: tudo herda
        // EmpresaId/LojaId dele. Se anônimo (legado): null nos campos novos
        // (registros pré-Onda-1).
        var device = HttpContext.GetMobileDevice();
        var empresaId = device?.EmpresaId;
        var lojaId = device?.LojaId;

        var accepted = new List<string>();
        var rejected = new List<SyncConflict>();

        foreach (var m in req.Mutations)
        {
            try
            {
                await ApplyMutation(m, req.DeviceId, req.OperatorName, empresaId, lojaId);
                accepted.Add(m.Id);
            }
            catch (ConflictException cex)
            {
                // Onda 5 — conflict explícito. Reason começa com "conflict:" pra
                // PWA detectar e mostrar UX especializada (toast + force-pull).
                rejected.Add(new SyncConflict(m.Id, "conflict: " + cex.Message));
            }
            catch (Exception ex)
            {
                rejected.Add(new SyncConflict(m.Id, ex.Message));
            }
        }

        await _db.SaveChangesAsync();

        // Onda 5: notifica outros devices da mesma loja em realtime.
        // Fail-safe: se hub indisponível, devices pegam no polling 30s normal.
        if (accepted.Count > 0)
        {
            await _eventBroker.NotifyMutationsAppliedAsync(empresaId, lojaId, req.DeviceId, accepted.Count);
        }

        return Ok(new SyncPushResponse(accepted, rejected.Count > 0 ? rejected : null));
    }

    [HttpGet("pull")]
    public async Task<ActionResult<SyncPullResponse>> Pull([FromQuery] long since, [FromQuery] string deviceId)
    {
        var sinceDate = DateTimeOffset.FromUnixTimeMilliseconds(since).UtcDateTime;

        // Onda 1 — escopo multi-tenant. Quando device pareado, restringe pull
        // aos registros da mesma loja. Sem device (legado), comportamento
        // anterior: retorna tudo (1 tenant implícito).
        var device = HttpContext.GetMobileDevice();
        var lojaId = device?.LojaId;
        var empresaId = device?.EmpresaId;

        var mutations = new List<MutationDto>();

        var productsQ = _db.Set<Product>().Where(p => p.UpdatedAt > sinceDate && p.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            productsQ = productsQ.Where(p => p.LojaId == lojaId || p.LojaId == null);
        var products = await productsQ.ToListAsync();
        foreach (var p in products)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), p.LastDeviceId ?? "server",
                "product.upsert", Serialize(ToDto(p)), new DateTimeOffset(p.UpdatedAt).ToUnixTimeMilliseconds()));

        var clientsQ = _db.Set<Client>().Where(c => c.UpdatedAt > sinceDate && c.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            clientsQ = clientsQ.Where(c => c.LojaId == lojaId || c.LojaId == null);
        var clients = await clientsQ.ToListAsync();
        foreach (var c in clients)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), c.LastDeviceId ?? "server",
                "client.upsert", Serialize(ToDto(c)), new DateTimeOffset(c.UpdatedAt).ToUnixTimeMilliseconds()));

        var ordersQ = _db.Set<Order>().Include(o => o.Items)
            .Where(o => o.UpdatedAt > sinceDate && o.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            ordersQ = ordersQ.Where(o => o.LojaId == lojaId || o.LojaId == null);
        var orders = await ordersQ.ToListAsync();
        foreach (var o in orders)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), o.LastDeviceId ?? "server",
                "order.upsert", Serialize(ToDto(o)), new DateTimeOffset(o.UpdatedAt).ToUnixTimeMilliseconds()));

        var batchesQ = _db.Set<Batch>().Include(b => b.Items)
            .Where(b => b.CreatedAt > sinceDate && b.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            batchesQ = batchesQ.Where(b => b.LojaId == lojaId || b.LojaId == null);
        var batches = await batchesQ.ToListAsync();
        foreach (var b in batches)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), b.LastDeviceId ?? "server",
                "batch.upsert", Serialize(ToDto(b)), new DateTimeOffset(b.CreatedAt).ToUnixTimeMilliseconds()));

        var cashQ = _db.Set<CashEntry>().Where(c => c.CreatedAt > sinceDate && c.LastDeviceId != deviceId);
        if (lojaId.HasValue)
            cashQ = cashQ.Where(c => c.LojaId == lojaId || c.LojaId == null);
        var cash = await cashQ.ToListAsync();
        foreach (var c in cash)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), c.LastDeviceId ?? "server",
                "cashEntry.upsert", Serialize(ToDto(c)), new DateTimeOffset(c.CreatedAt).ToUnixTimeMilliseconds()));

        return Ok(new SyncPullResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), mutations));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Apply mutation por tipo
    // ──────────────────────────────────────────────────────────────────────

    private async Task ApplyMutation(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var parts = m.Type.Split('.');
        if (parts.Length != 2) throw new ArgumentException($"Tipo invalido: {m.Type}");

        switch (parts[0])
        {
            case "product":   await ApplyProduct(m, deviceId, operatorName, empresaId, lojaId);   break;
            case "client":    await ApplyClient(m, deviceId, operatorName, empresaId, lojaId);    break;
            case "order":     await ApplyOrder(m, deviceId, operatorName, empresaId, lojaId);     break;
            case "batch":     await ApplyBatch(m, deviceId, operatorName, empresaId, lojaId);     break;
            case "cashEntry": await ApplyCashEntry(m, deviceId, operatorName, empresaId, lojaId); break;
            default: throw new ArgumentException($"Entidade desconhecida: {parts[0]}");
        }
    }

    private async Task ApplyProduct(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = m.Payload.Deserialize<ProductDto>(JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix): tenant guard.
        // Antes: FindAsync(dto.Id) sem filtro permitia device do tenant A
        // sobrescrever produto do tenant B com `dto.Id` arbitrário.
        var existing = await _db.Set<Product>()
            .FirstOrDefaultAsync(p => p.Id == dto.Id && p.EmpresaId == empresaId);

        // Onda 5: conflict detection. Se servidor tem versão mais nova
        // (UpdatedAt > timestamp da mutation), rejeita pra evitar
        // last-write-loser silencioso. Caller marca como rejected
        // com reason="conflict" e PWA mostra UX apropriada.
        if (existing != null && m.Ts > 0)
        {
            var serverTsMs = new DateTimeOffset(existing.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();
            // Tolerância de 2s pra clock skew entre cliente e server.
            if (serverTsMs > m.Ts + 2000 && existing.LastDeviceId != null && existing.LastDeviceId != deviceId)
            {
                throw new ConflictException(
                    $"Servidor já tem versão mais nova ({DateTimeOffset.FromUnixTimeMilliseconds(serverTsMs):HH:mm:ss}) " +
                    $"editada por {existing.LastOperatorName ?? "outro device"}");
            }
        }

        if (existing == null)
        {
            _db.Add(new Product
            {
                Id = dto.Id, Name = dto.Name, Emoji = dto.Emoji, Category = dto.Category,
                Unit = dto.Unit, Price = dto.Price, Stock = dto.Stock,
                IsCustom = dto.Custom ?? false,
                Sku = dto.Sku,
                DefaultWeightG = dto.DefaultWeightG,
                DefaultValidityDays = dto.DefaultValidityDays,
                LastDeviceId = deviceId,
                LastOperatorName = operatorName,
                EmpresaId = empresaId,
                LojaId = lojaId
            });
        }
        else
        {
            // Last-write-wins: sempre aplica. Conflict resolution seria aqui
            // se desejado (comparar existing.UpdatedAt com m.Ts).
            existing.Name = dto.Name;
            existing.Emoji = dto.Emoji;
            existing.Category = dto.Category;
            existing.Unit = dto.Unit;
            existing.Price = dto.Price;
            existing.Stock = dto.Stock;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.LastDeviceId = deviceId;
            existing.LastOperatorName = operatorName;
            // Multi-tenant: só seta se ainda não tinha (registros pré-Onda-1)
            // OU se o device é da mesma loja (idempotente). Não permite trocar
            // dono — uma vez vinculado, permanece.
            if (existing.EmpresaId == null && empresaId.HasValue) existing.EmpresaId = empresaId;
            if (existing.LojaId == null && lojaId.HasValue) existing.LojaId = lojaId;
            // Etiquetas: só persiste se DTO mandou (preserva valor se APK antigo omitir).
            if (dto.Sku is not null)                 existing.Sku = dto.Sku;
            if (dto.DefaultWeightG.HasValue)         existing.DefaultWeightG = dto.DefaultWeightG;
            if (dto.DefaultValidityDays.HasValue)    existing.DefaultValidityDays = dto.DefaultValidityDays;
        }
    }

    private async Task ApplyClient(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = m.Payload.Deserialize<ClientDto>(JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<Client>()
            .FirstOrDefaultAsync(c => c.Id == dto.Id && c.EmpresaId == empresaId);
        var lastOrderDate = DateTimeOffset.FromUnixTimeMilliseconds(dto.LastOrder).UtcDateTime;
        if (existing == null)
        {
            _db.Add(new Client
            {
                Id = dto.Id, Name = dto.Name, Apt = dto.Apt, Address = dto.Address,
                Phone = dto.Phone, LastOrder = lastOrderDate, OrderCount = dto.OrderCount,
                LastDeviceId = deviceId,
                LastOperatorName = operatorName,
                EmpresaId = empresaId,
                LojaId = lojaId
            });
        }
        else
        {
            existing.Name = dto.Name;
            existing.Apt = dto.Apt;
            existing.Address = dto.Address;
            existing.Phone = dto.Phone;
            existing.LastOrder = lastOrderDate;
            existing.OrderCount = dto.OrderCount;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.LastDeviceId = deviceId;
            existing.LastOperatorName = operatorName;
            if (existing.EmpresaId == null && empresaId.HasValue) existing.EmpresaId = empresaId;
            if (existing.LojaId == null && lojaId.HasValue) existing.LojaId = lojaId;
        }
    }

    private async Task ApplyOrder(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = m.Payload.Deserialize<OrderDto>(JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<Order>().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == dto.Id && o.EmpresaId == empresaId);
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.UpdatedAt).UtcDateTime;

        // Auditoria/conferência/retroativo: campos opcionais — só persiste se vier no DTO.
        var historyJson = dto.History.HasValue ? dto.History.Value.GetRawText() : null;
        var confirmedAt = dto.ConfirmedAt.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(dto.ConfirmedAt.Value).UtcDateTime
            : (DateTime?)null;
        var factAt = dto.FactAt.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(dto.FactAt.Value).UtcDateTime
            : (DateTime?)null;
        var scheduledDeliveryAt = dto.ScheduledDeliveryAt.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(dto.ScheduledDeliveryAt.Value).UtcDateTime
            : (DateTime?)null;

        if (existing == null)
        {
            var order = new Order
            {
                Id = dto.Id,
                ClientId = dto.ClientId,
                ClientSnapshotName = dto.ClientSnapshot.Name,
                ClientSnapshotRef = dto.ClientSnapshot.Ref,
                Notes = dto.Notes,
                Total = dto.Total,
                Status = dto.Status,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt,
                LastDeviceId = deviceId,
                LastOperatorName = operatorName,
                HistoryJson = historyJson,
                ConfirmedBy = dto.ConfirmedBy,
                ConfirmedAt = confirmedAt,
                FactAt = factAt,
                ScheduledDeliveryAt = scheduledDeliveryAt,
                EmpresaId = empresaId,
                LojaId = lojaId
            };
            foreach (var i in dto.Items)
                order.Items.Add(new OrderItem
                {
                    OrderId = dto.Id, ProductId = i.ProductId, Name = i.Name,
                    Emoji = i.Emoji, Unit = i.Unit, Qty = i.Qty, UnitPrice = i.UnitPrice
                });
            _db.Add(order);
            // Onda 3 — pedido criado direto como "entregue" (retroativo) ja
            // gera Venda no ERP. Caso contrario aguarda transicao de status.
            if (order.Status == "entregue")
            {
                await _saleSync.CreateVendaForDeliveredOrderAsync(order, dto.Items);
            }
        }
        else
        {
            // Status transicionou? Aplica regra de estoque.
            var oldStatus = existing.Status;
            if (oldStatus != dto.Status)
            {
                await ApplyStockRule(oldStatus, dto.Status, dto.Items, empresaId, dto.Id);
            }
            existing.Status = dto.Status;
            existing.Notes = dto.Notes;
            existing.Total = dto.Total;
            existing.UpdatedAt = updatedAt;
            existing.LastDeviceId = deviceId;
            existing.LastOperatorName = operatorName;
            if (existing.EmpresaId == null && empresaId.HasValue) existing.EmpresaId = empresaId;
            if (existing.LojaId == null && lojaId.HasValue) existing.LojaId = lojaId;
            // Atualiza só se o cliente enviou — preserva valores legados se omitir.
            if (historyJson is not null) existing.HistoryJson = historyJson;
            if (dto.ConfirmedBy is not null) existing.ConfirmedBy = dto.ConfirmedBy;
            if (confirmedAt.HasValue) existing.ConfirmedAt = confirmedAt;
            if (factAt.HasValue) existing.FactAt = factAt;
            if (dto.ScheduledDeliveryAt.HasValue) existing.ScheduledDeliveryAt = scheduledDeliveryAt;
            // Substitui os itens (simplificação — em produção, faça diff item-a-item).
            _db.RemoveRange(existing.Items);
            foreach (var i in dto.Items)
                existing.Items.Add(new OrderItem
                {
                    OrderId = dto.Id, ProductId = i.ProductId, Name = i.Name,
                    Emoji = i.Emoji, Unit = i.Unit, Qty = i.Qty, UnitPrice = i.UnitPrice
                });

            // Onda 3 — vendas mobile -> ERP.
            // Transicao -> entregue: cria Venda (idempotente via ErpVendaId).
            if (oldStatus != "entregue" && dto.Status == "entregue")
            {
                await _saleSync.CreateVendaForDeliveredOrderAsync(existing, dto.Items);
            }
            // Transicao entregue -> cancelado: marca Venda como cancelada.
            else if (oldStatus == "entregue" && dto.Status == "cancelado")
            {
                await _saleSync.CancelVendaForOrderAsync(existing);
            }
        }
    }

    /// <summary>
    /// Regra de estoque central: o app já desconta localmente, mas o backend
    /// é fonte da verdade ao conciliar com outros devices.
    ///
    /// Onda 2 parte 2: quando o produto está linkado ao ERP (ErpProductId),
    /// reconciler espelha a movimentação em <c>itens_estoque</c> +
    /// <c>movimentacoes_estoque</c>. Falha do reconciler NÃO interrompe sync.
    /// </summary>
    private async Task ApplyStockRule(string oldStatus, string newStatus, List<OrderItemDto> items, Guid? empresaId, string? orderId = null)
    {
        // Transição para "pronto"/"entregue": desconta
        if (oldStatus != "pronto" && oldStatus != "entregue"
            && (newStatus == "pronto" || newStatus == "entregue"))
        {
            foreach (var i in items)
            {
                var p = await _db.Set<Product>()
                    .FirstOrDefaultAsync(x => x.Id == i.ProductId && x.EmpresaId == empresaId);
                if (p == null) continue;
                var reconciliouNoErp = await _stockReconciler.ApplyDeltaAsync(
                    p, -i.Qty, NaturezaMovimentacaoEstoque.Venda,
                    descricao: $"Pedido mobile {orderId ?? p.Id} -> {newStatus}",
                    referenciaDocumento: orderId);
                if (!reconciliouNoErp) p.Stock -= i.Qty;
            }
        }
        // Cancelamento de pedido que já havia reservado: devolve
        if ((oldStatus == "pronto" || oldStatus == "entregue") && newStatus == "cancelado")
        {
            foreach (var i in items)
            {
                var p = await _db.Set<Product>()
                    .FirstOrDefaultAsync(x => x.Id == i.ProductId && x.EmpresaId == empresaId);
                if (p == null) continue;
                var reconciliouNoErp = await _stockReconciler.ApplyDeltaAsync(
                    p, +i.Qty, NaturezaMovimentacaoEstoque.Estorno,
                    descricao: $"Cancelamento de pedido mobile {orderId ?? p.Id}",
                    referenciaDocumento: orderId);
                if (!reconciliouNoErp) p.Stock += i.Qty;
            }
        }
    }

    private async Task ApplyBatch(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = m.Payload.Deserialize<BatchDto>(JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<Batch>().Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == dto.Id && b.EmpresaId == empresaId);
        if (existing != null) return; // Batches são imutáveis — ignora re-envio

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        var batch = new Batch
        {
            Id = dto.Id, Code = dto.Code, BatchPhoto = dto.BatchPhoto,
            CreatedAt = createdAt,
            Lote = dto.Lote,
            LastDeviceId = deviceId,
            LastOperatorName = operatorName,
            EmpresaId = empresaId,
            LojaId = lojaId
        };
        foreach (var i in dto.Items)
        {
            batch.Items.Add(new BatchItem
            {
                BatchId = dto.Id, ProductId = i.ProductId, Name = i.Name,
                Emoji = i.Emoji, Unit = i.Unit, Qty = i.Qty, Photo = i.Photo,
                WeightG = i.WeightG,
                ValidityDays = i.ValidityDays,
                ExpiresAt = i.ExpiresAt.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(i.ExpiresAt.Value).UtcDateTime
                    : (DateTime?)null
            });
            // Incrementa estoque (Onda 2 parte 2: reconciliação ERP se linkado).
            var p = await _db.Set<Product>()
                .FirstOrDefaultAsync(x => x.Id == i.ProductId && x.EmpresaId == empresaId);
            if (p == null) continue;
            var reconciliouNoErp = await _stockReconciler.ApplyDeltaAsync(
                p, +i.Qty, NaturezaMovimentacaoEstoque.Producao,
                descricao: $"Lote mobile {dto.Lote ?? dto.Code} unidade {i.Name}",
                referenciaDocumento: dto.Id);
            if (!reconciliouNoErp) p.Stock += i.Qty;
        }
        _db.Add(batch);
    }

    private async Task ApplyCashEntry(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = m.Payload.Deserialize<CashEntryDto>(JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<CashEntry>()
            .FirstOrDefaultAsync(c => c.Id == dto.Id && c.EmpresaId == empresaId);
        if (existing != null) return; // imutável

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        _db.Add(new CashEntry
        {
            Id = dto.Id, Type = dto.Type, Amount = dto.Amount,
            Description = dto.Description, CreatedAt = createdAt,
            LastDeviceId = deviceId,
            LastOperatorName = operatorName,
            EmpresaId = empresaId,
            LojaId = lojaId
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // DTO conversores para pull
    // ──────────────────────────────────────────────────────────────────────

    private static ProductDto ToDto(Product p) =>
        new(p.Id, p.Name, p.Emoji, p.Category, p.Unit, p.Price, p.Stock, p.IsCustom,
            p.Sku, p.DefaultWeightG, p.DefaultValidityDays);

    private static ClientDto ToDto(Client c) =>
        new(c.Id, c.Name, c.Apt, c.Address, c.Phone,
            new DateTimeOffset(c.LastOrder).ToUnixTimeMilliseconds(), c.OrderCount);

    private static OrderDto ToDto(Order o) =>
        new(o.Id, o.ClientId,
            new ClientSnapshotDto(o.ClientSnapshotName, o.ClientSnapshotRef),
            o.Items.Select(i => new OrderItemDto(i.ProductId, i.Name, i.Emoji, i.Unit, i.Qty, i.UnitPrice)).ToList(),
            o.Notes, o.Total, o.Status,
            new DateTimeOffset(o.CreatedAt).ToUnixTimeMilliseconds(),
            new DateTimeOffset(o.UpdatedAt).ToUnixTimeMilliseconds());

    private static BatchDto ToDto(Batch b) =>
        new(b.Id, b.Code,
            b.Items.Select(i => new BatchItemDto(
                i.ProductId, i.Name, i.Emoji, i.Unit, i.Qty, i.Photo,
                i.WeightG, i.ValidityDays,
                i.ExpiresAt.HasValue
                    ? new DateTimeOffset(i.ExpiresAt.Value).ToUnixTimeMilliseconds()
                    : (long?)null)).ToList(),
            b.BatchPhoto,
            new DateTimeOffset(b.CreatedAt).ToUnixTimeMilliseconds(),
            b.Lote);

    private static CashEntryDto ToDto(CashEntry c) =>
        new(c.Id, c.Type, c.Amount, c.Description,
            new DateTimeOffset(c.CreatedAt).ToUnixTimeMilliseconds());

    private static JsonElement Serialize<T>(T obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

/// <summary>
/// Lançada quando uma mutation chega com timestamp anterior à versão
/// do servidor — sinaliza last-write-loser que o cliente precisa tratar.
/// </summary>
public class ConflictException(string message) : Exception(message);
