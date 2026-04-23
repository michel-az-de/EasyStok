using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EasyStock.Mobile.DTOs;
using EasyStock.Mobile.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Mobile.Controllers;

/// <summary>
/// Endpoint unificado de sincronizacao. Recebe mutations do PWA e espelha no banco.
/// Estrategia: last-write-wins por timestamp, com DeviceId registrado pra auditoria.
///
/// Rotas:
///   POST /api/mobile/sync         - envia mutations pendentes
///   GET  /api/mobile/sync/pull    - busca mudancas feitas por outros devices
/// </summary>
[ApiController]
[Route("api/mobile/sync")]
public class SyncController : ControllerBase
{
    // IMPORTANTE: Substitua "ApplicationDbContext" pelo DbContext real do EasyStock.
    // Ver CLAUDE.md, passo 3 pra integracao com o DbContext existente.
    private readonly ApplicationDbContext _db;

    public SyncController(ApplicationDbContext db)
    {
        _db = db;
    }

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
                await ApplyMutation(m, req.DeviceId);
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
        // Busca entidades alteradas apos "since" por outros devices
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

    // ---- Apply mutation por tipo ----
    private async Task ApplyMutation(MutationDto m, string deviceId)
    {
        var parts = m.Type.Split('.');
        if (parts.Length != 2) throw new ArgumentException($"Tipo invalido: {m.Type}");

        switch (parts[0])
        {
            case "product":  await ApplyProduct(m, deviceId); break;
            case "client":   await ApplyClient(m, deviceId); break;
            case "order":    await ApplyOrder(m, deviceId); break;
            case "batch":    await ApplyBatch(m, deviceId); break;
            case "cashEntry": await ApplyCashEntry(m, deviceId); break;
            default: throw new ArgumentException($"Entidade desconhecida: {parts[0]}");
        }
    }

    private async Task ApplyProduct(MutationDto m, string deviceId)
    {
        var dto = m.Payload.Deserialize<ProductDto>(JsonOpts)!;
        var existing = await _db.Set<Product>().FindAsync(dto.Id);
        if (existing == null)
        {
            _db.Add(new Product
            {
                Id = dto.Id, Name = dto.Name, Emoji = dto.Emoji, Category = dto.Category,
                Unit = dto.Unit, Price = dto.Price, Stock = dto.Stock,
                IsCustom = dto.Custom ?? false, LastDeviceId = deviceId
            });
        }
        else
        {
            // Last-write-wins: sempre aplica. Se quiser conflict resolution,
            // compare existing.UpdatedAt com m.Ts.
            existing.Name = dto.Name;
            existing.Emoji = dto.Emoji;
            existing.Category = dto.Category;
            existing.Unit = dto.Unit;
            existing.Price = dto.Price;
            existing.Stock = dto.Stock;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.LastDeviceId = deviceId;
        }
    }

    private async Task ApplyClient(MutationDto m, string deviceId)
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
                LastDeviceId = deviceId
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
        }
    }

    private async Task ApplyOrder(MutationDto m, string deviceId)
    {
        var dto = m.Payload.Deserialize<OrderDto>(JsonOpts)!;
        var existing = await _db.Set<Order>().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == dto.Id);
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.UpdatedAt).UtcDateTime;

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
                LastDeviceId = deviceId
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
            // Substitui os itens (simplificacao - em producao, faca diff item-a-item)
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
    /// Regra de estoque central: o app ja desconta localmente, mas o backend
    /// e fonte da verdade ao conciliar com outros devices. Espelha a mesma logica.
    /// </summary>
    private async Task ApplyStockRule(string oldStatus, string newStatus, List<OrderItemDto> items)
    {
        // Transicao pra "pronto": desconta
        if (oldStatus != "pronto" && oldStatus != "entregue"
            && (newStatus == "pronto" || newStatus == "entregue"))
        {
            foreach (var i in items)
            {
                var p = await _db.Set<Product>().FindAsync(i.ProductId);
                if (p != null) p.Stock -= i.Qty;
            }
        }
        // Cancelamento de pedido que ja havia reservado: devolve
        if ((oldStatus == "pronto" || oldStatus == "entregue") && newStatus == "cancelado")
        {
            foreach (var i in items)
            {
                var p = await _db.Set<Product>().FindAsync(i.ProductId);
                if (p != null) p.Stock += i.Qty;
            }
        }
    }

    private async Task ApplyBatch(MutationDto m, string deviceId)
    {
        var dto = m.Payload.Deserialize<BatchDto>(JsonOpts)!;
        var existing = await _db.Set<Batch>().Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == dto.Id);
        if (existing != null) return; // Batches sao imutaveis - ignora re-envio

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        var batch = new Batch
        {
            Id = dto.Id, Code = dto.Code, BatchPhoto = dto.BatchPhoto,
            CreatedAt = createdAt, LastDeviceId = deviceId
        };
        foreach (var i in dto.Items)
        {
            batch.Items.Add(new BatchItem
            {
                BatchId = dto.Id, ProductId = i.ProductId, Name = i.Name,
                Emoji = i.Emoji, Unit = i.Unit, Qty = i.Qty, Photo = i.Photo
            });
            // Incrementa estoque
            var p = await _db.Set<Product>().FindAsync(i.ProductId);
            if (p != null) p.Stock += i.Qty;
        }
        _db.Add(batch);
    }

    private async Task ApplyCashEntry(MutationDto m, string deviceId)
    {
        var dto = m.Payload.Deserialize<CashEntryDto>(JsonOpts)!;
        var existing = await _db.Set<CashEntry>().FindAsync(dto.Id);
        if (existing != null) return; // imutavel

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        _db.Add(new CashEntry
        {
            Id = dto.Id, Type = dto.Type, Amount = dto.Amount,
            Description = dto.Description, CreatedAt = createdAt,
            LastDeviceId = deviceId
        });
    }

    // ---- DTO conversores pra pull ----
    private ProductDto ToDto(Product p) =>
        new(p.Id, p.Name, p.Emoji, p.Category, p.Unit, p.Price, p.Stock, p.IsCustom);

    private ClientDto ToDto(Client c) =>
        new(c.Id, c.Name, c.Apt, c.Address, c.Phone,
            new DateTimeOffset(c.LastOrder).ToUnixTimeMilliseconds(), c.OrderCount);

    private OrderDto ToDto(Order o) =>
        new(o.Id, o.ClientId,
            new ClientSnapshotDto(o.ClientSnapshotName, o.ClientSnapshotRef),
            o.Items.Select(i => new OrderItemDto(i.ProductId, i.Name, i.Emoji, i.Unit, i.Qty, i.UnitPrice)).ToList(),
            o.Notes, o.Total, o.Status,
            new DateTimeOffset(o.CreatedAt).ToUnixTimeMilliseconds(),
            new DateTimeOffset(o.UpdatedAt).ToUnixTimeMilliseconds());

    private BatchDto ToDto(Batch b) =>
        new(b.Id, b.Code,
            b.Items.Select(i => new BatchItemDto(i.ProductId, i.Name, i.Emoji, i.Unit, i.Qty, i.Photo)).ToList(),
            b.BatchPhoto,
            new DateTimeOffset(b.CreatedAt).ToUnixTimeMilliseconds());

    private CashEntryDto ToDto(CashEntry c) =>
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
