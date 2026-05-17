using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyStock.Mobile.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Mobile.Controllers;

/// <summary>
/// Visões agregadas de produção. O SyncController cuida da gravação
/// (BatchItem por etiqueta, rastreio individual); aqui ficam as consultas
/// que a UI consolidada usa.
///
/// Rotas:
///   GET /api/mobile/production/daily?date=YYYY-MM-DD  - agregado por produto/dia
///   GET /api/mobile/orders/today?date=YYYY-MM-DD      - encomendas do dia + cobertura
/// </summary>
[ApiController]
[Route("api/mobile")]
public class ProductionController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ProductionController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet("production/daily")]
    public async Task<ActionResult<List<DailyProductionRow>>> Daily([FromQuery] string? date)
    {
        var day = ParseDate(date) ?? DateTime.UtcNow.Date;
        var start = day;
        var end = day.AddDays(1);

        var items = await _db.Set<BatchItem>()
            .Include(i => i.Batch)
            .Where(i => i.Batch != null && i.Batch.CreatedAt >= start && i.Batch.CreatedAt < end)
            .ToListAsync();

        var products = await _db.Set<Product>().ToDictionaryAsync(p => p.Id, p => p);

        var rows = items
            .GroupBy(i => i.ProductId)
            .Select(g =>
            {
                products.TryGetValue(g.Key, out var p);
                var totalGrams = g.Sum(x => x.WeightGrams ?? 0);
                var totalUnits = g.Sum(x => x.Qty);
                return new DailyProductionRow(
                    g.Key,
                    p?.Name ?? g.First().Name,
                    p?.Emoji ?? g.First().Emoji,
                    p?.DefaultUnit ?? "unidades",
                    totalGrams,
                    totalUnits,
                    g.Count(),
                    g.OrderByDescending(x => x.Batch!.CreatedAt)
                     .Select(x => new DailyProductionLabel(
                         x.Id,
                         x.BatchId,
                         x.Batch!.Code,
                         x.Qty,
                         x.WeightGrams,
                         new DateTimeOffset(x.Batch.CreatedAt).ToUnixTimeMilliseconds()))
                     .ToList());
            })
            .OrderBy(r => r.Name)
            .ToList();

        return Ok(rows);
    }

    [HttpGet("orders/today")]
    public async Task<ActionResult<OrdersTodayResponse>> OrdersToday([FromQuery] string? date)
    {
        var day = ParseDate(date) ?? DateTime.UtcNow.Date;

        var orders = await _db.Set<Order>()
            .Include(o => o.Items)
            .Where(o => o.ScheduledFor == day && o.Status != "cancelado")
            .OrderBy(o => o.ScheduledWindow)
            .ToListAsync();

        // Produção do dia, somada por produto (gramas + unidades).
        var prodStart = day;
        var prodEnd = day.AddDays(1);
        var produced = await _db.Set<BatchItem>()
            .Include(i => i.Batch)
            .Where(i => i.Batch != null && i.Batch.CreatedAt >= prodStart && i.Batch.CreatedAt < prodEnd)
            .GroupBy(i => i.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Grams = g.Sum(x => x.WeightGrams ?? 0),
                Units = g.Sum(x => x.Qty)
            })
            .ToDictionaryAsync(x => x.ProductId);

        var products = await _db.Set<Product>().ToDictionaryAsync(p => p.Id, p => p);

        var result = orders.Select(o => new OrderTodayRow(
            o.Id,
            o.ClientSnapshotName,
            o.Status,
            o.ScheduledWindow,
            o.Items.Select(it =>
            {
                products.TryGetValue(it.ProductId, out var p);
                var isGrams = p?.DefaultUnit == "gramas";
                var ordered = isGrams ? (p?.DefaultGrams ?? 0) * it.Qty : it.Qty;
                produced.TryGetValue(it.ProductId, out var pr);
                var producedAmount = isGrams ? (pr?.Grams ?? 0) : (pr?.Units ?? 0);
                string coverage = producedAmount >= ordered ? "ok"
                                : producedAmount <= 0 ? "faltando"
                                : "parcial";
                return new OrderTodayItem(
                    it.ProductId, it.Name, isGrams ? "gramas" : "unidades",
                    ordered, producedAmount, coverage);
            }).ToList()
        )).ToList();

        var summary = new OrdersTodaySummary(
            result.Count,
            result.Count(r => r.Items.All(i => i.Coverage == "ok")),
            result.Count(r => r.Items.Any(i => i.Coverage == "faltando")),
            result.Count(r => r.Items.Any(i => i.Coverage == "parcial") && r.Items.All(i => i.Coverage != "faltando"))
        );

        return Ok(new OrdersTodayResponse(day.ToString("yyyy-MM-dd"), summary, result));
    }

    private static DateTime? ParseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return null;
        return DateTime.TryParse(iso, out var d) ? d.Date : null;
    }
}

public record DailyProductionRow(
    string ProductId,
    string Name,
    string? Emoji,
    string Unit,           // "gramas" ou "unidades"
    int TotalGrams,
    int TotalUnits,
    int LabelCount,
    List<DailyProductionLabel> Items
);

public record DailyProductionLabel(
    long BatchItemId,
    string BatchId,
    string BatchCode,
    int Qty,
    int? WeightGrams,
    long CreatedAt
);

public record OrdersTodayResponse(
    string Date,
    OrdersTodaySummary Summary,
    List<OrderTodayRow> Orders
);

public record OrdersTodaySummary(int Total, int Ok, int Faltando, int Parcial);

public record OrderTodayRow(
    string Id,
    string ClientName,
    string Status,
    string? Window,
    List<OrderTodayItem> Items
);

public record OrderTodayItem(
    string ProductId,
    string Name,
    string Unit,
    int QtyOrdered,
    int QtyProducedToday,
    string Coverage  // "ok" | "parcial" | "faltando"
);
