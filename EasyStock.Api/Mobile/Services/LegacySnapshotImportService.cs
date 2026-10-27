using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using MobileOrder = EasyStock.Domain.Entities.Mobile.Order;

namespace EasyStock.Api.Mobile.Services;

public sealed class LegacyImportException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>Primeira carga declarada pelo operador. Snapshot e recibo sao atomicos; nao reproduz eventos antigos.</summary>
public sealed class LegacySnapshotImportService(EasyStockDbContext db, SyncMutationDispatcher dispatcher,
    ProductLinker products, ClientLinker clients)
{
    public const string HistoryOrigin = "mobile-import";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Collections = ["products", "clients", "orders", "batches", "cashEntries", "cashClosings"];
    private static string ReceiptKey(Guid id) => "legacy:" + id.ToString("N");
    private static LegacyImportException Invalid(string code, string message) => new(400, code, message);
    private static LegacyImportException Conflict(string message) => new(409, "LEGACY_RECONCILIATION_REQUIRED", message);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<LegacySnapshotResponse> ImportAsync(LegacySnapshotRequest request, MobileDevice device, CancellationToken ct)
    {
        if (!request.SourceNeverSynced) throw Invalid("LEGACY_CONFIRMATION_REQUIRED", "Confirme que estes dados nunca foram enviados ao EasyStok.");
        if (device.EmpresaId != request.ExpectedEmpresaId || device.LojaId != request.ExpectedLojaId)
            throw Invalid("LEGACY_CONTEXT_MISMATCH", "A empresa ou loja confirmada difere do pareamento atual.");
        if (request.ImportId == Guid.Empty || string.IsNullOrWhiteSpace(request.SnapshotJson))
            throw Invalid("LEGACY_SNAPSHOT_INVALID", "ImportId e snapshot sao obrigatorios.");
        if (Encoding.UTF8.GetByteCount(request.SnapshotJson) > 12 * 1024 * 1024)
            throw Invalid("LEGACY_SNAPSHOT_TOO_LARGE", "Snapshot acima de 12 MiB. Preserve o arquivo e solicite migracao assistida.");
        if (request.OperatorName?.Length > 64) throw Invalid("LEGACY_OPERATOR_INVALID", "Nome do operador acima de 64 caracteres.");
        var hash = Hash(request.SnapshotJson);
        if (!string.Equals(hash, request.SnapshotSha256, StringComparison.OrdinalIgnoreCase))
            throw Invalid("LEGACY_HASH_MISMATCH", "O arquivo mudou depois da confirmacao. Nenhum registro foi importado.");
        var originIds = request.OriginMutationIds;
        if (originIds is null || originIds.Count > 50000 || originIds.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 60 || x.StartsWith("legacy:", StringComparison.Ordinal))
            || originIds.Distinct(StringComparer.Ordinal).Count() != originIds.Count || originIds.Contains(ReceiptKey(request.ImportId)))
            throw Invalid("LEGACY_QUEUE_INVALID", "Identificadores da fila de origem invalidos.");
        originIds = originIds.Order(StringComparer.Ordinal).ToList();
        using var document = JsonDocument.Parse(request.SnapshotJson);
        var state = document.RootElement;
        RequireUnambiguousProperties(state);
        if (state.ValueKind != JsonValueKind.Object) throw Invalid("LEGACY_SNAPSHOT_INVALID", "O snapshot precisa conter as colecoes do aplicativo.");
        var rows = new Dictionary<string, List<JsonElement>>();
        foreach (var name in Collections)
        {
            if (!state.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 50000)
                throw Invalid("LEGACY_COLLECTION_INVALID", "Colecao ausente ou invalida: " + name);
            rows[name] = array.EnumerateArray().Select(x => x.Clone()).ToList();
            var ids = rows[name].Select(Id).ToList();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count) throw Invalid("LEGACY_DUPLICATE_ID", "IDs repetidos na colecao " + name);
        }
        var counts = rows.ToDictionary(x => x.Key, x => x.Value.Count);
        if (counts.Values.Sum() == 0) throw Invalid("LEGACY_EMPTY_SNAPSHOT", "O arquivo nao contem registros para importar.");
        Validate(rows);
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var lockKey = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes($"legacy:{device.EmpresaId}:{device.LojaId}")), 0);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", ct);
            var key = ReceiptKey(request.ImportId);
            var previous = await db.MobileProcessedMutations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.MutationId == key, ct);
            if (previous is not null)
            {
                if (previous.Outcome != "accepted:legacy" || string.IsNullOrWhiteSpace(previous.ResponseMeta))
                    throw Conflict("ImportId reservado por um registro anterior. Preserve o arquivo para reconciliacao.");
                var receipt = JsonSerializer.Deserialize<LegacySnapshotResponse>(previous.ResponseMeta!, Json)!;
                if (previous.EmpresaId != device.EmpresaId || receipt.LojaId != device.LojaId || receipt.SnapshotSha256 != hash
                    || !receipt.CoveredMutationIds.SequenceEqual(originIds)) throw Conflict("ImportId ja utilizado com outro arquivo, fila ou contexto.");
                await AssertBackupAsync(receipt, ct);
                await tx.CommitAsync(ct);
                return receipt with { Replayed = true };
            }
            var earlier = await db.MobileProcessedMutations.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.EmpresaId == device.EmpresaId && x.Outcome == "accepted:legacy").Select(x => x.ResponseMeta).ToListAsync(ct);
            if (earlier.Any(x => JsonSerializer.Deserialize<LegacySnapshotResponse>(x!, Json)!.LojaId == device.LojaId))
                throw Conflict("Esta loja ja recebeu uma primeira carga. Um novo arquivo exige reconciliacao, nao outra abertura de saldo.");
            if (await db.MobileProcessedMutations.IgnoreQueryFilters().AnyAsync(x => originIds.Contains(x.MutationId), ct))
                throw Conflict("Parte da fila ja foi processada. A origem nao pode ser tratada como nunca sincronizada.");
            await RequireAbsentAsync<Product>(rows["products"], ct);
            await RequireAbsentAsync<Client>(rows["clients"], ct);
            await RequireAbsentAsync<MobileOrder>(rows["orders"], ct);
            await RequireAbsentAsync<Batch>(rows["batches"], ct);
            await RequireAbsentAsync<CashEntry>(rows["cashEntries"], ct);
            // Nao fundir catalogos por nome durante uma abertura absoluta de estoque.
            var names = rows["products"].Select(x => x.GetProperty("name").GetString()!.Trim()).ToList();
            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count
                || await db.Set<Produto>().AnyAsync(x => x.EmpresaId == device.EmpresaId && names.Contains(x.Nome), ct))
                throw Conflict("Ha produtos com nomes repetidos ou ja cadastrados no ERP. Vincule o catalogo antes da migracao.");
            var types = new[] { ("products", "product"), ("clients", "client"), ("orders", "order"), ("batches", "batch"), ("cashEntries", "cashEntry") };
            foreach (var (collection, type) in types)
                foreach (var row in rows[collection])
                    await dispatcher.ApplySnapshotAsync(new MutationDto("snapshot", device.Id, type + ".upsert", row, 0),
                        device.Id, request.OperatorName, device.EmpresaId, device.LojaId);
            await db.SaveChangesAsync(ct);
            var productIds = rows["products"].Select(Id).ToList();
            var clientIds = rows["clients"].Select(Id).ToList();
            var existingErpProducts = await db.Set<Produto>().Where(x => x.EmpresaId == device.EmpresaId).Select(x => x.Id).ToListAsync(ct);
            await products.ExecuteAsync(productIds, device.EmpresaId);
            var existingErpClients = await db.Set<Cliente>().Where(x => x.EmpresaId == device.EmpresaId).Select(x => x.Id).ToListAsync(ct);
            await clients.ExecuteAsync(clientIds, device.EmpresaId);
            await db.SaveChangesAsync(ct);
            var productMap = await db.Set<Product>().Where(x => productIds.Contains(x.Id) && x.EmpresaId == device.EmpresaId).ToDictionaryAsync(x => x.Id, ct);
            var clientMap = await db.Set<Client>().Where(x => clientIds.Contains(x.Id) && x.EmpresaId == device.EmpresaId).ToDictionaryAsync(x => x.Id, ct);
            if (productMap.Count != productIds.Count || clientMap.Count != clientIds.Count)
                throw Conflict("Nem todos os IDs do snapshot foram preservados. A importacao inteira foi revertida.");
            if (productMap.Values.Any(x => x.ErpProductId is { } id && existingErpProducts.Contains(id)))
                throw Conflict("O catalogo vinculado ja existe no ERP. Nada foi sobrescrito; reconcilie os produtos antes da abertura.");
            if (clientMap.Values.Any(x => x.ErpClienteId is { } id && existingErpClients.Contains(id)))
                throw Conflict("Ha clientes ja cadastrados no ERP. Confirme a identidade antes de vincular o historico.");
            if (productMap.Values.Any(x => x.ErpProductId is null) || clientMap.Values.Any(x => x.ErpClienteId is null))
                throw Conflict("Um cadastro nao pode ser vinculado ao ERP. A importacao inteira foi revertida.");
            await CreateOpeningStockAsync(productMap, device, request.ImportId, ct);
            await CreateHistoryAsync(rows, productMap, clientMap, device, request, ct);
            var archive = counts["cashClosings"] > 0 ? new List<string> { "cashClosings" } : [];
            var warnings = new List<string> { "Pedidos historicos nao geram pagamentos presumidos. Lotes e pedidos encerrados exigem revisao para alterar estoque. Pedidos prontos podem ser entregues com forma de pagamento informada." };
            if (archive.Count > 0) warnings.Add("Fechamentos antigos preservados no backup; nao reabrem nem recalculam caixa do EasyStok.");
            var response = new LegacySnapshotResponse(request.ImportId, hash, device.EmpresaId, device.LojaId, originIds, counts,
                false, archive, warnings, request.ImportId);
            db.Add(new DeviceBackup { Id = request.ImportId, DeviceId = device.Id, EmpresaId = device.EmpresaId,
                SnapshotJson = request.SnapshotJson, SizeBytes = Encoding.UTF8.GetByteCount(request.SnapshotJson),
                Note = "legacy-import:" + request.ImportId, OperatorName = request.OperatorName, CreatedAt = DateTime.UtcNow });
            db.MobileProcessedMutations.Add(new MobileProcessedMutation { MutationId = key, DeviceId = device.Id,
                EmpresaId = device.EmpresaId, Outcome = "accepted:legacy", ResponseMeta = JsonSerializer.Serialize(response, Json), CriadoEm = DateTime.UtcNow });
            foreach (var id in originIds) db.MobileProcessedMutations.Add(new MobileProcessedMutation { MutationId = id,
                DeviceId = device.Id, EmpresaId = device.EmpresaId, Outcome = "accepted",
                ResponseMeta = JsonSerializer.Serialize(new { legacyImportId = request.ImportId }), CriadoEm = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            await AssertBackupAsync(response, ct);
            await tx.CommitAsync(ct);
            return response;
        });
    }

    private async Task RequireAbsentAsync<T>(List<JsonElement> rows, CancellationToken ct) where T : class
    {
        var ids = rows.Select(Id).ToList();
        if (await db.Set<T>().IgnoreQueryFilters().AnyAsync(x => ids.Contains(EF.Property<string>(x, "Id")), ct))
            throw Conflict("Um ou mais IDs do arquivo ja existem no servidor. Nada foi substituido.");
    }

    private static void RequireUnambiguousProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid("LEGACY_AMBIGUOUS_JSON", "Propriedade JSON repetida ou ambigua: " + property.Name);
                RequireUnambiguousProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RequireUnambiguousProperties(item);
    }

    private static string Id(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(id.GetString()) || id.GetString()!.Length > 64)
            throw Invalid("LEGACY_ID_INVALID", "Registro sem ID de texto valido. Preserve o arquivo para revisao.");
        return id.GetString()!;
    }

    private static void Validate(Dictionary<string, List<JsonElement>> rows)
    {
        foreach (var row in rows["products"])
        {
            var p = row.Deserialize<ProductDto>(Json)!;
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 120 || string.IsNullOrWhiteSpace(p.Category) || p.Category.Length > 16 || p.Stock < 0 || p.Price < 0 || p.Cost < 0)
                throw Invalid("LEGACY_PRODUCT_INVALID", "Produto com nome, preco ou saldo invalido: " + p.Id);
        }
        foreach (var row in rows["clients"])
        {
            var c = row.Deserialize<ClientDto>(Json)!;
            if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 120 || c.Apt?.Length > 32 || c.Address?.Length > 255 || c.Phone?.Length > 32)
                throw Invalid("LEGACY_CLIENT_INVALID", "Cliente com campos incompletos ou acima do limite: " + c.Id);
        }
        foreach (var row in rows["orders"])
        {
            var o = row.Deserialize<OrderDto>(Json)!;
            if (o.ClientSnapshot is null || string.IsNullOrWhiteSpace(o.ClientSnapshot.Name) || o.Items is null || o.Items.Count == 0
                || o.Items.Any(i => string.IsNullOrWhiteSpace(i.Name) || i.Qty <= 0 || i.UnitPrice < 0)
                || !new[] { "aguardando", "preparando", "pronto", "entregue", "cancelado" }.Contains(o.Status)
                || Math.Abs(o.Items.Sum(i => Math.Round(i.Qty * i.UnitPrice, 2, MidpointRounding.AwayFromZero)) - o.Total) > 0.005m)
                throw Invalid("LEGACY_ORDER_INVALID", "Pedido com itens, status ou total divergente: " + o.Id);
            if (o.CreatedAt <= 0 || o.UpdatedAt <= 0) throw Invalid("LEGACY_DATE_INVALID", "Pedido sem data original: " + o.Id);
            _ = DateTimeOffset.FromUnixTimeMilliseconds(o.CreatedAt);
            _ = DateTimeOffset.FromUnixTimeMilliseconds(o.UpdatedAt);
        }
        var productIds = rows["products"].Select(Id).ToHashSet();
        foreach (var row in rows["batches"])
        {
            var b = row.Deserialize<BatchDto>(Json)!;
            if (string.IsNullOrWhiteSpace(b.Code) || b.Code.Length > 32 || b.CreatedAt <= 0 || b.Items is null || b.Items.Any(i => !productIds.Contains(i.ProductId) || i.Qty <= 0))
                throw Invalid("LEGACY_BATCH_INVALID", "Lote sem itens validos ou produto presente no snapshot: " + b.Id);
        }
        foreach (var row in rows["cashEntries"])
        {
            var c = row.Deserialize<CashEntryDto>(Json)!;
            if (c.Type is not ("income" or "expense") || c.Amount <= 0 || c.CreatedAt <= 0 || string.IsNullOrWhiteSpace(c.Description) || c.Description.Length > 255)
                throw Invalid("LEGACY_CASH_INVALID", "Lancamento financeiro invalido: " + c.Id);
        }
    }

    private async Task CreateOpeningStockAsync(Dictionary<string, Product> map, MobileDevice device, Guid importId, CancellationToken ct)
    {
        foreach (var mobile in map.Values)
        {
            var product = await db.Set<Produto>().SingleAsync(x => x.Id == mobile.ErpProductId && x.EmpresaId == device.EmpresaId, ct);
            if (await db.Set<ItemEstoque>().AnyAsync(x => x.ProdutoId == product.Id && x.EmpresaId == device.EmpresaId, ct))
                throw Conflict("Produto vinculado ja tem estoque ERP. A abertura absoluta exige reconciliacao.");
            if (mobile.Stock == 0) continue;
            var now = DateTime.UtcNow;
            var item = ItemEstoque.CriarParaEntrada(Guid.NewGuid(), device.EmpresaId, product, null, Quantidade.From(mobile.Stock),
                product.CustoReferencia ?? Dinheiro.Zero, product.PrecoReferencia, now, "legacy:" + importId.ToString("N") + ":" + product.Id.ToString("N"),
                null, null, null, null, null, null, null, null, null, "Saldo inicial da primeira migracao; nao representa nova producao.", now);
            item.LojaId = device.LojaId;
            db.Add(item);
            db.Add(MovimentacaoEstoque.CriarEntrada(Guid.NewGuid(), device.EmpresaId, item, NaturezaMovimentacaoEstoque.Ajuste,
                Quantidade.From(mobile.Stock), product.CustoReferencia ?? Dinheiro.Zero, now, "Saldo inicial do PWA", "legacy-import:" + importId, now));
        }
    }

    private async Task CreateHistoryAsync(Dictionary<string, List<JsonElement>> rows, Dictionary<string, Product> productMap,
        Dictionary<string, Client> clientMap, MobileDevice device, LegacySnapshotRequest request, CancellationToken ct)
    {
        foreach (var row in rows["batches"])
        {
            var b = await db.Set<Batch>().Include(x => x.Items).SingleAsync(x => x.Id == Id(row), ct);
            var lote = Lote.Criar(device.EmpresaId, BatchLinker.CodigoDoLote(b), b.CreatedAt, device.LojaId);
            lote.MobileBatchId = b.Id; lote.Origem = HistoryOrigin; lote.OperadorNome = request.OperatorName;
            foreach (var i in b.Items) lote.Itens.Add(new LoteItem { Id = Guid.NewGuid(), LoteId = lote.Id,
                ProdutoId = productMap[i.ProductId].ErpProductId, Nome = i.Name, Emoji = i.Emoji, Unidade = i.Unit,
                Quantidade = i.Qty, PesoG = i.WeightG, ValidadeDias = i.ValidityDays, ExpiraEm = i.ExpiresAt, FotoUrl = i.Photo, CriadoEm = b.CreatedAt });
            lote.Finalizar(); lote.Status = LoteMobileEstadoReconciler.StatusDesejado(b); lote.CriadoEm = b.CreatedAt; lote.FinalizadoEm = b.CreatedAt;
            db.Add(lote); b.ErpLoteId = lote.Id;
        }
        foreach (var row in rows["orders"])
        {
            var o = await db.Set<MobileOrder>().Include(x => x.Items).SingleAsync(x => x.Id == Id(row), ct);
            var active = o.Status is "aguardando" or "preparando";
            var pedido = Pedido.Criar(device.EmpresaId, null, device.LojaId, active ? "mobile" : HistoryOrigin);
            pedido.MobileOrderId = o.Id; pedido.Status = o.Status; pedido.ClienteNome = o.ClientSnapshotName;
            if (o.ClientId is not null && clientMap.TryGetValue(o.ClientId, out var client))
            { pedido.ClienteId = client.ErpClienteId; pedido.ClienteApt = client.Apt; pedido.ClienteTelefone = client.Phone; }
            pedido.Observacoes = o.Notes; pedido.CriadoEm = o.CreatedAt; pedido.AlteradoEm = o.UpdatedAt;
            pedido.AgendadoParaEm = o.ScheduledDeliveryAt;
            foreach (var i in o.Items)
            {
                var item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = i.Name, Emoji = i.Emoji,
                    Unidade = i.Unit, Quantidade = i.Qty, PrecoUnitario = i.UnitPrice,
                    ProdutoId = i.ProductId is not null && productMap.TryGetValue(i.ProductId, out var product) ? product.ErpProductId : null };
                item.RecalcularSubtotal(); pedido.Itens.Add(item);
            }
            pedido.RecalcularTotal();
            pedido.Eventos.Add(new PedidoEvento { Id = Guid.NewGuid(), PedidoId = pedido.Id, Tipo = "importado", StatusNovo = o.Status,
                Origem = HistoryOrigin, UsuarioNome = request.OperatorName, OcorridoEm = DateTime.UtcNow,
                Detalhes = "Snapshot legado preservado. Pagamentos antigos nao foram presumidos." });
            db.Add(pedido); o.ErpPedidoId = pedido.Id;
        }
        foreach (var row in rows["cashEntries"])
        {
            var c = await db.Set<CashEntry>().SingleAsync(x => x.Id == Id(row), ct);
            var movimento = MovimentoCaixa.Criar(device.EmpresaId, c.Type == "income" ? "entrada" : "saida", c.Amount, c.CreatedAt, device.LojaId);
            movimento.Origem = HistoryOrigin; movimento.Descricao = c.Description; movimento.Metodo = c.Metodo;
            movimento.Referencia = "mobile:" + c.Id; movimento.RegistradoPorNome = request.OperatorName; movimento.CriadoEm = c.CreatedAt;
            if (row.TryGetProperty("estornado", out var reversed) && reversed.ValueKind == JsonValueKind.True)
            { movimento.EstornadoEm = c.CreatedAt; movimento.MotivoEstorno = "Estado estornado preservado do snapshot legado."; }
            db.Add(movimento); c.ErpMovimentoCaixaId = movimento.Id;
        }
    }

    private async Task<string> AssertBackupAsync(LegacySnapshotResponse receipt, CancellationToken ct)
    {
        var snapshot = await db.Set<DeviceBackup>().AsNoTracking().Where(x => x.Id == receipt.BackupId && x.EmpresaId == receipt.EmpresaId)
            .Select(x => x.SnapshotJson).SingleOrDefaultAsync(ct);
        if (snapshot is null || Hash(snapshot) != receipt.SnapshotSha256) throw Conflict("Backup original indisponivel ou divergente. Preserve a copia do Android e contate a equipe.");
        return snapshot;
    }

    public async Task<object?> ReadAsync(Guid importId, MobileDevice device, CancellationToken ct)
    {
        var metadata = await db.MobileProcessedMutations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.MutationId == ReceiptKey(importId) && x.EmpresaId == device.EmpresaId && x.Outcome == "accepted:legacy").Select(x => x.ResponseMeta).SingleOrDefaultAsync(ct);
        if (metadata is null) return null;
        var receipt = JsonSerializer.Deserialize<LegacySnapshotResponse>(metadata, Json)!;
        if (receipt.LojaId != device.LojaId) return null;
        var snapshot = await AssertBackupAsync(receipt, ct);
        return new { importId, receipt.SnapshotSha256, snapshotJson = snapshot };
    }
}
