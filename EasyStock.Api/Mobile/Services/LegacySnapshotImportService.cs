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
        var legacyProducts = ReadProducts(rows["products"]);
        var union = UnifyRepeatedProducts(legacyProducts);
        var aliases = union.Aliases;
        var erpNames = legacyProducts.ToDictionary(x => x.Id, x => x.ErpName, StringComparer.Ordinal);
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
            // Colisao com o ERP nao trava a carga: cliente de mesmo telefone e produto de mesmo nome composto (nome + embalagem)
            // sem saldo nem lote vinculam. O que nao tem vinculo seguro (ambiguo, com saldo ou lote) recusa, listando tudo de uma vez.
            // Nome composto repetido so dentro do arquivo nao recusa: UnifyRepeatedProducts une no registro vencedor.
            var collisions = await FindCollisionsAsync(rows, erpNames.Values, device.EmpresaId, ct);
            if (collisions.Count > 0) throw Conflict(CollisionMessage(collisions));
            var types = new[] { ("products", "product"), ("clients", "client"), ("orders", "order"), ("batches", "batch"), ("cashEntries", "cashEntry") };
            foreach (var (collection, type) in types)
                foreach (var row in rows[collection])
                    await dispatcher.ApplySnapshotAsync(new MutationDto("snapshot", device.Id, type + ".upsert", row, 0),
                        device.Id, request.OperatorName, device.EmpresaId, device.LojaId);
            await db.SaveChangesAsync(ct);
            var productIds = rows["products"].Select(Id).ToList();
            var clientIds = rows["clients"].Select(Id).ToList();
            var existingErpProducts = await db.Set<Produto>().Where(x => x.EmpresaId == device.EmpresaId).Select(x => x.Id).ToListAsync(ct);
            // Nome composto repetido no arquivo: so o vencedor passa pelo linker; o alias herda o cadastro dele mais abaixo.
            // O linker vincula e cria pelo nome composto (nome + embalagem), nao pelo nome do app.
            await products.ExecuteAsync(productIds.Where(x => !aliases.ContainsKey(x)).ToList(), device.EmpresaId, erpNames);
            var existingErpClients = await db.Set<Cliente>().Where(x => x.EmpresaId == device.EmpresaId).Select(x => x.Id).ToListAsync(ct);
            // Sem telefone nao ha prova de identidade: cria sempre, sem vincular por nome. Com telefone, o linker do sync normal
            // vincula ao unico cliente do ERP de mesmo telefone (a ambiguidade ja foi recusada acima) ou cria.
            var withoutPhone = await CreateClientsWithoutPhoneAsync(clientIds, device.EmpresaId, ct);
            await clients.ExecuteAsync(clientIds.Where(x => !withoutPhone.Contains(x)).ToList(), device.EmpresaId);
            await db.SaveChangesAsync(ct);
            var productMap = await db.Set<Product>().Where(x => productIds.Contains(x.Id) && x.EmpresaId == device.EmpresaId).ToDictionaryAsync(x => x.Id, ct);
            var clientMap = await db.Set<Client>().Where(x => clientIds.Contains(x.Id) && x.EmpresaId == device.EmpresaId).ToDictionaryAsync(x => x.Id, ct);
            if (productMap.Count != productIds.Count || clientMap.Count != clientIds.Count)
                throw Conflict("Nem todos os IDs do snapshot foram preservados. A importacao inteira foi revertida.");
            // Nome repetido no arquivo: o alias herda o cadastro do ERP do vencedor (so o vencedor passou pelo linker). O espelho do alias continua.
            foreach (var (aliasId, winnerId) in aliases) productMap[aliasId].ErpProductId = productMap[winnerId].ErpProductId;
            // Rede de seguranca: o vinculo por prato do cardapio ou por ILIKE pode ligar a um cadastro que a checagem previa nao viu.
            var linkedProducts = productMap.Values.Where(x => !aliases.ContainsKey(x.Id) && x.ErpProductId is { } id && existingErpProducts.Contains(id)).ToList();
            var blocked = await BlockedLinksAsync(linkedProducts, device.EmpresaId, ct);
            if (blocked.Count > 0) throw Conflict(CollisionMessage(blocked));
            var linkedClients = clientMap.Values.Count(x => x.ErpClienteId is { } id && existingErpClients.Contains(id));
            if (productMap.Values.Any(x => x.ErpProductId is null) || clientMap.Values.Any(x => x.ErpClienteId is null))
                throw Conflict("Um cadastro nao pode ser vinculado ao ERP. A importacao inteira foi revertida.");
            // So o vencedor abre saldo, num item so: o alias nao abre o proprio. No grupo repetido o vencedor abre com a soma dos saldos
            // positivos distintos dos ativos (OpeningStockOfGroup); registro unico abre o proprio. Sem saldo positivo a abrir, abre com 0.
            var winners = productMap.Where(x => !aliases.ContainsKey(x.Key)).ToDictionary(x => x.Key, x => x.Value);
            await CreateOpeningStockAsync(winners, union, device, request.ImportId, ct);
            await CreateHistoryAsync(rows, productMap, clientMap, device, request, ct);
            var archive = counts["cashClosings"] > 0 ? new List<string> { "cashClosings" } : [];
            var warnings = new List<string> { "Pedidos historicos nao geram pagamentos presumidos. Lotes e pedidos encerrados exigem revisao para alterar estoque. Pedidos prontos podem ser entregues com forma de pagamento informada." };
            if (archive.Count > 0) warnings.Add("Fechamentos antigos preservados no backup; nao reabrem nem recalculam caixa do EasyStok.");
            if (linkedClients > 0) warnings.Add(linkedClients == 1
                ? "1 cliente ja existia no ERP (mesmo telefone) e foi vinculado; o cadastro do ERP nao foi alterado."
                : $"{linkedClients} clientes ja existiam no ERP (mesmo telefone) e foram vinculados; os cadastros do ERP nao foram alterados.");
            if (linkedProducts.Count > 0) warnings.Add(linkedProducts.Count == 1
                ? "1 produto vinculado ao cadastro existente do ERP (sem saldo nem lote); o saldo aberto vem so do backup."
                : $"{linkedProducts.Count} produtos vinculados aos cadastros existentes do ERP (sem saldo nem lote); o saldo aberto vem so do backup.");
            warnings.AddRange(UnionWarnings(union));
            if (NegativeStockWarning(winners.Values.Where(x => union.StockToOpen(x) <= 0).Select(x => x.Stock)) is { } negative) warnings.Add(negative);
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

    /// <summary>
    /// #2813: o produto do ERP e nome + embalagem (LegacyProductName). Registros do arquivo com o mesmo nome composto (sem
    /// diferenciar maiusculas) viram um cadastro so no ERP. Vence o ativo (archived != true) mais recente: data de 13 digitos
    /// no fim do id (0 se nao houver); empate ou sem data, o que aparece depois no arquivo. Sem nenhum ativo, vence o arquivado
    /// mais recente. Os demais viram alias: nao passam pelo linker, herdam o cadastro do ERP do vencedor e nao abrem o proprio
    /// saldo: o vencedor abre com a soma dos saldos positivos distintos dos ativos do grupo (OpeningStockOfGroup). O espelho de
    /// cada alias continua existindo. Mesmo nome com embalagens diferentes sao produtos diferentes: cada um abre o proprio saldo.
    /// </summary>
    private static ProductUnion UnifyRepeatedProducts(List<LegacyProduct> products)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var openingStock = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var groups = 0;
        foreach (var group in products.GroupBy(x => x.ErpName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            var winner = group.OrderBy(x => x.Archived).ThenByDescending(x => DateInId(x.Id)).ThenByDescending(x => x.Index).First();
            foreach (var other in group.Where(x => x.Index != winner.Index)) aliases[other.Id] = winner.Id;
            openingStock[winner.Id] = OpeningStockOfGroup(winner, group);
            groups++;
        }
        return new ProductUnion(aliases, groups, openingStock);
    }

    /// <summary>
    /// #2813, decisao do Felipe (17/11, "Somar, menos copias"): o vencedor abre com a SOMA dos saldos positivos DISTINTOS dos
    /// registros ATIVOS do grupo. Saldos iguais sao copias e contam uma vez; arquivado, zero e negativo ficam fora. Sem nenhum
    /// ativo positivo vale o saldo do proprio vencedor, como antes (so abre se for positivo). Registro unico nao passa por aqui.
    /// Risco aceito: dois registros com o mesmo saldo que NAO sejam copias perdem um saldo (a dona confere os grupos reais na Fase 5).
    /// </summary>
    private static decimal OpeningStockOfGroup(LegacyProduct winner, IEnumerable<LegacyProduct> group)
    {
        var distinct = group.Where(x => !x.Archived && x.Stock > 0).Select(x => x.Stock).Distinct().ToList();
        return distinct.Count == 0 ? winner.Stock : distinct.Sum();
    }

    /// <summary>Uniao dos repetidos: alias -> vencedor, quantos grupos e o saldo que o vencedor de cada grupo abre (quem nao esta em OpeningStock, registro unico, abre o proprio).</summary>
    private sealed record ProductUnion(Dictionary<string, string> Aliases, int Groups, Dictionary<string, decimal> OpeningStock)
    {
        public decimal StockToOpen(Product product) => OpeningStock.GetValueOrDefault(product.Id, product.Stock);
    }

    /// <summary>Produto do arquivo com o nome que ele tera no ERP (nome + embalagem).</summary>
    private sealed record LegacyProduct(string Id, string ErpName, bool Archived, int Index, decimal Stock);

    private static List<LegacyProduct> ReadProducts(List<JsonElement> products) => products.Select((row, index) =>
    {
        var p = row.Deserialize<ProductDto>(Json)!;
        return new LegacyProduct(p.Id, LegacyProductName.Compose(p.Name, p.Unit), IsArchived(row), index, p.Stock);
    }).ToList();

    private static bool IsArchived(JsonElement row) => row.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True;

    private static long DateInId(string id) => id.Length > 13 && id[^14] == '-'
        && long.TryParse(id[^13..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ms) ? ms : 0;

    /// <summary>Avisos da uniao: so numeros, nunca nomes. A unidade de cada pedido e lote continua a do proprio registro.</summary>
    private static IEnumerable<string> UnionWarnings(ProductUnion union)
    {
        if (union.Groups == 0) yield break;
        var groups = union.Groups == 1 ? "1 produto aparecia repetido no arquivo" : $"{union.Groups} produtos apareciam repetidos no arquivo";
        var extra = union.Aliases.Count == 1 ? "1 registro a mais" : $"{union.Aliases.Count} registros a mais";
        yield return $"{groups} (mesmo nome e embalagem; {extra}). Cada um virou um cadastro so, com o preco do registro ativo mais recente e o saldo somado dos registros ativos (saldos iguais contam uma vez); pedidos e lotes dos registros a mais ficam ligados a esse cadastro.";
    }

    /// <summary>Saldo negativo nao recusa o arquivo: abre com 0 no ERP (conta so quem abre saldo). O numero original fica no espelho e no backup.</summary>
    private static string? NegativeStockWarning(IEnumerable<decimal> stocks)
    {
        var negatives = stocks.Where(x => x < 0).ToList();
        if (negatives.Count == 0) return null;
        var sum = negatives.Sum().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        return negatives.Count == 1
            ? $"1 produto com saldo negativo entrou com saldo 0 (soma zerada: {sum})."
            : $"{negatives.Count} produtos com saldo negativo entraram com saldo 0 (soma zerada: {sum}).";
    }

    private const int MaxCollisionLines = 200;

    private static string CollisionMessage(List<string> lines)
    {
        var head = lines.Count == 1 ? "Ha 1 colisao entre o backup e o ERP." : $"Ha {lines.Count} colisoes entre o backup e o ERP.";
        var shown = lines.Take(MaxCollisionLines).Select(x => "- " + x).ToList();
        if (lines.Count > shown.Count) shown.Add($"- ... e mais {lines.Count - shown.Count} colisoes.");
        return head + " Nada foi importado; resolva e envie de novo:\n" + string.Join("\n", shown);
    }

    /// <summary>Colisoes de cadastro, todas de uma vez. Mesmo universo e mesma comparacao dos linkers do sync normal.</summary>
    private async Task<List<string>> FindCollisionsAsync(Dictionary<string, List<JsonElement>> rows, IEnumerable<string> erpProductNames, Guid empresaId, CancellationToken ct)
    {
        var lines = new List<string>();
        // #2813: o produto do arquivo e o nome composto (nome + embalagem), o mesmo que vira o nome no ERP. Repetido so dentro do
        // arquivo nao e colisao (UnifyRepeatedProducts une no vencedor); aqui entra so o que bate com o ERP.
        var erpProducts = await db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.EmpresaId == empresaId && x.Status == StatusProduto.Ativo).Select(x => new { x.Id, x.Nome }).ToListAsync(ct);
        var erpByName = erpProducts.GroupBy(x => x.Nome.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).Order().ToList(), StringComparer.OrdinalIgnoreCase);
        var matches = erpProductNames.Distinct(StringComparer.OrdinalIgnoreCase).Where(erpByName.ContainsKey)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(x => (Name: x, Erp: erpByName[x])).ToList();
        var (withStock, withLot) = await StockAndLotAsync(matches.SelectMany(x => x.Erp).Distinct().ToList(), empresaId, ct);
        foreach (var (name, erp) in matches)
        {
            if (erp.Count > 1)
                lines.Add($"Produto \"{name}\": {erp.Count} cadastros ativos do ERP com o mesmo nome ({string.Join(", ", erp)}). Nao ha como escolher um; renomeie ou inative os extras.");
            else if (withStock.Contains(erp[0]) || withLot.Contains(erp[0]))
                lines.Add($"Produto \"{name}\": o cadastro do ERP {erp[0]} ja tem {(withStock.Contains(erp[0]) ? (withLot.Contains(erp[0]) ? "saldo e lote" : "saldo") : "lote")}. Reconcilie antes de abrir o saldo do backup.");
        }
        var backupClients = rows["clients"].Select(r => r.Deserialize<ClientDto>(Json)!)
            .Select(x => (x.Id, x.Name, x.Phone, Key: ChaveDeTelefone.De(x.Phone))).Where(x => x.Key.Length > 0).ToList();
        if (backupClients.Count > 0)
        {
            var erpClients = await db.Set<Cliente>().IgnoreQueryFilters().AsNoTracking().Where(x => x.EmpresaId == empresaId && x.Ativo)
                .Select(x => new { x.Id, x.Nome, x.Telefone }).ToListAsync(ct);
            var erpByPhone = erpClients.Select(x => (x.Id, x.Nome, Key: ChaveDeTelefone.De(x.Telefone))).Where(x => x.Key.Length > 0).ToLookup(x => x.Key);
            foreach (var client in backupClients.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var same = erpByPhone[client.Key].OrderBy(x => x.Id).ToList();
                if (same.Count > 1)
                    lines.Add($"Cliente \"{client.Name}\" (telefone {client.Phone}): {same.Count} clientes do ERP com o mesmo telefone ({string.Join("; ", same.Select(x => $"{x.Id} \"{x.Nome}\""))}). Deixe um so cadastro por telefone.");
            }
        }
        return lines;
    }

    private async Task<(HashSet<Guid> Stock, HashSet<Guid> Lot)> StockAndLotAsync(List<Guid> productIds, Guid empresaId, CancellationToken ct)
    {
        if (productIds.Count == 0) return (new HashSet<Guid>(), new HashSet<Guid>());
        var stock = await db.Set<ItemEstoque>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresaId && productIds.Contains(x.ProdutoId))
            .Select(x => x.ProdutoId).Distinct().ToListAsync(ct);
        var lot = await db.Set<Lote>().IgnoreQueryFilters().Where(x => x.EmpresaId == empresaId).SelectMany(x => x.Itens)
            .Where(x => x.ProdutoId != null && productIds.Contains(x.ProdutoId.Value)).Select(x => x.ProdutoId!.Value).Distinct().ToListAsync(ct);
        return (stock.ToHashSet(), lot.ToHashSet());
    }

    /// <summary>Produto do ERP ligado pelo linker (nome ou prato do cardapio) so vale se nao tem saldo nem lote e nao foi ligado duas vezes.</summary>
    private async Task<List<string>> BlockedLinksAsync(List<Product> linked, Guid empresaId, CancellationToken ct)
    {
        var lines = new List<string>();
        if (linked.Count == 0) return lines;
        foreach (var group in linked.GroupBy(x => x.ErpProductId!.Value).Where(g => g.Count() > 1))
            lines.Add($"Produtos {string.Join(", ", group.Select(x => $"\"{x.Name}\"").Order(StringComparer.Ordinal))} do arquivo ligaram ao mesmo cadastro do ERP {group.Key}. Deixe um so.");
        var (withStock, withLot) = await StockAndLotAsync(linked.Select(x => x.ErpProductId!.Value).Distinct().ToList(), empresaId, ct);
        lines.AddRange(linked.Where(x => withStock.Contains(x.ErpProductId!.Value) || withLot.Contains(x.ErpProductId!.Value))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"Produto \"{x.Name}\": ligou ao cadastro do ERP {x.ErpProductId} que ja tem saldo ou lote. Reconcilie antes de abrir o saldo do backup."));
        return lines;
    }

    /// <summary>Cliente sem telefone nao tem prova de identidade: cria sempre, sem vincular por nome como faz o sync normal.</summary>
    private async Task<HashSet<string>> CreateClientsWithoutPhoneAsync(List<string> clientIds, Guid empresaId, CancellationToken ct)
    {
        var handled = new HashSet<string>(StringComparer.Ordinal);
        var mobile = await db.Set<Client>().Where(x => clientIds.Contains(x.Id) && x.EmpresaId == empresaId).ToListAsync(ct);
        foreach (var client in mobile.Where(x => x.ErpClienteId is null && ChaveDeTelefone.De(x.Phone).Length == 0).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            var novo = Cliente.Criar(empresaId, client.Name);
            novo.Apt = client.Apt; novo.Endereco = client.Address; novo.Telefone = client.Phone;
            novo.LastOrderAt = client.LastOrder; novo.OrderCount = client.OrderCount;
            db.Add(novo); client.ErpClienteId = novo.Id; handled.Add(client.Id);
            db.Add(new ClienteAlteracao { Id = Guid.NewGuid(), EmpresaId = empresaId, ClienteId = novo.Id, Campo = "criado", ValorAntigo = null,
                ValorNovo = $"Nome={client.Name}; Telefone={client.Phone}; mobile_client={client.Id}", AlteradoEm = DateTime.UtcNow, Origem = "mobile" });
        }
        return handled;
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
            // Saldo negativo nao invalida: o PWA vende sem estoque. Abre com 0 e avisa (CreateOpeningStockAsync). Preco null tambem passa.
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 120 || string.IsNullOrWhiteSpace(p.Category) || p.Category.Length > 16 || p.Price < 0 || p.Cost < 0)
                throw Invalid("LEGACY_PRODUCT_INVALID", "Produto com nome, categoria, preco ou custo invalido: " + p.Id);
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

    private async Task CreateOpeningStockAsync(Dictionary<string, Product> map, ProductUnion union, MobileDevice device, Guid importId, CancellationToken ct)
    {
        foreach (var mobile in map.Values)
        {
            var product = await db.Set<Produto>().SingleAsync(x => x.Id == mobile.ErpProductId && x.EmpresaId == device.EmpresaId, ct);
            if (await db.Set<ItemEstoque>().AnyAsync(x => x.ProdutoId == product.Id && x.EmpresaId == device.EmpresaId, ct))
                throw Conflict("Produto vinculado ja tem estoque ERP. A abertura absoluta exige reconciliacao.");
            var stock = union.StockToOpen(mobile);
            if (stock <= 0) continue; // sem saldo positivo a abrir (negativo abre com 0): sem item de estoque nem movimento
            var now = DateTime.UtcNow;
            var item = ItemEstoque.CriarParaEntrada(Guid.NewGuid(), device.EmpresaId, product, null, Quantidade.From(stock),
                product.CustoReferencia ?? Dinheiro.Zero, product.PrecoReferencia, now, "legacy:" + importId.ToString("N") + ":" + product.Id.ToString("N"),
                null, null, null, null, null, null, null, null, null, "Saldo inicial da primeira migracao; nao representa nova producao.", now);
            item.LojaId = device.LojaId;
            db.Add(item);
            db.Add(MovimentacaoEstoque.CriarEntrada(Guid.NewGuid(), device.EmpresaId, item, NaturezaMovimentacaoEstoque.Ajuste,
                Quantidade.From(stock), product.CustoReferencia ?? Dinheiro.Zero, now, "Saldo inicial do PWA", "legacy-import:" + importId, now));
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
            // Mesmo criterio do sync normal (OrderLinker): a data do ultimo evento do PWA e a data da entrega ou do cancelamento.
            if (o.Status == "entregue")
            {
                pedido.EntreguEm = o.UpdatedAt;
                // #2813: o entregue do histórico entra quitado, sem pagamento nem caixa; só o import grava a marca.
                pedido.MarcarQuitadoHistorico(o.UpdatedAt);
            }
            else if (o.Status == "cancelado") pedido.CanceladoEm = o.UpdatedAt;
            pedido.AgendadoParaEm = o.ScheduledDeliveryAt;
            foreach (var i in o.Items)
            {
                var item = new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, Nome = i.Name, Emoji = i.Emoji,
                    Unidade = i.Unit, Quantidade = i.Qty, PrecoUnitario = i.UnitPrice,
                    ProdutoId = i.ProductId is not null && productMap.TryGetValue(i.ProductId, out var product) ? product.ErpProductId : null };
                item.RecalcularSubtotal(); pedido.Itens.Add(item);
            }
            pedido.RecalcularTotal(); pedido.AlteradoEm = o.UpdatedAt; // RecalcularTotal grava "agora"; o histórico guarda a data original do aparelho.
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
