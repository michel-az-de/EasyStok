using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Domain.Entities.Mobile;
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
// [MobileApiKey] — DESABILITADO temporariamente para facilitar testes iniciais
// do PWA/APK. Reativar quando a ferramenta estiver estável e a API for
// exposta fora da rede local (ver Onda 1 do roadmap de integração).
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "mobile-v1")]
public class SyncController(EasyStockDbContext db) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;

    [HttpPost]
    public async Task<ActionResult<SyncPushResponse>> Push([FromBody] SyncPushRequest req)
    {
        if (req == null || req.Mutations == null) return BadRequest("Payload invalido.");

        var accepted = new List<string>();
        var rejected = new List<SyncConflict>();

        foreach (var m in req.Mutations)
        {
            try
            {
                await ApplyMutation(m, req.DeviceId, req.OperatorName);
                accepted.Add(m.Id);
            }
            catch (Exception ex)
            {
                rejected.Add(new SyncConflict(m.Id, ex.Message));
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new SyncPushResponse(accepted, rejected.Count > 0 ? rejected : null));
    }

    [HttpGet("pull")]
    public async Task<ActionResult<SyncPullResponse>> Pull([FromQuery] long since, [FromQuery] string deviceId)
    {
        var sinceDate = DateTimeOffset.FromUnixTimeMilliseconds(since).UtcDateTime;

        var mutations = new List<MutationDto>();

        var products = await _db.Set<Product>()
            .Where(p => p.UpdatedAt > sinceDate && p.LastDeviceId != deviceId)
            .ToListAsync();
        foreach (var p in products)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), p.LastDeviceId ?? "server",
                "product.upsert", Serialize(ToDto(p)), new DateTimeOffset(p.UpdatedAt).ToUnixTimeMilliseconds()));

        var clients = await _db.Set<Client>()
            .Where(c => c.UpdatedAt > sinceDate && c.LastDeviceId != deviceId)
            .ToListAsync();
        foreach (var c in clients)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), c.LastDeviceId ?? "server",
                "client.upsert", Serialize(ToDto(c)), new DateTimeOffset(c.UpdatedAt).ToUnixTimeMilliseconds()));

        var orders = await _db.Set<Order>().Include(o => o.Items)
            .Where(o => o.UpdatedAt > sinceDate && o.LastDeviceId != deviceId)
            .ToListAsync();
        foreach (var o in orders)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), o.LastDeviceId ?? "server",
                "order.upsert", Serialize(ToDto(o)), new DateTimeOffset(o.UpdatedAt).ToUnixTimeMilliseconds()));

        var batches = await _db.Set<Batch>().Include(b => b.Items)
            .Where(b => b.CreatedAt > sinceDate && b.LastDeviceId != deviceId)
            .ToListAsync();
        foreach (var b in batches)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), b.LastDeviceId ?? "server",
                "batch.upsert", Serialize(ToDto(b)), new DateTimeOffset(b.CreatedAt).ToUnixTimeMilliseconds()));

        var cash = await _db.Set<CashEntry>()
            .Where(c => c.CreatedAt > sinceDate && c.LastDeviceId != deviceId)
            .ToListAsync();
        foreach (var c in cash)
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), c.LastDeviceId ?? "server",
                "cashEntry.upsert", Serialize(ToDto(c)), new DateTimeOffset(c.CreatedAt).ToUnixTimeMilliseconds()));

        return Ok(new SyncPullResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), mutations));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Apply mutation por tipo
    // ──────────────────────────────────────────────────────────────────────

    private async Task ApplyMutation(MutationDto m, string deviceId, string? operatorName)
    {
        var parts = m.Type.Split('.');
        if (parts.Length != 2) throw new ArgumentException($"Tipo invalido: {m.Type}");

        switch (parts[0])
        {
            case "product":   await ApplyProduct(m, deviceId, operatorName);   break;
            case "client":    await ApplyClient(m, deviceId, operatorName);    break;
            case "order":     await ApplyOrder(m, deviceId, operatorName);     break;
            case "batch":     await ApplyBatch(m, deviceId, operatorName);     break;
            case "cashEntry": await ApplyCashEntry(m, deviceId, operatorName); break;
            default: throw new ArgumentException($"Entidade desconhecida: {parts[0]}");
        }
    }

    private async Task ApplyProduct(MutationDto m, string deviceId, string? operatorName)
    {
        var dto = m.Payload.Deserialize<ProductDto>(JsonOpts)!;
        var existing = await _db.Set<Product>().FindAsync(dto.Id);
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
                LastOperatorName = operatorName
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
            // Etiquetas: só persiste se DTO mandou (preserva valor se APK antigo omitir).
            if (dto.Sku is not null)                 existing.Sku = dto.Sku;
            if (dto.DefaultWeightG.HasValue)         existing.DefaultWeightG = dto.DefaultWeightG;
            if (dto.DefaultValidityDays.HasValue)    existing.DefaultValidityDays = dto.DefaultValidityDays;
        }
    }

    private async Task ApplyClient(MutationDto m, string deviceId, string? operatorName)
    {
        var dto = m.Payload.Deserialize<ClientDto>(JsonOpts)!;
        var existing = await _db.Set<Client>().FindAsync(dto.Id);
        var lastOrderDate = DateTimeOffset.FromUnixTimeMilliseconds(dto.LastOrder).UtcDateTime;
        if (existing == null)
        {
            _db.Add(new Client
            {
                Id = dto.Id, Name = dto.Name, Apt = dto.Apt, Address = dto.Address,
                Phone = dto.Phone, LastOrder = lastOrderDate, OrderCount = dto.OrderCount,
                LastDeviceId = deviceId,
                LastOperatorName = operatorName
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
        }
    }

    private async Task ApplyOrder(MutationDto m, string deviceId, string? operatorName)
    {
        var dto = m.Payload.Deserialize<OrderDto>(JsonOpts)!;
        var existing = await _db.Set<Order>().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == dto.Id);
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
                FactAt = factAt
            };
            foreach (var i in dto.Items)
                order.Items.Add(new OrderItem
                {
                    OrderId = dto.Id, ProductId = i.ProductId, Name = i.Name,
                    Emoji = i.Emoji, Unit = i.Unit, Qty = i.Qty, UnitPrice = i.UnitPrice
                });
            _db.Add(order);
        }
        else
        {
            // Status transicionou? Aplica regra de estoque.
            if (existing.Status != dto.Status)
            {
                await ApplyStockRule(existing.Status, dto.Status, dto.Items);
            }
            existing.Status = dto.Status;
            existing.Notes = dto.Notes;
            existing.Total = dto.Total;
            existing.UpdatedAt = updatedAt;
            existing.LastDeviceId = deviceId;
            existing.LastOperatorName = operatorName;
            // Atualiza só se o cliente enviou — preserva valores legados se omitir.
            if (historyJson is not null) existing.HistoryJson = historyJson;
            if (dto.ConfirmedBy is not null) existing.ConfirmedBy = dto.ConfirmedBy;
            if (confirmedAt.HasValue) existing.ConfirmedAt = confirmedAt;
            if (factAt.HasValue) existing.FactAt = factAt;
            // Substitui os itens (simplificação — em produção, faça diff item-a-item).
            _db.RemoveRange(existing.Items);
            foreach (var i in dto.Items)
                existing.Items.Add(new OrderItem
                {
                    OrderId = dto.Id, ProductId = i.ProductId, Name = i.Name,
                    Emoji = i.Emoji, Unit = i.Unit, Qty = i.Qty, UnitPrice = i.UnitPrice
                });
        }
    }

    /// <summary>
    /// Regra de estoque central: o app já desconta localmente, mas o backend
    /// é fonte da verdade ao conciliar com outros devices.
    /// </summary>
    private async Task ApplyStockRule(string oldStatus, string newStatus, List<OrderItemDto> items)
    {
        // Transição para "pronto"/"entregue": desconta
        if (oldStatus != "pronto" && oldStatus != "entregue"
            && (newStatus == "pronto" || newStatus == "entregue"))
        {
            foreach (var i in items)
            {
                var p = await _db.Set<Product>().FindAsync(i.ProductId);
                if (p != null) p.Stock -= i.Qty;
            }
        }
        // Cancelamento de pedido que já havia reservado: devolve
        if ((oldStatus == "pronto" || oldStatus == "entregue") && newStatus == "cancelado")
        {
            foreach (var i in items)
            {
                var p = await _db.Set<Product>().FindAsync(i.ProductId);
                if (p != null) p.Stock += i.Qty;
            }
        }
    }

    private async Task ApplyBatch(MutationDto m, string deviceId, string? operatorName)
    {
        var dto = m.Payload.Deserialize<BatchDto>(JsonOpts)!;
        var existing = await _db.Set<Batch>().Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == dto.Id);
        if (existing != null) return; // Batches são imutáveis — ignora re-envio

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        var batch = new Batch
        {
            Id = dto.Id, Code = dto.Code, BatchPhoto = dto.BatchPhoto,
            CreatedAt = createdAt,
            Lote = dto.Lote,
            LastDeviceId = deviceId,
            LastOperatorName = operatorName
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
            // Incrementa estoque
            var p = await _db.Set<Product>().FindAsync(i.ProductId);
            if (p != null) p.Stock += i.Qty;
        }
        _db.Add(batch);
    }

    private async Task ApplyCashEntry(MutationDto m, string deviceId, string? operatorName)
    {
        var dto = m.Payload.Deserialize<CashEntryDto>(JsonOpts)!;
        var existing = await _db.Set<CashEntry>().FindAsync(dto.Id);
        if (existing != null) return; // imutável

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        _db.Add(new CashEntry
        {
            Id = dto.Id, Type = dto.Type, Amount = dto.Amount,
            Description = dto.Description, CreatedAt = createdAt,
            LastDeviceId = deviceId,
            LastOperatorName = operatorName
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
