using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
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
    MobileEventBroker eventBroker,
    IPedidoRepository pedidoRepo,
    CriarPedidoUseCase criarPedidoUseCase,
    ILoteRepository loteRepo,
    IConfiguration appConfig,
    ILogger<SyncController> log) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly MobileStockReconciler _stockReconciler = stockReconciler;
    private readonly MobileSaleSyncService _saleSync = saleSync;
    private readonly MobileEventBroker _eventBroker = eventBroker;
    private readonly IPedidoRepository _pedidoRepo = pedidoRepo;
    private readonly CriarPedidoUseCase _criarPedidoUseCase = criarPedidoUseCase;
    private readonly ILoteRepository _loteRepo = loteRepo;
    private readonly IConfiguration _appConfig = appConfig;
    private readonly ILogger<SyncController> _log = log;

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
        // F0 — IDs de Product/Client aplicados com sucesso, candidatos a
        // auto-link com Produto/Cliente web depois do SaveChanges principal.
        // F1 — idem para Order → Pedido (promove o mobile_order a Pedido ERP).
        var autoLinkProductIds = new HashSet<string>(StringComparer.Ordinal);
        var autoLinkClientIds  = new HashSet<string>(StringComparer.Ordinal);
        var autoLinkOrderIds   = new HashSet<string>(StringComparer.Ordinal);
        var autoLinkBatchIds   = new HashSet<string>(StringComparer.Ordinal);
        var autoLinkCashIds    = new HashSet<string>(StringComparer.Ordinal);

        foreach (var m in req.Mutations)
        {
            try
            {
                await ApplyMutation(m, req.DeviceId, req.OperatorName, empresaId, lojaId);
                accepted.Add(m.Id);
                // Coleta IDs pra auto-link (so se mutation foi aceita).
                var typePrefix = m.Type?.Split('.')[0];
                if (typePrefix == "product" || typePrefix == "client" || typePrefix == "order"
                    || typePrefix == "batch" || typePrefix == "cashEntry")
                {
                    try
                    {
                        if (m.Payload.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
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
                // Onda 5 — conflict explícito. Reason começa com "conflict:" pra
                // PWA detectar e mostrar UX especializada (toast + force-pull).
                // C3: cex.WinningPayload (opcional) traz a versao server vencedora
                // pra cliente exibir diff visual ao operador.
                rejected.Add(new SyncConflict(m.Id, "conflict: " + cex.Message, cex.WinningPayload));
            }
            catch (Exception ex)
            {
                rejected.Add(new SyncConflict(m.Id, ex.Message));
            }
        }

        await _db.SaveChangesAsync();

        // F0/F1 — auto-link Product/Client/Order ↔ Produto/Cliente/Pedido web.
        // Roda DEPOIS do SaveChanges principal pra (a) ler entities tracked,
        // (b) falha aqui nao bloqueia o sync, (c) idempotente. Feature flags:
        // MobileSync:AutoLink:Product/Client/Order (default true).
        if (autoLinkProductIds.Count > 0 || autoLinkClientIds.Count > 0
            || autoLinkOrderIds.Count > 0 || autoLinkBatchIds.Count > 0
            || autoLinkCashIds.Count  > 0)
        {
            try
            {
                var autoLinkProd   = _appConfig.GetValue<bool>("MobileSync:AutoLink:Product", true);
                var autoLinkClient = _appConfig.GetValue<bool>("MobileSync:AutoLink:Client", true);
                var autoLinkOrder  = _appConfig.GetValue<bool>("MobileSync:AutoLink:Order", true);
                var autoLinkBatch  = _appConfig.GetValue<bool>("MobileSync:AutoLink:Batch", true);
                var autoLinkCash   = _appConfig.GetValue<bool>("MobileSync:AutoLink:CashEntry", true);
                if (autoLinkProd   && autoLinkProductIds.Count > 0) await TryAutoLinkProductsAsync(autoLinkProductIds, empresaId);
                if (autoLinkClient && autoLinkClientIds.Count  > 0) await TryAutoLinkClientsAsync(autoLinkClientIds, empresaId);
                if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
                // F1/F2/F3 — promove orders, batches e cash entries DEPOIS de products/clients
                // pra que FKs (ErpProductId, ErpClienteId) ja estejam preenchidas.
                if (autoLinkOrder  && autoLinkOrderIds.Count   > 0) await TryAutoLinkOrdersAsync(autoLinkOrderIds, empresaId);
                if (autoLinkBatch  && autoLinkBatchIds.Count   > 0) await TryAutoLinkBatchesAsync(autoLinkBatchIds, empresaId);
                if (autoLinkCash   && autoLinkCashIds.Count    > 0) await TryAutoLinkCashEntriesAsync(autoLinkCashIds, empresaId);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha auto-link mobile→ERP apos sync");
            }
        }

        // Onda 5: notifica outros devices da mesma loja em realtime.
        // Fail-safe: se hub indisponível, devices pegam no polling 30s normal.
        if (accepted.Count > 0)
        {
            await _eventBroker.NotifyMutationsAppliedAsync(empresaId, lojaId, req.DeviceId, accepted.Count);
        }

        return Ok(new SyncPushResponse(accepted, rejected.Count > 0 ? rejected : null));
    }

    /// <summary>
    /// F5 — Backfill auto-link mobile→ERP de entidades pre-existentes.
    /// Itera todos os Product/Client/Order/Batch/CashEntry da empresa do device
    /// pareado que ainda não têm ErpId preenchido e roda o pipeline de auto-link.
    /// Idempotente: pode chamar quantas vezes quiser. Disparado manualmente
    /// (PowerShell ou UI futura), não em todo sync.
    /// </summary>
    [HttpPost("backfill-erp-link")]
    [MobileApiKey]
    public async Task<IActionResult> BackfillErpLink(CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "device não pareado" });
        var empresaId = (Guid?)device.EmpresaId;

        var productIds = await _db.Set<Product>().IgnoreQueryFilters()
            .Where(p => p.EmpresaId == empresaId && p.ErpProductId == null)
            .Select(p => p.Id).ToListAsync(ct);
        var clientIds = await _db.Set<Client>().IgnoreQueryFilters()
            .Where(c => c.EmpresaId == empresaId && c.ErpClienteId == null)
            .Select(c => c.Id).ToListAsync(ct);
        // F8 backfill: processa TODOS os orders (mesmo ja promovidos) — pedido
        // ja com Pedido linked mas sem PedidoPagamento ainda precisa que
        // EnsurePagamentoEntregueAsync rode pra criar pagamento + MovimentoCaixa.
        // TryAutoLinkOrdersAsync detecta idempotencia internamente.
        var orderIds = await _db.Set<Order>().IgnoreQueryFilters()
            .Where(o => o.EmpresaId == empresaId)
            .Select(o => o.Id).ToListAsync(ct);
        var batchIds = await _db.Set<Batch>().IgnoreQueryFilters()
            .Where(b => b.EmpresaId == empresaId && b.ErpLoteId == null)
            .Select(b => b.Id).ToListAsync(ct);
        var cashIds = await _db.Set<CashEntry>().IgnoreQueryFilters()
            .Where(c => c.EmpresaId == empresaId && c.ErpMovimentoCaixaId == null)
            .Select(c => c.Id).ToListAsync(ct);

        _log.LogInformation("Backfill empresa={EmpresaId}: products={P} clients={C} orders={O} batches={B} cash={CE}",
            empresaId, productIds.Count, clientIds.Count, orderIds.Count, batchIds.Count, cashIds.Count);

        await TryAutoLinkProductsAsync(productIds, empresaId);
        await TryAutoLinkClientsAsync(clientIds, empresaId);
        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
        await TryAutoLinkOrdersAsync(orderIds, empresaId);
        await TryAutoLinkBatchesAsync(batchIds, empresaId);
        await TryAutoLinkCashEntriesAsync(cashIds, empresaId);

        return Ok(new
        {
            empresaId,
            processed = new
            {
                products = productIds.Count,
                clients = clientIds.Count,
                orders = orderIds.Count,
                batches = batchIds.Count,
                cashEntries = cashIds.Count
            }
        });
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

        // F6 — sync reverso web→mobile. Retorna entidades web que (a) NAO foram
        // originadas no mobile (sem MobileOrderId/MobileBatchId, sem mobile_*
        // com Erp*Id equivalente, ou Referencia sem prefixo 'mobile:'), e (b)
        // foram alteradas depois do `since`. APK aplica como upserts no estado
        // local + reenfileira no proximo push (idempotencia em ApplyOrder/etc
        // detecta Pedido com Id=mobile.Id pra evitar duplicar).
        if (empresaId.HasValue && _appConfig.GetValue<bool>("MobileSync:PullReverse:Enabled", true))
        {
            try
            {
                await AppendWebReversePullAsync(mutations, sinceDate, empresaId.Value, lojaId);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Falha pull reverso web→mobile");
            }
        }

        return Ok(new SyncPullResponse(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), mutations));
    }

    /// <summary>
    /// F6 — Acrescenta na resposta do pull as entidades criadas/editadas no web
    /// (Pedido/Produto/Cliente/Lote/MovimentoCaixa) que ainda nao tem espelho
    /// mobile. Reusa IgnoreQueryFilters porque endpoint anonimo (sem JWT).
    /// </summary>
    private async Task AppendWebReversePullAsync(
        List<MutationDto> mutations, DateTime sinceDate, Guid empresaId, Guid? lojaId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Produtos web sem mobile_product correspondente (Erp link).
        var mobileLinkedProdutos = await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.ErpProductId != null)
            .Select(p => p.ErpProductId!.Value).ToListAsync();
        var produtosQ = _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && p.AlteradoEm > sinceDate
                && p.Status == StatusProduto.Ativo
                && !mobileLinkedProdutos.Contains(p.Id));
        var produtos = await produtosQ.ToListAsync();
        foreach (var p in produtos)
        {
            var dto = new ProductDto(
                Id: p.Id.ToString(),
                Name: p.Nome,
                Emoji: null,
                Category: "Geral",
                Unit: null,
                Price: p.PrecoReferencia?.Valor ?? 0m,
                Stock: 0,
                Custom: false,
                Sku: p.CodigoBarras,
                DefaultWeightG: null,
                DefaultValidityDays: null);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "product.upsert", Serialize(dto), new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // Clientes web sem mobile_client correspondente.
        var mobileLinkedClientes = await _db.Set<Client>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.ErpClienteId != null)
            .Select(c => c.ErpClienteId!.Value).ToListAsync();
        var clientesQ = _db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.AlteradoEm > sinceDate
                && c.Ativo && !mobileLinkedClientes.Contains(c.Id));
        var clientes = await clientesQ.ToListAsync();
        foreach (var c in clientes)
        {
            var dto = new ClientDto(
                Id: c.Id.ToString(),
                Name: c.Nome,
                Apt: c.Apt,
                Address: c.Endereco,
                Phone: c.Telefone,
                LastOrder: c.LastOrderAt.HasValue ? new DateTimeOffset(c.LastOrderAt.Value).ToUnixTimeMilliseconds() : 0,
                OrderCount: c.OrderCount);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "client.upsert", Serialize(dto), new DateTimeOffset(c.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // Pedidos web sem MobileOrderId (criados via /api/pedidos).
        var pedidosQ = _db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
            .Include(p => p.Itens)
            .Where(p => p.EmpresaId == empresaId && p.AlteradoEm > sinceDate
                && (p.MobileOrderId == null || p.MobileOrderId == ""));
        if (lojaId.HasValue) pedidosQ = pedidosQ.Where(p => p.LojaId == lojaId || p.LojaId == null);
        var pedidos = await pedidosQ.ToListAsync();
        foreach (var p in pedidos)
        {
            var items = p.Itens.Select(i => new OrderItemDto(
                ProductId: i.ProdutoId?.ToString() ?? "",
                Name: i.Nome ?? "",
                Emoji: i.Emoji,
                Unit: i.Unidade,
                Qty: (int)i.Quantidade,
                UnitPrice: i.PrecoUnitario
            )).ToList();
            var dto = new OrderDto(
                Id: p.Id.ToString(),
                ClientId: p.ClienteId?.ToString(),
                ClientSnapshot: new ClientSnapshotDto(p.ClienteNome ?? "", null),
                Items: items,
                Notes: p.Observacoes,
                Total: p.Total.Valor,
                Status: p.Status ?? "aguardando",
                CreatedAt: new DateTimeOffset(p.CriadoEm).ToUnixTimeMilliseconds(),
                UpdatedAt: new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds(),
                ScheduledDeliveryAt: p.AgendadoParaEm.HasValue
                    ? new DateTimeOffset(p.AgendadoParaEm.Value).ToUnixTimeMilliseconds()
                    : null);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "order.upsert", Serialize(dto), new DateTimeOffset(p.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // Lotes web sem MobileBatchId (criados direto no admin/web).
        var lotesQ = _db.Set<Lote>().IgnoreQueryFilters().AsNoTracking()
            .Include(l => l.Itens)
            .Where(l => l.EmpresaId == empresaId && l.AlteradoEm > sinceDate
                && (l.MobileBatchId == null || l.MobileBatchId == ""));
        if (lojaId.HasValue) lotesQ = lotesQ.Where(l => l.LojaId == lojaId || l.LojaId == null);
        var lotes = await lotesQ.ToListAsync();
        foreach (var l in lotes)
        {
            var items = l.Itens.Select(it => new BatchItemDto(
                ProductId: it.ProdutoId?.ToString() ?? "",
                Name: it.Nome,
                Emoji: it.Emoji,
                Unit: it.Unidade,
                Qty: it.Quantidade,
                Photo: it.FotoUrl,
                WeightG: it.PesoG,
                ValidityDays: it.ValidadeDias,
                ExpiresAt: it.ExpiraEm.HasValue ? new DateTimeOffset(it.ExpiraEm.Value).ToUnixTimeMilliseconds() : null
            )).ToList();
            var dto = new BatchDto(
                Id: l.Id.ToString(),
                Code: l.Codigo,
                Items: items,
                BatchPhoto: null,
                CreatedAt: new DateTimeOffset(l.DataProducao).ToUnixTimeMilliseconds(),
                Lote: l.Codigo);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "batch.upsert", Serialize(dto), new DateTimeOffset(l.AlteradoEm).ToUnixTimeMilliseconds()));
        }

        // MovimentoCaixa web sem Referencia="mobile:..." (criados direto no admin).
        // F7-B: incluir tambem estornados — mobile aplica flag Estornado pra
        // refletir o estado real (ate hoje o filtro escondia estornos no APK).
        var movimentosQ = _db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.CriadoEm > sinceDate
                && (m.Referencia == null || !m.Referencia.StartsWith("mobile:")));
        if (lojaId.HasValue) movimentosQ = movimentosQ.Where(m => m.LojaId == lojaId || m.LojaId == null);
        var movimentos = await movimentosQ.ToListAsync();
        foreach (var m in movimentos)
        {
            // Mobile entende so "income"/"expense"; mapeamos pelos tipos web.
            string type = m.Tipo switch
            {
                "entrada" => "income",
                "abertura" => "income",
                "saida" => "expense",
                _ => "expense" // fechamento e outros: tratar como expense (marker zerado)
            };
            var dto = new CashEntryDto(
                Id: m.Id.ToString(),
                Type: type,
                Amount: m.Valor,
                Description: m.Descricao ?? "",
                CreatedAt: new DateTimeOffset(m.DataMovimento).ToUnixTimeMilliseconds(),
                Estornado: m.EstornadoEm.HasValue,
                Metodo: m.Metodo);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "cashEntry.upsert", Serialize(dto), new DateTimeOffset(m.CriadoEm).ToUnixTimeMilliseconds()));
        }

        // F7-C — Fechamentos de caixa do web (alterados depois do `since`).
        var fechamentosQ = _db.Set<FechamentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.EmpresaId == empresaId && f.FechadoEm > sinceDate);
        if (lojaId.HasValue) fechamentosQ = fechamentosQ.Where(f => f.LojaId == lojaId || f.LojaId == null);
        var fechamentos = await fechamentosQ.ToListAsync();
        foreach (var f in fechamentos)
        {
            var dto = new CashClosingDto(
                Id: f.Id.ToString(),
                DateKey: f.Data.ToString("yyyy-MM-dd"),
                ClosedAt: new DateTimeOffset(f.FechadoEm).ToUnixTimeMilliseconds(),
                ClosedByName: f.FechadoPorNome,
                TotalPagamentosPedidos: f.TotalPagamentosPedidos,
                TotalSaidasExtras: f.TotalSaidasExtras,
                SaldoFinal: f.SaldoFinal,
                Notes: f.Observacoes);
            mutations.Add(new MutationDto(Guid.NewGuid().ToString(), "web",
                "closing.upsert", Serialize(dto), new DateTimeOffset(f.FechadoEm).ToUnixTimeMilliseconds()));
        }
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
            case "closing":   await ApplyClosing(m, deviceId, empresaId, lojaId);                 break;
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
                    $"editada por {existing.LastOperatorName ?? "outro device"}",
                    // C3: payload server vencedor — PWA exibe diff ao operador.
                    Serialize(ToDto(existing)));
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

        // C3: conflict detection (last-write-loser) pra orders. Cenario tipico:
        // device A marca pedido "preparando", device B marca "cancelado" offline;
        // quando B volta online, server ja tem mudanca de A com timestamp maior.
        // Sem deteccao, B sobrescrevia silenciosamente — producao entregava pedido
        // que deveria ser cancelado, ou vice-versa.
        // Tolerancia 2s pra clock skew. So dispara se outro device editou (LastDeviceId).
        if (existing != null && m.Ts > 0)
        {
            var serverTsMs = new DateTimeOffset(existing.UpdatedAt, TimeSpan.Zero).ToUnixTimeMilliseconds();
            if (serverTsMs > m.Ts + 2000 && existing.LastDeviceId != null && existing.LastDeviceId != deviceId)
            {
                throw new ConflictException(
                    $"Pedido editado em {DateTimeOffset.FromUnixTimeMilliseconds(serverTsMs):HH:mm:ss} " +
                    $"por {existing.LastOperatorName ?? "outro device"} — status atual: {existing.Status}",
                    Serialize(ToDto(existing)));
            }
        }

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

            // C4 — Transicao para "pronto" alerta garcom em outros devices via
            // SSE. Best-effort: se broker falhar ou nao houver loja, polling
            // 30s do PWA continua resolvendo. Garcom recebe notification
            // imediata em vez de descobrir no proximo refresh.
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

    /// <summary>
    /// F7-C — aplica fechamento de caixa enviado pelo mobile (cashClosings.upsert).
    /// Cria FechamentoCaixa via factory. Idempotente: (EmpresaId + Data) unique
    /// — segunda chamada do mesmo dia faz update do snapshot.
    /// Caso o servidor receba `closing.upsert` em DTO com formato CashClosingDto,
    /// processa. Se o DTO falhar a deserializar (cliente antigo, formato outro),
    /// silenciosamente ignora — não bloqueia o sync.
    /// </summary>
    private async Task ApplyClosing(MutationDto m, string deviceId, Guid? empresaId, Guid? lojaId)
    {
        if (!empresaId.HasValue) return;
        CashClosingDto? dto;
        try { dto = m.Payload.Deserialize<CashClosingDto>(JsonOpts); }
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
            // Override SaldoFinal e FechadoEm com snapshot do mobile (verdade do operador no momento)
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
            new DateTimeOffset(o.UpdatedAt).ToUnixTimeMilliseconds(),
            // Campos opcionais: sem isto o pull silenciosamente zerava
            // factAt/confirmedAt/scheduledDeliveryAt quando outro device sincronizava.
            History: o.HistoryJson != null
                ? TryParseJson(o.HistoryJson)
                : null,
            ConfirmedBy: o.ConfirmedBy,
            ConfirmedAt: o.ConfirmedAt.HasValue
                ? new DateTimeOffset(o.ConfirmedAt.Value).ToUnixTimeMilliseconds()
                : (long?)null,
            FactAt: o.FactAt.HasValue
                ? new DateTimeOffset(o.FactAt.Value).ToUnixTimeMilliseconds()
                : (long?)null,
            ScheduledDeliveryAt: o.ScheduledDeliveryAt.HasValue
                ? new DateTimeOffset(o.ScheduledDeliveryAt.Value).ToUnixTimeMilliseconds()
                : (long?)null);

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

    private static JsonElement? TryParseJson(string json)
    {
        try { return JsonDocument.Parse(json).RootElement.Clone(); }
        catch { return null; }
    }

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

    // ─── F0: auto-link Product ↔ Produto / Client ↔ Cliente ────────────────
    // Unifica o domínio mobile com o ERP web. Sem isso, pedidos mobile não
    // viram Pedido web (contabilidade quebrada). Cada Product mobile vira
    // (ou liga a) um Produto web, preservando ErpProductId no mobile pra
    // referencia futura. Falha aqui NÃO bloqueia sync (tratada no caller).
    //
    // Estratégia de match:
    //   - Produto: nome ILIKE + empresa
    //   - Cliente: nome ILIKE + empresa; fallback por telefone
    //   - Se sem match, cria entity web com defaults seguros.

    private async Task TryAutoLinkProductsAsync(IEnumerable<string> mobileProductIds, Guid? empresaId)
    {
        if (!empresaId.HasValue) return;
        Guid? cachedCategoriaId = null;
        foreach (var pid in mobileProductIds)
        {
            try
            {
                var mobileP = await _db.Set<Product>()
                    .FirstOrDefaultAsync(p => p.Id == pid && p.EmpresaId == empresaId);
                if (mobileP == null || mobileP.ErpProductId.HasValue) continue;

                var webP = await _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(p =>
                        p.EmpresaId == empresaId
                        && p.Status == StatusProduto.Ativo
                        && EF.Functions.ILike(p.Nome, mobileP.Name));

                if (webP != null)
                {
                    mobileP.ErpProductId = webP.Id;
                    _log.LogInformation("AutoLink Produto: mobile={MobileId} → erp={ErpId} via nome match", pid, webP.Id);
                    continue;
                }

                // Criar Produto web. Categoria default cacheada por request pra
                // evitar N+1 quando varios produtos novos sobem juntos.
                cachedCategoriaId ??= await GetOrCreateDefaultCategoriaAsync(empresaId.Value);

                var novoProd = new Produto
                {
                    Id = Guid.NewGuid(),
                    EmpresaId = empresaId.Value,
                    CategoriaId = cachedCategoriaId.Value,
                    Nome = mobileP.Name,
                    Tipo = TipoProduto.Alimento, // default Casa da Babá (food); admin pode reclassificar
                    Status = StatusProduto.Ativo,
                    PrecoReferencia = mobileP.Price is { } pr && pr > 0 ? Dinheiro.FromDecimal(pr) : null,
                    CodigoBarras = mobileP.Sku,
                    ControlaValidade = mobileP.DefaultValidityDays.HasValue,
                    CriadoEm = DateTime.UtcNow,
                    AlteradoEm = DateTime.UtcNow
                };
                _db.Add(novoProd);
                mobileP.ErpProductId = novoProd.Id;
                _log.LogInformation("AutoLink Produto CRIADO: mobile={MobileId} → erp={ErpId} ({Nome})",
                    pid, novoProd.Id, mobileP.Name);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AutoLink Produto falhou pra mobile={MobileId}", pid);
            }
        }
    }

    private async Task TryAutoLinkClientsAsync(IEnumerable<string> mobileClientIds, Guid? empresaId)
    {
        if (!empresaId.HasValue) return;
        foreach (var cid in mobileClientIds)
        {
            try
            {
                var mobileC = await _db.Set<Client>()
                    .FirstOrDefaultAsync(c => c.Id == cid && c.EmpresaId == empresaId);
                if (mobileC == null || mobileC.ErpClienteId.HasValue) continue;

                var baseQuery = _db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.EmpresaId == empresaId && c.Ativo);

                Cliente? match = await baseQuery
                    .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Nome, mobileC.Name));

                if (match == null && !string.IsNullOrWhiteSpace(mobileC.Phone))
                {
                    var phone = mobileC.Phone;
                    match = await baseQuery.FirstOrDefaultAsync(c => c.Telefone == phone);
                }

                if (match != null)
                {
                    mobileC.ErpClienteId = match.Id;
                    _log.LogInformation("AutoLink Cliente: mobile={MobileId} → erp={ErpId}", cid, match.Id);
                    continue;
                }

                var novoC = Cliente.Criar(empresaId.Value, mobileC.Name);
                novoC.Apt      = mobileC.Apt;
                novoC.Endereco = mobileC.Address;
                novoC.Telefone = mobileC.Phone;
                novoC.LastOrderAt = mobileC.LastOrder;
                novoC.OrderCount  = mobileC.OrderCount;
                _db.Add(novoC);
                mobileC.ErpClienteId = novoC.Id;
                _log.LogInformation("AutoLink Cliente CRIADO: mobile={MobileId} → erp={ErpId} ({Nome})",
                    cid, novoC.Id, mobileC.Name);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AutoLink Cliente falhou pra mobile={MobileId}", cid);
            }
        }
    }

    // F1 — promove mobile_order → Pedido web via CriarPedidoUseCase. Idempotente
    // via pedidoRepo.FindByMobileOrderIdAsync (skip se ja promovido). Resolve
    // ClienteId via ErpClienteId (preenchido em F0). Itens vao sem ProdutoId FK
    // por enquanto (futuras iteracoes resolvem via ErpProductId tambem).
    //
    // Caso o Pedido falhe a criar (validacao, etc), engole excecao e segue —
    // mobile_order continua existindo e pode ser linkado manualmente via
    // MobileOrdersController.Link depois.
    private async Task TryAutoLinkOrdersAsync(IEnumerable<string> mobileOrderIds, Guid? empresaId)
    {
        if (!empresaId.HasValue) return;
        foreach (var oid in mobileOrderIds)
        {
            try
            {
                var mobileO = await _db.Set<Order>().IgnoreQueryFilters()
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == oid && o.EmpresaId == empresaId);
                if (mobileO == null) continue;

                Guid? pedidoIdResolvido = null;

                if (mobileO.ErpPedidoId.HasValue && mobileO.ErpPedidoId.Value != Guid.Empty)
                {
                    pedidoIdResolvido = mobileO.ErpPedidoId.Value;
                }
                else
                {
                    // Idempotencia cross-request: se ja existe Pedido com este MobileOrderId.
                    var jaPromovido = await _pedidoRepo.FindByMobileOrderIdAsync(empresaId.Value, mobileO.Id);
                    if (jaPromovido != null)
                    {
                        mobileO.ErpPedidoId = jaPromovido.Id;
                        mobileO.UpdatedAt = DateTime.UtcNow;
                        pedidoIdResolvido = jaPromovido.Id;
                        _log.LogInformation("AutoLink Pedido (idempotente MobileOrderId): mobile={MobileId} → erp={ErpId}", oid, jaPromovido.Id);
                    }
                    // F6 idempotencia: pull web→mobile retornou Pedido web com Guid,
                    // APK reenfileirou de volta com mobile.Id=Guid. So linka reverso.
                    else if (Guid.TryParse(mobileO.Id, out var pedidoIdAsGuid))
                    {
                        var pedidoExistente = await _pedidoRepo.GetByIdAsync(empresaId.Value, pedidoIdAsGuid);
                        if (pedidoExistente != null)
                        {
                            mobileO.ErpPedidoId = pedidoExistente.Id;
                            mobileO.UpdatedAt = DateTime.UtcNow;
                            if (string.IsNullOrEmpty(pedidoExistente.MobileOrderId))
                            {
                                pedidoExistente.MobileOrderId = mobileO.Id;
                                await _pedidoRepo.UpdateAsync(pedidoExistente);
                            }
                            pedidoIdResolvido = pedidoExistente.Id;
                            _log.LogInformation("AutoLink Pedido (idempotente Guid eco): mobile={MobileId} ↔ erp={ErpId}",
                                oid, pedidoExistente.Id);
                        }
                    }

                    if (pedidoIdResolvido == null)
                    {
                        // CriarPedidoUseCase usa clienteRepo/produtoRepo que aplicam o
                        // Global Query Filter tenant. Endpoint anonimo (sem JWT) faz
                        // o filter zerar TUDO. Resultado: validacao do use case sempre
                        // dispara "Cliente nao encontrado nesta empresa" / "Produto
                        // do item nao pertence a esta empresa", mesmo que existam.
                        //
                        // Solucao: passa Cliente/Produto sempre como NULL + snapshot
                        // ad-hoc. Pedido nasce sem FK explicita; admin linka depois
                        // via UI (ja existe MobileOrderId pra rastrear origem).
                        // Trade-off aceitavel ate refatorar repos pra suportar contexto
                        // anonimo (provavelmente via overload com IgnoreQueryFilters).
                        Client? mClient = null;
                        if (!string.IsNullOrWhiteSpace(mobileO.ClientId))
                        {
                            mClient = await _db.Set<Client>().IgnoreQueryFilters().AsNoTracking()
                                .FirstOrDefaultAsync(c => c.Id == mobileO.ClientId);
                        }

                        var itens = mobileO.Items.Select(i => new CriarPedidoItemInput(
                            Nome: i.Name,
                            Quantidade: i.Qty,
                            PrecoUnitario: i.UnitPrice,
                            ProdutoId: null, // ad-hoc: use case revalidaria com filter zerado
                            Emoji: i.Emoji,
                            Unidade: i.Unit,
                            Observacao: null
                        )).ToList();

                        var clienteNomeFinal = !string.IsNullOrWhiteSpace(mobileO.ClientSnapshotName)
                            ? mobileO.ClientSnapshotName
                            : (mClient?.Name ?? "Avulso");
                        var result = await _criarPedidoUseCase.ExecuteAsync(new CriarPedidoCommand(
                            EmpresaId: empresaId.Value,
                            LojaId: mobileO.LojaId,
                            ClienteId: null,
                            ClienteNomeAdHoc: clienteNomeFinal,
                            ClienteAptAdHoc: mClient?.Apt,
                            ClienteTelefoneAdHoc: mClient?.Phone,
                            Observacoes: mobileO.Notes,
                            Origem: "mobile",
                            MobileOrderId: mobileO.Id,
                            Itens: itens,
                            CriadoPorUserId: null,
                            CriadoPorNome: mobileO.LastOperatorName,
                            AgendadoParaEm: mobileO.ScheduledDeliveryAt
                        ));

                        // Pos-criacao: linka FKs (Cliente e Produtos) que o use case
                        // nao podia setar por causa do filter tenant. Usamos UPDATE
                        // direto via _db tracker — entities ja existem.
                        try
                        {
                            Guid? clienteFk = null;
                            if (mClient?.ErpClienteId.HasValue == true)
                            {
                                var existeErp = await _db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking()
                                    .AnyAsync(c => c.Id == mClient.ErpClienteId.Value && c.EmpresaId == empresaId);
                                if (existeErp) clienteFk = mClient.ErpClienteId;
                            }
                            if (clienteFk.HasValue)
                            {
                                await _db.Set<Pedido>().IgnoreQueryFilters()
                                    .Where(p => p.Id == result.Id)
                                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.ClienteId, clienteFk));
                            }

                            // Mapa mobile.product.Id -> ErpProductId
                            var productIds = mobileO.Items.Where(i => !string.IsNullOrWhiteSpace(i.ProductId))
                                .Select(i => i.ProductId).Distinct().ToList();
                            var produtoMap = await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                                .Where(p => productIds.Contains(p.Id) && p.EmpresaId == empresaId)
                                .ToDictionaryAsync(p => p.Id, p => p.ErpProductId);

                            // Carrega pedido_itens criados pelo use case e atualiza ProdutoId
                            // por matching de Nome (mesma ordem que mobile envia).
                            var pedidoItens = await _db.Set<PedidoItem>().IgnoreQueryFilters()
                                .Where(pi => pi.PedidoId == result.Id)
                                .OrderBy(pi => pi.Id).ToListAsync();
                            for (int idx = 0; idx < Math.Min(pedidoItens.Count, mobileO.Items.Count); idx++)
                            {
                                var mobItem = mobileO.Items[idx];
                                if (!string.IsNullOrWhiteSpace(mobItem.ProductId)
                                    && produtoMap.TryGetValue(mobItem.ProductId, out var prodFk)
                                    && prodFk.HasValue)
                                {
                                    pedidoItens[idx].ProdutoId = prodFk;
                                }
                            }
                            if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
                        }
                        catch (Exception linkEx)
                        {
                            _log.LogWarning(linkEx, "Pedido {ErpId}: falha ao linkar Cliente/Produto FKs (pedido criado mas ad-hoc). {Msg}",
                                result.Id, linkEx.Message);
                        }

                        mobileO.ErpPedidoId = result.Id;
                        mobileO.UpdatedAt = DateTime.UtcNow;
                        if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
                        pedidoIdResolvido = result.Id;
                        _log.LogInformation("AutoLink Pedido CRIADO: mobile={MobileId} → erp={ErpId} status={Status}",
                            oid, result.Id, mobileO.Status);
                    }
                }

                // F7-A — Pagamento auto. Quando mobileO.Status == "entregue", criar
                // PedidoPagamento default "dinheiro" cobrindo o Total (se ainda
                // nao tiver pagamento registrado). Operador refina metodo no admin.
                // Idempotente: skip se Pedido ja tem qualquer pagamento OU se Total=0.
                if (pedidoIdResolvido.HasValue
                    && string.Equals(mobileO.Status, "entregue", StringComparison.OrdinalIgnoreCase))
                {
                    await EnsurePagamentoEntregueAsync(pedidoIdResolvido.Value, mobileO);
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AutoLink Pedido falhou pra mobile={MobileId}", oid);
            }
        }
    }

    /// <summary>
    /// F7-A: garante 1 PedidoPagamento default ("dinheiro", total) num Pedido
    /// cujo status veio do mobile como "entregue". F8-C: alem disso, cria
    /// MovimentoCaixa de entrada espelhando o pagamento (caixa diario passa
    /// a refletir o recebimento). Idempotente: skip se ja tem pagamento OU
    /// Total = 0; idempotente cross-runs via Referencia="pedido-pagamento:<id>".
    /// </summary>
    private async Task EnsurePagamentoEntregueAsync(Guid pedidoId, Order mobileO)
    {
        // Carrega SEM tracking pra evitar DbUpdateConcurrencyException quando
        // varias mutations do mesmo pedido sao processadas (versao anterior
        // dava .Include(p.Pagamentos) e .Add com tracker — o EF tentava
        // re-salvar campos do Pedido pai e dava concurrency error).
        var pedido = await _db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pedidoId);
        if (pedido == null) return;
        if (pedido.Total.Valor <= 0) return;

        // Idempotencia: ja tem pagamento?
        var temPagamento = await _db.Set<PedidoPagamento>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(pp => pp.PedidoId == pedidoId);
        if (temPagamento) return;

        var pagamentoId = Guid.NewGuid();
        _db.Add(new PedidoPagamento
        {
            Id = pagamentoId,
            PedidoId = pedido.Id,
            Metodo = "dinheiro",
            Valor = pedido.Total.Valor,
            PagoEm = mobileO.UpdatedAt,
            RegistradoPorNome = mobileO.LastOperatorName,
            Observacao = "Auto-registrado pelo F7-A (mobile→ERP). Refine método no admin se necessário."
        });

        // F8-C: espelha o pagamento como MovimentoCaixa de entrada do dia.
        var refKey = "pedido-pagamento:" + pagamentoId;
        var jaExisteMov = await _db.Set<MovimentoCaixa>().IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(m => m.Referencia == refKey);
        if (!jaExisteMov)
        {
            var mov = MovimentoCaixa.Criar(pedido.EmpresaId, "entrada", pedido.Total.Valor,
                dataMovimento: mobileO.UpdatedAt, lojaId: pedido.LojaId);
            mov.Descricao = "Pagamento pedido " + (pedido.Id.ToString().Substring(0, 8)) +
                            (string.IsNullOrEmpty(pedido.ClienteNome) ? "" : " — " + pedido.ClienteNome);
            mov.Metodo = "dinheiro";
            mov.Categoria = "pedido";
            mov.Origem = "mobile-payment";
            mov.Referencia = refKey;
            mov.RegistradoPorNome = mobileO.LastOperatorName;
            _db.Add(mov);
        }

        await _db.SaveChangesAsync();
        _log.LogInformation(
            "F7-A/F8-C Pagamento+MovCaixa CRIADO: pedido={ErpId} valor={Valor} metodo=dinheiro",
            pedido.Id, pedido.Total.Valor);
    }

    // F2 — promove mobile_batch → Lote web. Mobile produz e finaliza ao mesmo
    // tempo (Batch e' insert-only no mobile), entao o Lote ja nasce com
    // Status="finalizado". Itens com ProdutoId resolvido via Product.ErpProductId
    // (F0 preenche). Idempotente via FindByMobileBatchIdAsync.
    private async Task TryAutoLinkBatchesAsync(IEnumerable<string> mobileBatchIds, Guid? empresaId)
    {
        if (!empresaId.HasValue) return;
        foreach (var bid in mobileBatchIds)
        {
            try
            {
                var mobileB = await _db.Set<Batch>().IgnoreQueryFilters()
                    .Include(b => b.Items)
                    .FirstOrDefaultAsync(b => b.Id == bid && b.EmpresaId == empresaId);
                if (mobileB == null) continue;
                if (mobileB.ErpLoteId.HasValue && mobileB.ErpLoteId.Value != Guid.Empty) continue;

                var jaPromovido = await _loteRepo.FindByMobileBatchIdAsync(empresaId.Value, mobileB.Id);
                if (jaPromovido != null)
                {
                    mobileB.ErpLoteId = jaPromovido.Id;
                    _log.LogInformation("AutoLink Lote (idempotente): mobile={MobileId} → erp={ErpId}", bid, jaPromovido.Id);
                    continue;
                }

                // Codigo do Lote: usa o identificador do mobile (LOT-YYMMDD) MAS
                // sempre acrescenta sufixo com final do mobile.Id pra garantir
                // unicidade (IX_lotes_EmpresaId_Codigo). Mobile gera batches
                // diferentes do mesmo dia com mesmo "Lote"=LOT-YYMMDD — sem
                // sufixo daria duplicate key constraint violation no web.
                var codigoBase = !string.IsNullOrWhiteSpace(mobileB.Lote)
                    ? mobileB.Lote!
                    : !string.IsNullOrWhiteSpace(mobileB.Code)
                        ? mobileB.Code!
                        : $"LOT-{mobileB.CreatedAt:yyMMdd}";
                var sufixo = mobileB.Id.Length >= 6
                    ? mobileB.Id.Substring(mobileB.Id.Length - 6)
                    : mobileB.Id;
                // Sanitiza sufixo pra evitar caracteres invalidos em codigo de lote.
                sufixo = new string(sufixo.Where(c => char.IsLetterOrDigit(c)).ToArray());
                var codigo = string.IsNullOrEmpty(sufixo) ? codigoBase : (codigoBase + "-" + sufixo);

                var lote = Lote.Criar(empresaId.Value, codigo, mobileB.CreatedAt, mobileB.LojaId);
                lote.MobileBatchId = mobileB.Id;
                lote.OperadorNome  = mobileB.LastOperatorName;
                lote.Origem        = "mobile";

                foreach (var item in mobileB.Items)
                {
                    // Resolve ProdutoId via mobile.Product.ErpProductId (F0 preenche).
                    Guid? produtoIdResolved = null;
                    if (!string.IsNullOrWhiteSpace(item.ProductId))
                    {
                        var mProd = await _db.Set<Product>().IgnoreQueryFilters().AsNoTracking()
                            .FirstOrDefaultAsync(p => p.Id == item.ProductId);
                        produtoIdResolved = mProd?.ErpProductId;
                    }
                    lote.Itens.Add(new LoteItem
                    {
                        Id = Guid.NewGuid(),
                        LoteId = lote.Id,
                        ProdutoId = produtoIdResolved,
                        Nome = item.Name,
                        Emoji = item.Emoji,
                        Unidade = item.Unit,
                        Quantidade = item.Qty,
                        PesoG = item.WeightG,
                        ValidadeDias = item.ValidityDays,
                        ExpiraEm = item.ExpiresAt,
                        CriadoEm = DateTime.UtcNow
                    });
                }

                // Mobile entrega lote ja finalizado (insert-only). Espelha esse status.
                lote.Finalizar();

                await _loteRepo.AddAsync(lote);
                mobileB.ErpLoteId = lote.Id;
                if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
                _log.LogInformation("F2 Lote CRIADO: mobile={MobileId} → erp={ErpId} itens={N}",
                    bid, lote.Id, lote.Itens.Count);

                // F8-A: produzido = entrada de estoque. Pra cada LoteItem com
                // ProdutoId resolvido (F0 preencheu Product.ErpProductId), criar
                // ItemEstoque + MovimentacaoEstoque (Entrada). Idempotente via
                // CodigoInterno="lote:<loteId>:<produtoId>".
                await EnsureEntradaEstoqueDoLoteAsync(lote);
            }
            catch (Exception ex)
            {
                // F8-E: log explicito com stack pra diagnose ficar acessivel via
                // painel Fly logs (era LogWarning sem detail antes).
                _log.LogError(ex,
                    "F2 AutoLink Lote FALHOU mobile={MobileId}: {Tipo}: {Mensagem}",
                    bid, ex.GetType().Name, ex.Message);
            }
        }
    }

    /// <summary>
    /// F8-A — Cria ItemEstoque + MovimentacaoEstoque (Entrada) para cada
    /// LoteItem com ProdutoId resolvido. Idempotente: CodigoInterno=
    /// "lote:&lt;loteId&gt;:&lt;produtoId&gt;" garante que segunda execucao
    /// detecta duplicacao e skipa.
    ///
    /// Custo unitario: Produto.CustoReferencia ou 0. Preco sugerido:
    /// Produto.PrecoReferencia. Sem variacao (Casa da Baba nao usa).
    /// </summary>
    private async Task EnsureEntradaEstoqueDoLoteAsync(Lote lote)
    {
        foreach (var item in lote.Itens.Where(i => i.ProdutoId.HasValue && i.Quantidade > 0))
        {
            try
            {
                var codigoInterno = $"lote:{lote.Id}:{item.ProdutoId}";

                var jaTem = await _db.Set<ItemEstoque>().IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(ie => ie.EmpresaId == lote.EmpresaId && ie.CodigoInterno == codigoInterno);
                if (jaTem)
                {
                    _log.LogInformation("F8-A skip (idempotente): lote={LoteId} produto={ProdutoId} codigoInterno={Codigo}",
                        lote.Id, item.ProdutoId, codigoInterno);
                    continue;
                }

                var produto = await _db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == item.ProdutoId);
                if (produto == null)
                {
                    _log.LogWarning("F8-A: produto {ProdutoId} nao existe; pulando entrada do lote {LoteId}",
                        item.ProdutoId, lote.Id);
                    continue;
                }

                var custoUnit = produto.CustoReferencia ?? Dinheiro.Zero;
                var precoVenda = produto.PrecoReferencia;
                Validade? validade = item.ExpiraEm.HasValue ? Validade.From(item.ExpiraEm.Value) : null;
                var qtd = Quantidade.From(item.Quantidade);

                var itemEstoque = ItemEstoque.CriarParaEntrada(
                    id: Guid.NewGuid(),
                    empresaId: lote.EmpresaId,
                    produto: produto,
                    variacao: null,
                    quantidade: qtd,
                    custoUnitario: custoUnit,
                    precoVendaSugerido: precoVenda,
                    dataEntrada: lote.DataProducao,
                    codigoInterno: codigoInterno,
                    codigoLote: null,
                    codigoMarketplace: null,
                    variacaoDescricao: null,
                    cor: null,
                    tamanho: null,
                    descricaoAnuncio: null,
                    dimensoesReais: null,
                    fornecedorNome: null,
                    validade: validade,
                    observacoes: $"Auto-gerado pelo F8-A a partir do Lote {lote.Codigo}",
                    criadoEm: DateTime.UtcNow);
                if (lote.LojaId.HasValue) itemEstoque.LojaId = lote.LojaId;

                _db.Add(itemEstoque);

                var movimentacao = MovimentacaoEstoque.CriarEntrada(
                    id: Guid.NewGuid(),
                    empresaId: lote.EmpresaId,
                    item: itemEstoque,
                    natureza: NaturezaMovimentacaoEstoque.Producao,
                    quantidade: qtd,
                    valorUnitario: custoUnit,
                    dataMovimentacao: lote.DataProducao,
                    descricao: $"Producao lote {lote.Codigo}",
                    documentoReferencia: $"lote:{lote.Id}",
                    criadoEm: DateTime.UtcNow);
                _db.Add(movimentacao);

                await _db.SaveChangesAsync();
                _log.LogInformation(
                    "F8-A ItemEstoque + Movimentacao CRIADOS: lote={LoteId} produto={ProdutoId} qtd={Qtd}",
                    lote.Id, item.ProdutoId, item.Quantidade);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "F8-A FALHOU pra lote={LoteId} produto={ProdutoId}: {Tipo}: {Mensagem}",
                    lote.Id, item.ProdutoId, ex.GetType().Name, ex.Message);
            }
        }
    }

    // F3 — promove mobile_cash_entry → MovimentoCaixa web. Idempotente via
    // campo Referencia="mobile:<id>" (MovimentoCaixa.Referencia foi feito pra
    // identificador externo). Mobile so manda "income"/"expense" — mapeamos
    // pra "entrada"/"saida". Metodo default "dinheiro" (operador pode editar
    // no admin depois).
    private async Task TryAutoLinkCashEntriesAsync(IEnumerable<string> mobileCashIds, Guid? empresaId)
    {
        if (!empresaId.HasValue) return;
        foreach (var ceid in mobileCashIds)
        {
            try
            {
                var mobileCE = await _db.Set<CashEntry>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.Id == ceid && c.EmpresaId == empresaId);
                if (mobileCE == null) continue;
                if (mobileCE.ErpMovimentoCaixaId.HasValue && mobileCE.ErpMovimentoCaixaId.Value != Guid.Empty) continue;

                var referencia = $"mobile:{mobileCE.Id}";
                var jaPromovido = await _db.Set<MovimentoCaixa>().IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.EmpresaId == empresaId && m.Referencia == referencia);
                if (jaPromovido != null)
                {
                    mobileCE.ErpMovimentoCaixaId = jaPromovido.Id;
                    _log.LogInformation("AutoLink MovimentoCaixa (idempotente): mobile={MobileId} → erp={ErpId}", ceid, jaPromovido.Id);
                    continue;
                }

                var tipo = string.Equals(mobileCE.Type, "income", StringComparison.OrdinalIgnoreCase) ? "entrada" : "saida";
                var mov = MovimentoCaixa.Criar(empresaId.Value, tipo, mobileCE.Amount, mobileCE.CreatedAt, mobileCE.LojaId);
                mov.Descricao = mobileCE.Description;
                mov.Metodo = "dinheiro"; // default; operador refina no admin
                mov.Origem = "mobile";
                mov.RegistradoPorNome = mobileCE.LastOperatorName;
                mov.Referencia = referencia;

                _db.Add(mov);
                mobileCE.ErpMovimentoCaixaId = mov.Id;
                if (_db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync();
                _log.LogInformation("AutoLink MovimentoCaixa CRIADO: mobile={MobileId} → erp={ErpId} tipo={Tipo} valor={Valor}",
                    ceid, mov.Id, tipo, mov.Valor);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "AutoLink MovimentoCaixa falhou pra mobile={MobileId}", ceid);
            }
        }
    }

    private async Task<Guid> GetOrCreateDefaultCategoriaAsync(Guid empresaId)
    {
        // Reusa categoria existente preferindo nomes neutros (Mobile/Geral).
        // Sem ordering por bool no SQL: fazer client-side é OK porque o N é
        // pequeno (raramente uma empresa tem > 100 categorias).
        var existentes = await _db.Set<Categoria>().IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .Select(c => new { c.Id, c.Nome, c.CriadoEm })
            .ToListAsync();
        if (existentes.Count > 0)
        {
            var preferida = existentes
                .OrderBy(c => c.Nome == "Mobile" ? 0 : (c.Nome == "Geral" ? 1 : 2))
                .ThenBy(c => c.CriadoEm)
                .First();
            return preferida.Id;
        }

        // Empresa sem nenhuma categoria — cria "Mobile" como default.
        var nova = new Categoria
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Nome = "Mobile",
            Descricao = "Categoria default criada pelo auto-link mobile→ERP",
            CriadoEm = DateTime.UtcNow,
            AlteradoEm = DateTime.UtcNow
        };
        _db.Add(nova);
        _log.LogInformation("AutoLink: criada Categoria 'Mobile' default empresa={EmpresaId}", empresaId);
        return nova.Id;
    }
}

/// <summary>
/// Lançada quando uma mutation chega com timestamp anterior à versão
/// do servidor — sinaliza last-write-loser que o cliente precisa tratar.
/// C3: <see cref="WinningPayload"/> traz a versao server vencedora (DTO serializado)
/// pra que o PWA possa exibir diff visual ao operador antes de sobrescrever.
/// Opcional — nem todo conflict tem payload (ex: validacoes de tenant).
/// </summary>
public class ConflictException(string message, System.Text.Json.JsonElement? winningPayload = null) : Exception(message)
{
    public System.Text.Json.JsonElement? WinningPayload { get; } = winningPayload;
}
