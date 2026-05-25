using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Applies a single mobile mutation (product/client/order/batch/cashEntry/closing)
/// to the database. Extracted from SyncController to keep the HTTP layer thin.
/// All Apply* methods are last-write-wins with conflict detection via timestamps.
/// </summary>
public class SyncMutationDispatcher(
    EasyStockDbContext db,
    MobileStockReconciler stockReconciler,
    MobileSaleSyncService saleSync,
    MobileEventBroker eventBroker,
    IProdutoRepository produtoRepo,
    ILogger<SyncMutationDispatcher> log)
{
    private readonly EasyStockDbContext _db = db;
    private readonly MobileStockReconciler _stockReconciler = stockReconciler;
    private readonly MobileSaleSyncService _saleSync = saleSync;
    private readonly MobileEventBroker _eventBroker = eventBroker;
    private readonly IProdutoRepository _produtoRepo = produtoRepo;
    private readonly ILogger<SyncMutationDispatcher> _log = log;

    public async Task ApplyMutationAsync(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var parts = m.Type.Split('.');
        if (parts.Length != 2) throw new ArgumentException($"Tipo invalido: {m.Type}");

        switch (parts[0])
        {
            case "product": await ApplyProduct(m, deviceId, operatorName, empresaId, lojaId); break;
            case "client": await ApplyClient(m, deviceId, operatorName, empresaId, lojaId); break;
            case "order": await ApplyOrder(m, deviceId, operatorName, empresaId, lojaId); break;
            case "batch": await ApplyBatch(m, deviceId, operatorName, empresaId, lojaId); break;
            case "cashEntry": await ApplyCashEntry(m, deviceId, operatorName, empresaId, lojaId); break;
            case "closing": await ApplyClosing(m, deviceId, empresaId, lojaId); break;
            default: throw new ArgumentException($"Entidade desconhecida: {parts[0]}");
        }
    }

    private async Task ApplyProduct(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = m.Payload.Deserialize<ProductDto>(SyncDtoConverters.JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix): tenant guard.
        var existing = await _db.Set<Product>()
            .FirstOrDefaultAsync(p => p.Id == dto.Id && p.EmpresaId == empresaId);

        // Onda 5: conflict detection. Tolerância de 2s pra clock skew.
        if (existing != null && m.Ts > 0)
        {
            var serverTsMs = new DateTimeOffset(existing.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (serverTsMs > m.Ts + 2000 && existing.LastDeviceId != null && existing.LastDeviceId != deviceId)
            {
                throw new ConflictException(
                    $"Servidor já tem versão mais nova ({DateTimeOffset.FromUnixTimeMilliseconds(serverTsMs):HH:mm:ss}) " +
                    $"editada por {existing.LastOperatorName ?? "outro device"}",
                    SyncDtoConverters.Serialize(SyncDtoConverters.ToDto(existing)));
            }
        }

        if (existing == null)
        {
            _db.Add(new Product
            {
                Id = dto.Id,
                Name = dto.Name,
                Emoji = dto.Emoji,
                Category = dto.Category,
                Unit = dto.Unit,
                Price = dto.Price,
                Stock = dto.Stock,
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
            existing.Name = dto.Name;
            existing.Emoji = dto.Emoji;
            existing.Category = dto.Category;
            existing.Unit = dto.Unit;
            existing.Price = dto.Price;
            existing.Stock = dto.Stock;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.LastDeviceId = deviceId;
            existing.LastOperatorName = operatorName;
            if (existing.EmpresaId == null && empresaId.HasValue) existing.EmpresaId = empresaId;
            if (existing.LojaId == null && lojaId.HasValue) existing.LojaId = lojaId;
            if (dto.Sku is not null) existing.Sku = dto.Sku;
            if (dto.DefaultWeightG.HasValue) existing.DefaultWeightG = dto.DefaultWeightG;
            if (dto.DefaultValidityDays.HasValue) existing.DefaultValidityDays = dto.DefaultValidityDays;
        }
    }

    private async Task ApplyClient(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = m.Payload.Deserialize<ClientDto>(SyncDtoConverters.JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<Client>()
            .FirstOrDefaultAsync(c => c.Id == dto.Id && c.EmpresaId == empresaId);
        var lastOrderDate = DateTimeOffset.FromUnixTimeMilliseconds(dto.LastOrder).UtcDateTime;
        if (existing == null)
        {
            _db.Add(new Client
            {
                Id = dto.Id,
                Name = dto.Name,
                Apt = dto.Apt,
                Address = dto.Address,
                Phone = dto.Phone,
                LastOrder = lastOrderDate,
                OrderCount = dto.OrderCount,
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
        var dto = m.Payload.Deserialize<OrderDto>(SyncDtoConverters.JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<Order>().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == dto.Id && o.EmpresaId == empresaId);
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.UpdatedAt).UtcDateTime;

        // C3: conflict detection (last-write-loser). Tolerancia 2s pra clock skew.
        if (existing != null && m.Ts > 0)
        {
            var serverTsMs = new DateTimeOffset(existing.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (serverTsMs > m.Ts + 2000 && existing.LastDeviceId != null && existing.LastDeviceId != deviceId)
            {
                throw new ConflictException(
                    $"Pedido editado em {DateTimeOffset.FromUnixTimeMilliseconds(serverTsMs):HH:mm:ss} " +
                    $"por {existing.LastOperatorName ?? "outro device"} — status atual: {existing.Status}",
                    SyncDtoConverters.Serialize(SyncDtoConverters.ToDto(existing)));
            }
        }

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
                    OrderId = dto.Id,
                    ProductId = i.ProductId,
                    Name = i.Name,
                    Emoji = i.Emoji,
                    Unit = i.Unit,
                    Qty = i.Qty,
                    UnitPrice = i.UnitPrice
                });
            _db.Add(order);
            // Onda 3 — pedido criado direto como "entregue" (retroativo) cria Venda.
            if (order.Status == "entregue")
            {
                await _saleSync.CreateVendaForDeliveredOrderAsync(order, dto.Items);
            }
        }
        else
        {
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
            if (historyJson is not null) existing.HistoryJson = historyJson;
            if (dto.ConfirmedBy is not null) existing.ConfirmedBy = dto.ConfirmedBy;
            if (confirmedAt.HasValue) existing.ConfirmedAt = confirmedAt;
            if (factAt.HasValue) existing.FactAt = factAt;
            if (dto.ScheduledDeliveryAt.HasValue) existing.ScheduledDeliveryAt = scheduledDeliveryAt;
            _db.RemoveRange(existing.Items);
            foreach (var i in dto.Items)
                existing.Items.Add(new OrderItem
                {
                    OrderId = dto.Id,
                    ProductId = i.ProductId,
                    Name = i.Name,
                    Emoji = i.Emoji,
                    Unit = i.Unit,
                    Qty = i.Qty,
                    UnitPrice = i.UnitPrice
                });

            // Onda 3 — vendas mobile -> ERP.
            if (oldStatus != "entregue" && dto.Status == "entregue")
            {
                await _saleSync.CreateVendaForDeliveredOrderAsync(existing, dto.Items);
            }
            else if (oldStatus == "entregue" && dto.Status == "cancelado")
            {
                await _saleSync.CancelVendaForOrderAsync(existing);
            }

            // C4 — Transicao para "pronto" alerta garcom em outros devices via SSE.
            if (oldStatus != "pronto" && dto.Status == "pronto")
            {
                try
                {
                    await _eventBroker.NotifyOrderReadyAsync(
                        existing.EmpresaId, existing.LojaId, deviceId,
                        existing.Id, existing.ClientSnapshotName, existing.Total, existing.Items.Count);
                }
                catch { /* fail-safe — nao bloqueia sync */ }
            }
        }
    }

    /// <summary>
    /// Regra de estoque central: reconcilia movimentação ERP quando produto está linkado.
    /// Onda 2 parte 2: quando ErpProductId preenchido, espelha em itens_estoque +
    /// movimentacoes_estoque. Falha NÃO interrompe sync.
    /// </summary>
    private async Task ApplyStockRule(string oldStatus, string newStatus, List<OrderItemDto> items,
        Guid? empresaId, string? orderId = null)
    {
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
        var dto = m.Payload.Deserialize<BatchDto>(SyncDtoConverters.JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<Batch>().Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == dto.Id && b.EmpresaId == empresaId);
        if (existing != null) return; // Batches são imutáveis — ignora re-envio

        // C2 (RDC 727/2022): valida peso obrigatorio para itens Embalados.
        if (empresaId.HasValue && dto.Items != null && dto.Items.Count > 0)
        {
            var produtoIdsErp = dto.Items
                .Where(i => Guid.TryParse(i.ProductId, out _))
                .Select(i => Guid.Parse(i.ProductId))
                .Distinct()
                .ToList();
            var tipoMap = produtoIdsErp.Count > 0
                ? await _produtoRepo.GetTipoEmbalagemMapAsync(empresaId.Value, produtoIdsErp)
                : new Dictionary<Guid, TipoEmbalagem>();
            foreach (var i in dto.Items)
            {
                if (Guid.TryParse(i.ProductId, out var pid)
                    && tipoMap.TryGetValue(pid, out var t)
                    && t == TipoEmbalagem.Embalado
                    && (i.WeightG == null || i.WeightG <= 0))
                {
                    throw new InvalidOperationException(
                        $"Item '{i.Name}' precisa de peso (produto Embalado — RDC 727/2022). " +
                        $"Atualize o PWA e informe o peso por unidade.");
                }
            }
        }

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        var batch = new Batch
        {
            Id = dto.Id,
            Code = dto.Code,
            BatchPhoto = dto.BatchPhoto,
            CreatedAt = createdAt,
            Lote = dto.Lote,
            LastDeviceId = deviceId,
            LastOperatorName = operatorName,
            EmpresaId = empresaId,
            LojaId = lojaId
        };
        if (dto.Items is null)
            throw new InvalidOperationException(
                $"Batch {dto.Id} chegou sem coleção Items — payload mal-formado do PWA. " +
                "Cliente deveria enviar Items=[] em vez de null; rejeita p/ nao corromper estoque.");

        foreach (var i in dto.Items)
        {
            batch.Items.Add(new BatchItem
            {
                BatchId = dto.Id,
                ProductId = i.ProductId,
                Name = i.Name,
                Emoji = i.Emoji,
                Unit = i.Unit,
                Qty = i.Qty,
                Photo = i.Photo,
                WeightG = i.WeightG,
                ValidityDays = i.ValidityDays,
                ExpiresAt = i.ExpiresAt.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(i.ExpiresAt.Value).UtcDateTime
                    : (DateTime?)null
            });
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
        var dto = m.Payload.Deserialize<CashEntryDto>(SyncDtoConverters.JsonOpts)!;
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<CashEntry>()
            .FirstOrDefaultAsync(c => c.Id == dto.Id && c.EmpresaId == empresaId);
        if (existing != null) return; // imutável

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        _db.Add(new CashEntry
        {
            Id = dto.Id,
            Type = dto.Type,
            Amount = dto.Amount,
            Description = dto.Description,
            CreatedAt = createdAt,
            LastDeviceId = deviceId,
            LastOperatorName = operatorName,
            EmpresaId = empresaId,
            LojaId = lojaId
        });
    }

    /// <summary>
    /// F7-C — aplica fechamento de caixa enviado pelo mobile (cashClosings.upsert).
    /// Idempotente: (EmpresaId + Data) unique — segunda chamada do mesmo dia faz update.
    /// </summary>
    private async Task ApplyClosing(MutationDto m, string deviceId, Guid? empresaId, Guid? lojaId)
    {
        if (!empresaId.HasValue) return;
        CashClosingDto? dto;
        try { dto = m.Payload.Deserialize<CashClosingDto>(SyncDtoConverters.JsonOpts); }
        catch { return; }
        if (dto == null) return;
        if (!DateOnly.TryParse(dto.DateKey, out var data)) return;

        var existing = await _db.Set<FechamentoCaixa>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.EmpresaId == empresaId && f.Data == data
                && (lojaId == null || f.LojaId == lojaId || f.LojaId == null));

        var closedAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.ClosedAt).UtcDateTime;
        if (existing == null)
        {
            var f = FechamentoCaixa.Criar(
                empresaId: empresaId.Value,
                data: data,
                saldoInicial: 0,
                totalVendas: 0,
                totalPagamentosPedidos: dto.TotalPagamentosPedidos,
                totalEntradasExtras: 0,
                totalSaidasExtras: dto.TotalSaidasExtras,
                lojaId: lojaId);
            f.SaldoFinal = dto.SaldoFinal;
            f.FechadoEm = closedAt;
            f.FechadoPorNome = dto.ClosedByName;
            f.Observacoes = dto.Notes;
            _db.Add(f);
            _log.LogInformation("F7-C Fechamento CRIADO: empresa={EmpresaId} data={Data} saldo={Saldo}",
                empresaId, data, dto.SaldoFinal);
        }
        else
        {
            existing.TotalPagamentosPedidos = dto.TotalPagamentosPedidos;
            existing.TotalSaidasExtras = dto.TotalSaidasExtras;
            existing.SaldoFinal = dto.SaldoFinal;
            existing.FechadoEm = closedAt;
            if (!string.IsNullOrWhiteSpace(dto.ClosedByName)) existing.FechadoPorNome = dto.ClosedByName;
            if (!string.IsNullOrWhiteSpace(dto.Notes)) existing.Observacoes = dto.Notes;
            _log.LogInformation("F7-C Fechamento ATUALIZADO: empresa={EmpresaId} data={Data}", empresaId, data);
        }
    }
}
