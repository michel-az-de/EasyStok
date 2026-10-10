using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Common;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Applies a single mobile mutation (product/client/order/batch/cashEntry/closing)
/// to the database. Extracted from SyncController to keep the HTTP layer thin.
/// All Apply* methods are last-write-wins with conflict detection via timestamps.
/// </summary>
public class SyncMutationDispatcher(
    EasyStockDbContext db,
    MobileStockReconciler stockReconciler,
    LoteMobileEstadoReconciler loteEstado,
    MobileSaleSyncService saleSync,
    OperacaoEventBroker eventBroker,
    IProdutoRepository produtoRepo,
    EstornarMovimentoCaixaUseCase estornarMovimentoCaixa,
    ILogger<SyncMutationDispatcher> log)
{
    private readonly EasyStockDbContext _db = db;
    private readonly MobileStockReconciler _stockReconciler = stockReconciler;
    private readonly LoteMobileEstadoReconciler _loteEstado = loteEstado;
    private readonly MobileSaleSyncService _saleSync = saleSync;
    private readonly OperacaoEventBroker _eventBroker = eventBroker;
    private readonly IProdutoRepository _produtoRepo = produtoRepo;
    private readonly EstornarMovimentoCaixaUseCase _estornarMovimentoCaixa = estornarMovimentoCaixa;
    private readonly ILogger<SyncMutationDispatcher> _log = log;

    // #1520 (ADR-0060): produtos que nasceram neste lote ja com o saldo do aparelho. Esse saldo
    // inclui os movimentos que vieram no mesmo lote; soma-los de novo contaria em dobro.
    private readonly HashSet<string> _nascidosComSaldoDoAparelho = new(StringComparer.Ordinal);

    public async Task ApplyMutationAsync(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var parts = m.Type.Split('.');
        if (parts.Length != 2) throw new ArgumentException($"Tipo invalido: {m.Type}");
        // #1509: texto maior que a coluna derrubava o SaveChanges do lote inteiro (22001).
        operatorName = Cortar(operatorName, 64);

        switch (parts[0])
        {
            case "product":   await ApplyProduct(m, deviceId, operatorName, empresaId, lojaId);   break;
            case "client":    await ApplyClient(m, deviceId, operatorName, empresaId, lojaId);    break;
            case "order":     await ApplyOrder(m, deviceId, operatorName, empresaId, lojaId);     break;
            case "batch":     await ApplyBatch(m, deviceId, operatorName, empresaId, lojaId);     break;
            case "cashEntry" when parts[1] == "delete":
                await ApplyCashEntryDelete(m, deviceId, operatorName, empresaId);
                break;
            case "cashEntry": await ApplyCashEntry(m, deviceId, operatorName, empresaId, lojaId); break;
            case "closing":   await ApplyClosing(m, deviceId, empresaId, lojaId);                 break;
            case "stock":     await ApplyStockDelta(m, deviceId, operatorName, empresaId);        break;
            default: throw new ArgumentException($"Entidade desconhecida: {parts[0]}");
        }
    }

    private async Task ApplyProduct(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = CaberNasColunas(m.Payload.Deserialize<ProductDto>(SyncDtoConverters.JsonOpts)!);
        // #1520: com movimento de estoque ("stock.delta") o saldo do cadastro nao e gravado em
        // produto que ja existe; so vale quando o produto nasce aqui.
        var saldoVemNoCadastro = dto.StockByDelta != true && m.Payload.TryGetProperty("stock", out _);
        // Auditoria 2026-04-30 (CRITICAL fix): tenant guard.
        // FindAsync enxerga o produto criado neste mesmo lote, ainda nao salvo.
        var existing = await _db.Set<Product>().FindAsync(dto.Id);
        if (existing != null && existing.EmpresaId != empresaId) existing = null;

        // Onda 5: conflict detection. Tolerância de 2s pra clock skew.
        // #1520: compara com o carimbo do servidor (quando ele gravou), nao com UpdatedAt.
        if (existing != null && m.Ts > 0)
        {
            var serverTsMs = CarimboMs(existing.ServerUpdatedAt);
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
                Id = dto.Id, Name = dto.Name, Emoji = dto.Emoji, Category = dto.Category,
                Unit = dto.Unit, Price = dto.Price, Stock = dto.Stock,
                IsCustom = dto.Custom ?? false,
                Sku = dto.Sku,
                DefaultWeightG = dto.DefaultWeightG,
                DefaultValidityDays = dto.DefaultValidityDays,
                Cost = CustoValido(dto.Cost),
                MinStock = MinimoValido(dto.MinStock),
                LastDeviceId = deviceId,
                LastOperatorName = operatorName,
                EmpresaId = empresaId,
                LojaId = lojaId
            });
            if (dto.StockByDelta == true) _nascidosComSaldoDoAparelho.Add(dto.Id);
        }
        else
        {
            existing.Name = dto.Name;
            existing.Emoji = dto.Emoji;
            existing.Category = dto.Category;
            existing.Unit = dto.Unit;
            existing.Price = dto.Price;
            if (saldoVemNoCadastro) existing.Stock = dto.Stock;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.LastDeviceId = deviceId;
            existing.LastOperatorName = operatorName;
            if (existing.EmpresaId == null && empresaId.HasValue) existing.EmpresaId = empresaId;
            if (existing.LojaId == null && lojaId.HasValue) existing.LojaId = lojaId;
            if (dto.Sku is not null)                 existing.Sku = dto.Sku;
            if (dto.DefaultWeightG.HasValue)         existing.DefaultWeightG = dto.DefaultWeightG;
            if (dto.DefaultValidityDays.HasValue)    existing.DefaultValidityDays = dto.DefaultValidityDays;
            // #1467: so mexe quando o campo veio no payload (null explicito = apagado no PWA).
            if (m.Payload.TryGetProperty("cost", out _))     existing.Cost = CustoValido(dto.Cost);
            if (m.Payload.TryGetProperty("minStock", out _)) existing.MinStock = MinimoValido(dto.MinStock);
        }
    }

    /// <summary>
    /// #1520 (ADR-0060) — soma um movimento de estoque do aparelho ao espelho do produto. E o
    /// unico caminho, alem do saldo absoluto do cadastro de aparelho antigo, que altera
    /// <c>mobile_products.Stock</c>: pedido e lote nao recontam o que o aparelho ja contou.
    /// </summary>
    private async Task ApplyStockDelta(MutationDto m, string deviceId, string? operatorName, Guid? empresaId)
    {
        var dto = m.Payload.Deserialize<StockDeltaDto>(SyncDtoConverters.JsonOpts)!;
        // FindAsync enxerga o produto criado neste mesmo lote, ainda nao salvo.
        var p = await _db.Set<Product>().FindAsync(dto.ProductId);
        if (p == null || p.EmpresaId != empresaId)
            throw new InvalidOperationException(
                $"Movimento de estoque de {dto.Qty:+#;-#;0} não aplicado: o produto '{dto.ProductId}' não existe no servidor.");
        if (dto.Qty == 0 || _nascidosComSaldoDoAparelho.Contains(p.Id)) return;
        p.Stock += dto.Qty;
        p.UpdatedAt = DateTime.UtcNow;
        p.LastDeviceId = deviceId;
        p.LastOperatorName = operatorName;
    }

    private static decimal? CustoValido(decimal? c) => c is >= 0 ? c : null;
    private static int? MinimoValido(int? q) => q is >= 0 ? q : null;

    private async Task ApplyClient(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = CaberNasColunas(m.Payload.Deserialize<ClientDto>(SyncDtoConverters.JsonOpts)!);
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
        var dto = CaberNasColunas(m.Payload.Deserialize<OrderDto>(SyncDtoConverters.JsonOpts)!);
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<Order>().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == dto.Id && o.EmpresaId == empresaId);
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.UpdatedAt).UtcDateTime;

        // C3: conflict detection (last-write-loser). Tolerancia 2s pra clock skew.
        // #1520 (ADR-0060): compara com o carimbo do servidor. O UpdatedAt do pedido e a hora do
        // aparelho que editou por ultimo: relogio adiantado ali fazia todo outro aparelho perder
        // (conflito falso), e relogio atrasado deixava edicao velha passar por cima da nova.
        if (existing != null && m.Ts > 0)
        {
            var serverTsMs = CarimboMs(existing.ServerUpdatedAt);
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
                Metodo = FormaPagamentoMobile.Normalizar(dto.Metodo),
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
            // #1493 — reenvio sem forma (aparelho antigo) nao apaga a forma ja gravada.
            var metodo = FormaPagamentoMobile.Normalizar(dto.Metodo);
            if (metodo is not null) existing.Metodo = metodo;
            _db.RemoveRange(existing.Items);
            foreach (var i in dto.Items)
                existing.Items.Add(new OrderItem
                {
                    OrderId = dto.Id, ProductId = i.ProductId, Name = i.Name,
                    Emoji = i.Emoji, Unit = i.Unit, Qty = i.Qty, UnitPrice = i.UnitPrice
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
    /// Leva ao ERP a baixa (ou a devolução) do pedido quando o produto está linkado: espelha em
    /// itens_estoque + movimentacoes_estoque. Falha NÃO interrompe sync.
    /// #1520 (ADR-0060): não mexe em <c>mobile_products.Stock</c>. O aparelho já contou esse
    /// movimento e o manda em "stock.delta" (ou no saldo absoluto, se for aparelho antigo);
    /// descontar aqui de novo deixava o servidor uma venda abaixo do aparelho.
    /// </summary>
    /// <summary>Status do Order mobile em que o estoque já foi descontado (espelha StatusPedido.ComEstoqueDescontado).</summary>
    public static bool StatusDescontaEstoque(string status)
        => status is "pronto" or "saiu_para_entrega" or "entregue";

    private async Task ApplyStockRule(string oldStatus, string newStatus, List<OrderItemDto> items,
        Guid? empresaId, string? orderId = null)
    {
        if (!StatusDescontaEstoque(oldStatus) && StatusDescontaEstoque(newStatus))
        {
            foreach (var i in items)
            {
                var p = await _db.Set<Product>()
                    .FirstOrDefaultAsync(x => x.Id == i.ProductId && x.EmpresaId == empresaId);
                if (p == null) continue;
                await _stockReconciler.ApplyDeltaAsync(
                    p, -i.Qty, NaturezaMovimentacaoEstoque.Venda,
                    descricao: $"Pedido mobile {orderId ?? p.Id} -> {newStatus}",
                    referenciaDocumento: orderId);
            }
        }
        if (StatusDescontaEstoque(oldStatus) && newStatus == "cancelado")
        {
            foreach (var i in items)
            {
                var p = await _db.Set<Product>()
                    .FirstOrDefaultAsync(x => x.Id == i.ProductId && x.EmpresaId == empresaId);
                if (p == null) continue;
                await _stockReconciler.ApplyDeltaAsync(
                    p, +i.Qty, NaturezaMovimentacaoEstoque.Estorno,
                    descricao: $"Cancelamento de pedido mobile {orderId ?? p.Id}",
                    referenciaDocumento: orderId);
            }
        }
    }

    private async Task ApplyBatch(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = CaberNasColunas(m.Payload.Deserialize<BatchDto>(SyncDtoConverters.JsonOpts)!);
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<Batch>().Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.Id == dto.Id && b.EmpresaId == empresaId);
        if (existing != null)
        {
            // Itens sao imutaveis; o re-envio so traz as marcas de exclusao/descarte (#1464).
            var marcasAntes = (existing.DeletedAt, existing.DeletedBy, existing.DiscardedAt, existing.DiscardedBy, existing.DiscardReason);
            AplicarMarcas(existing, dto);
            // #1520: a marca agora desce no pull (carimbo do servidor). Quem marcou passa a ser o
            // autor, para nao receber de volta o lote que acabou de alterar.
            if (marcasAntes != (existing.DeletedAt, existing.DeletedBy, existing.DiscardedAt, existing.DiscardedBy, existing.DiscardReason))
            {
                existing.LastDeviceId = deviceId;
                existing.LastOperatorName = operatorName;
            }
            await _loteEstado.AplicarAsync(existing);
            return;
        }

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
            Id = dto.Id, Code = dto.Code, BatchPhoto = dto.BatchPhoto,
            CreatedAt = createdAt,
            Lote = dto.Lote,
            LastDeviceId = deviceId,
            LastOperatorName = operatorName,
            EmpresaId = empresaId,
            LojaId = lojaId
        };
        AplicarMarcas(batch, dto);
        if (dto.Items is null)
            throw new InvalidOperationException(
                $"Batch {dto.Id} chegou sem coleção Items — payload mal-formado do PWA. " +
                "Cliente deveria enviar Items=[] em vez de null; rejeita p/ nao corromper estoque.");

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
            // #1458: a entrada no ERP e do BatchLinker (um ItemEstoque por lote, com validade).
            // Reconciliar aqui tambem somava o mesmo lote duas vezes no estoque.
            // #1520: o espelho mobile_products.Stock tambem nao e somado aqui; o aparelho ja
            // somou o lote e manda esse movimento em "stock.delta" (ADR-0060).
        }
        _db.Add(batch);
    }

    private static void AplicarMarcas(Batch b, BatchDto dto)
    {
        b.DeletedAt = dto.Deleted == true ? MsParaUtc(dto.DeletedAt) ?? b.DeletedAt ?? DateTime.UtcNow : null;
        b.DeletedBy = dto.Deleted == true ? Cortar(dto.DeletedBy, 64) : null;
        b.DiscardedAt = dto.Discarded == true ? MsParaUtc(dto.DiscardedAt) ?? b.DiscardedAt ?? DateTime.UtcNow : null;
        b.DiscardedBy = dto.Discarded == true ? Cortar(dto.DiscardedBy, 64) : null;
        // Motivo + observacao livre do operador: corta para caber na coluna em vez de rejeitar o sync.
        b.DiscardReason = dto.Discarded == true ? Cortar(dto.DiscardReason, 200) : null;
    }

    private static string? Cortar(string? s, int max)
    {
        if (s is null || s.Length <= max) return s;
        // Nao parte emoji (par substituto) no meio: o Npgsql recusa UTF-16 invalido.
        var fim = char.IsHighSurrogate(s[max - 1]) ? max - 1 : max;
        return s[..fim];
    }

    // #1509: corta os textos livres no tamanho da coluna (mobile_* VARCHAR, ver entidades em
    // Domain/Entities/Mobile). Um texto longo rejeitado pelo banco derrubava o lote inteiro.
    private static ProductDto CaberNasColunas(ProductDto d) => d with
    {
        Name = Cortar(d.Name, 120)!, Emoji = Cortar(d.Emoji, 16), Category = Cortar(d.Category, 16)!,
        Unit = Cortar(d.Unit, 32), Sku = Cortar(d.Sku, 32)
    };

    private static ClientDto CaberNasColunas(ClientDto d) => d with
    {
        Name = Cortar(d.Name, 120)!, Apt = Cortar(d.Apt, 32), Address = Cortar(d.Address, 255),
        Phone = Cortar(d.Phone, 32)
    };

    private static OrderDto CaberNasColunas(OrderDto d) => d with
    {
        ClientSnapshot = d.ClientSnapshot is { } cs
            ? cs with { Name = Cortar(cs.Name, 120)!, Ref = Cortar(cs.Ref, 255) }
            : d.ClientSnapshot!,
        Items = d.Items?.Select(i => i with { Name = Cortar(i.Name, 120)!, Emoji = Cortar(i.Emoji, 16), Unit = Cortar(i.Unit, 32) }).ToList()!,
        ConfirmedBy = Cortar(d.ConfirmedBy, 64)
    };

    private static BatchDto CaberNasColunas(BatchDto d) => d with
    {
        Code = Cortar(d.Code, 32)!, Lote = Cortar(d.Lote, 32),
        Items = d.Items?.Select(i => i with { Name = Cortar(i.Name, 120)!, Emoji = Cortar(i.Emoji, 16), Unit = Cortar(i.Unit, 32) }).ToList()!
    };

    private static CashEntryDto CaberNasColunas(CashEntryDto d) => d with
    {
        Description = Cortar(d.Description, 255)!
    };

    private static long CarimboMs(DateTime carimbo) =>
        new DateTimeOffset(DateTime.SpecifyKind(carimbo, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private static DateTime? MsParaUtc(long? ms) =>
        ms.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime : null;

    private async Task ApplyCashEntry(MutationDto m, string deviceId, string? operatorName,
        Guid? empresaId, Guid? lojaId)
    {
        var dto = CaberNasColunas(m.Payload.Deserialize<CashEntryDto>(SyncDtoConverters.JsonOpts)!);
        // Auditoria 2026-04-30 (CRITICAL fix tenant): filtra por empresa.
        var existing = await _db.Set<CashEntry>()
            .FirstOrDefaultAsync(c => c.Id == dto.Id && c.EmpresaId == empresaId);
        if (existing != null)
        {
            await EditarLancamentoAsync(existing, dto, deviceId, operatorName);
            return;
        }

        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(dto.CreatedAt).UtcDateTime;
        _db.Add(new CashEntry
        {
            Id = dto.Id, Type = dto.Type, Amount = dto.Amount,
            Description = dto.Description, CreatedAt = createdAt,
            Metodo = FormaPagamentoMobile.Normalizar(dto.Metodo),
            LastDeviceId = deviceId,
            LastOperatorName = operatorName,
            EmpresaId = empresaId,
            LojaId = lojaId
        });
    }

    /// <summary>
    /// #1520 (ADR-0060) — edição de lançamento feita no PWA: atualiza tipo, valor, descrição e
    /// forma no lançamento e no <see cref="MovimentoCaixa"/> vinculado. Reenvio sem mudança não
    /// faz nada; lançamento excluído não volta por reenvio de outro aparelho.
    /// </summary>
    private async Task EditarLancamentoAsync(CashEntry existing, CashEntryDto dto, string deviceId, string? operatorName)
    {
        if (existing.DeletedAt != null) return;
        // #1493 — reenvio sem forma (aparelho antigo) nao apaga a forma ja gravada.
        var metodo = FormaPagamentoMobile.Normalizar(dto.Metodo) ?? existing.Metodo;
        if (existing.Type == dto.Type && existing.Amount == dto.Amount
            && existing.Description == dto.Description && existing.Metodo == metodo)
            return;

        await RecusarSeCaixaFechadoAsync(existing, "a alteração");
        existing.Type = dto.Type;
        existing.Amount = dto.Amount;
        existing.Description = dto.Description;
        existing.Metodo = metodo;
        existing.LastDeviceId = deviceId;
        existing.LastOperatorName = operatorName;

        // Movimento ja estornado no ERP fica como estava: e a trilha do estorno.
        var movimento = await MovimentoVinculadoAsync(existing);
        if (movimento is { EstornadoEm: null }) CashEntryLinker.Espelhar(existing, movimento);
    }

    /// <summary>
    /// #1520 (ADR-0060) — exclusão de lançamento, sempre explícita ("cashEntry.delete", nascida
    /// da ação do operador). O PWA limpa lançamentos antigos sozinho, então ausência no aparelho
    /// nunca é exclusão. A linha fica marcada e o movimento vinculado é estornado pelo caso de
    /// uso do ERP. Reenvio e lançamento que o servidor não conhece: aceita sem efeito.
    /// </summary>
    private async Task ApplyCashEntryDelete(MutationDto m, string deviceId, string? operatorName, Guid? empresaId)
    {
        var id = m.Payload.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("Exclusão de lançamento sem id.");
        var existing = await _db.Set<CashEntry>()
            .FirstOrDefaultAsync(c => c.Id == id && c.EmpresaId == empresaId);
        if (existing == null || existing.DeletedAt != null) return;

        await RecusarSeCaixaFechadoAsync(existing, "a exclusão");
        var movimento = await MovimentoVinculadoAsync(existing);
        if (movimento != null)
            await _estornarMovimentoCaixa.ExecuteAsync(new EstornarMovimentoCaixaCommand(
                movimento.EmpresaId, movimento.Id,
                Motivo: "Lançamento excluído no PWA", UsuarioNome: operatorName));

        existing.DeletedAt = DateTime.UtcNow;
        existing.DeletedBy = operatorName;
        // Quem excluiu ja tirou o lancamento da tela; o pull leva a exclusao aos outros aparelhos.
        existing.LastDeviceId = deviceId;
    }

    private async Task<MovimentoCaixa?> MovimentoVinculadoAsync(CashEntry entry)
    {
        // Pela referencia tambem: cobre o movimento ja promovido cujo id nao chegou a ser gravado.
        var referencia = CashEntryLinker.ReferenciaDe(entry);
        return await _db.Set<MovimentoCaixa>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(mv => mv.EmpresaId == entry.EmpresaId
                && (mv.Id == entry.ErpMovimentoCaixaId || mv.Referencia == referencia));
    }

    /// <summary>Mesma regra do estorno no ERP: dia com caixa fechado não aceita mudança.</summary>
    private async Task RecusarSeCaixaFechadoAsync(CashEntry entry, string oQue)
    {
        if (entry.EmpresaId is not { } empresaId) return;
        var dia = HorarioBrasil.DataOperacional(entry.CreatedAt);
        var fechado = await _db.Set<FechamentoCaixa>().IgnoreQueryFilters()
            .AnyAsync(f => f.EmpresaId == empresaId && f.Data == dia && f.LojaId == entry.LojaId);
        if (fechado)
            throw new UseCaseValidationException(
                $"O caixa de {dia:dd/MM/yyyy} já foi fechado: {oQue} do lançamento '{entry.Description}' não foi aplicada.");
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
