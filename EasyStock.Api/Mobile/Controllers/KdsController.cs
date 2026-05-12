using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// KDS (Kitchen Display System) — versao alpha. Display dedicado pra cozinha
/// que escuta o stream SSE existente (mutations-applied via MobileEventBroker)
/// e mostra pedidos abertos com botao "Pronto" pra bumpar.
///
/// Reusa toda a infra mobile existente: pareamento via X-Mobile-Api-Key,
/// Order/OrderItem mobile, broker in-memory pra realtime. Sem SignalR, sem
/// novo notifier no Application — o KDS e uma view paralela do mesmo domain
/// que a PWA balcao ja opera.
///
/// Endpoints:
///   GET   /api/kds/pedidos?statuses=aguardando,preparando — snapshot atual
///   PATCH /api/kds/pedidos/{id}/status                    — bumpa status
/// </summary>
[ApiController]
[Route("api/kds")]
[MobileApiKey]
[AllowAnonymous]
public class KdsController(
    EasyStockDbContext db,
    MobileEventBroker eventBroker,
    ILogger<KdsController> log) : ControllerBase
{
    private static readonly HashSet<string> StatusesPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        "aguardando", "preparando", "pronto", "entregue", "cancelado"
    };

    private static readonly HashSet<string> StatusesAtivosDefault = new(StringComparer.OrdinalIgnoreCase)
    {
        "aguardando", "preparando"
    };

    [HttpGet("pedidos")]
    public async Task<ActionResult<KdsOrderDto[]>> GetPedidos(
        [FromQuery] string? statuses,
        CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device == null) return Unauthorized();

        var filtro = ParsearStatuses(statuses);

        // KDS so puxa pedidos do dia: nao-agendados aparecem sempre; agendados
        // aparecem 24h antes da entrega. Cobre fuso BR sem precisar saber TZ
        // da empresa (UtcNow + 24h da janela de 1 dia em qualquer fuso).
        var janelaKds = DateTime.UtcNow.AddHours(24);

        var query = db.Set<Order>()
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.EmpresaId == device.EmpresaId
                     && o.LojaId == device.LojaId
                     && filtro.Contains(o.Status)
                     && (o.ScheduledDeliveryAt == null || o.ScheduledDeliveryAt <= janelaKds));

        var orders = await query
            .OrderBy(o => o.CreatedAt)
            .Take(200) // sanity cap
            .ToListAsync(ct);

        var result = orders.Select(MapToDto).ToArray();
        return Ok(result);
    }

    [HttpPatch("pedidos/{id}/status")]
    public async Task<ActionResult<KdsOrderDto>> AtualizarStatus(
        string id,
        [FromBody] AtualizarStatusInput input,
        CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device == null) return Unauthorized();

        if (input == null || string.IsNullOrWhiteSpace(input.Status))
            return BadRequest(new { error = "status obrigatorio" });

        var statusNovo = input.Status.Trim().ToLowerInvariant();
        if (!StatusesPermitidos.Contains(statusNovo))
            return BadRequest(new { error = $"status invalido: {statusNovo}" });

        var order = await db.Set<Order>()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.EmpresaId == device.EmpresaId, ct);
        if (order == null) return NotFound();

        // Tenant guard explicito (defesa em camadas) — Order.LojaId e nullable
        // por compat legado, mas device.LojaId nunca e null.
        if (order.LojaId.HasValue && order.LojaId.Value != device.LojaId)
            return StatusCode(403, new { error = "pedido fora da loja do device" });

        if (string.Equals(order.Status, statusNovo, StringComparison.OrdinalIgnoreCase))
            return Ok(MapToDto(order)); // idempotente

        var statusAntigo = order.Status;
        order.Status = statusNovo;
        order.UpdatedAt = DateTime.UtcNow;
        order.LastDeviceId = device.Id;
        order.LastOperatorName = input.OperadorNome ?? device.Label;
        if (statusNovo == "entregue") order.ConfirmedAt ??= order.UpdatedAt;

        await db.SaveChangesAsync(ct);

        // Dispara mutations-applied pros outros devices da loja (PWA balcao,
        // outros KDS). Reusa fail-safe documentado: se broker fora, polling
        // 30s da PWA recupera. Origin = device atual pra evitar eco.
        await eventBroker.NotifyMutationsAppliedAsync(
            order.EmpresaId, order.LojaId, device.Id, mutationCount: 1);

        log.LogInformation("KDS bumpou pedido {Id}: {De} → {Para} (device={Device})",
            order.Id, statusAntigo, statusNovo, device.Id);

        return Ok(MapToDto(order));
    }

    private HashSet<string> ParsearStatuses(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return StatusesAtivosDefault;
        var lista = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(s => StatusesPermitidos.Contains(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return lista.Count == 0 ? StatusesAtivosDefault : lista;
    }

    private static KdsOrderDto MapToDto(Order o) => new(
        Id: o.Id,
        Status: o.Status,
        ClienteNome: o.ClientSnapshotName,
        ClienteRef: o.ClientSnapshotRef,
        Notes: o.Notes,
        Total: o.Total,
        CriadoEm: new DateTimeOffset(o.CreatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds(),
        AtualizadoEm: new DateTimeOffset(o.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds(),
        // F5 — campo de agendamento (long? unix ms). PWA usa pra ordenar/badge.
        ScheduledDeliveryAt: o.ScheduledDeliveryAt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(o.ScheduledDeliveryAt.Value, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeMilliseconds()
            : (long?)null,
        Itens: o.Items.Select(i => new KdsOrderItemDto(
            ProdutoId: i.ProductId,
            Nome: i.Name,
            Emoji: i.Emoji,
            Unidade: i.Unit,
            Quantidade: i.Qty
        )).ToArray()
    );
}

public record AtualizarStatusInput(string Status, string? OperadorNome);

public record KdsOrderDto(
    string Id,
    string Status,
    string? ClienteNome,
    string? ClienteRef,
    string? Notes,
    decimal Total,
    long CriadoEm,
    long AtualizadoEm,
    KdsOrderItemDto[] Itens,
    // F5 — agendamento (MVP). NULL = sem agendamento (caso padrão).
    long? ScheduledDeliveryAt = null
);

public record KdsOrderItemDto(
    string ProdutoId,
    string Nome,
    string? Emoji,
    string? Unidade,
    int Quantidade
);
