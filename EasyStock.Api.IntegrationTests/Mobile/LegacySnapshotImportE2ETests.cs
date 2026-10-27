using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MobileOrder = EasyStock.Domain.Entities.Mobile.Order;

namespace EasyStock.Api.IntegrationTests.Mobile;

[Collection("MobileE2E")]
public class LegacySnapshotImportE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Scenario(MobileDeviceCredentials Device, HttpClient Http, string ProductId, string OrderId, string BatchId, LegacySnapshotRequest Request);

    [SkippableFact]
    public async Task PrimeiraCarga_Saldo7Lote10Venda3_PreservaHistoricoSemRecriarEfeitos()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var s = await ScenarioAsync();
        var labelJson = s.Request.SnapshotJson.Replace("\"code\":\"LEGADO\"", "\"code\":\"BATCH-X\",\"lote\":\"LOT-261026\"");
        s = s with { Request = s.Request with { SnapshotJson = labelJson, SnapshotSha256 = Sha(labelJson) } };
        var receipt = await ImportAsync(s);
        receipt.Counts["cashClosings"].Should().Be(1);
        receipt.ArchiveOnlyCollections.Should().Contain("cashClosings");
        receipt.CoveredMutationIds.Should().Equal(s.Request.OriginMutationIds);
        await ReadDbAsync(s.Device.EmpresaId, async db =>
        {
            (await db.Set<Product>().SingleAsync(p => p.Id == s.ProductId)).Stock.Should().Be(7);
            var stock = await db.Set<ItemEstoque>().ToListAsync();
            stock.Should().ContainSingle().Which.QuantidadeAtual.Value.Should().Be(7);
            var movements = await db.Set<MovimentacaoEstoque>().ToListAsync();
            movements.Should().ContainSingle().Which.Natureza.Should().Be(NaturezaMovimentacaoEstoque.Ajuste);
            var lote = await db.Set<Lote>().SingleAsync();
            lote.Itens.Should().NotBeNull();
            lote.Codigo.Should().Be("LOT-261026-" + s.BatchId[^6..]);
            lote.MobileBatchId.Should().Be(s.BatchId);
            (await db.Set<Pedido>().SingleAsync()).Status.Should().Be("entregue");
            (await db.Set<Cliente>().SingleAsync()).Endereco.Should().Be("Endereco preservado");
            (await db.Set<Pedido>().SingleAsync()).ClienteTelefone.Should().Be("11999999999");
            (await db.Set<PedidoPagamento>().Where(payment => db.Set<Pedido>().Any(order => order.Id == payment.PedidoId && order.EmpresaId == s.Device.EmpresaId)).CountAsync()).Should().Be(0);
            (await db.Set<Venda>().CountAsync()).Should().Be(0);
            (await db.Set<CobrancaPedido>().CountAsync()).Should().Be(0);
            (await db.Set<OutboxMensagemNotificacao>().CountAsync()).Should().Be(0);
            (await db.Set<MovimentoCaixa>().SingleAsync()).Valor.Should().Be(4.5m);
            return 0;
        });
        var inspection = await fixture.SeedMobileDeviceAsync(s.Device.EmpresaId, s.Device.LojaId);
        using var inspectionHttp = fixture.CreateMobileClient(inspection.ApiKey);
        var pulled = await inspectionHttp.GetFromJsonAsync<JsonElement>($"/api/mobile/sync/pull?since=0&deviceId={inspection.DeviceId}");
        var mutations = pulled.GetProperty("mutations").EnumerateArray().ToList();
        var pulledOrder = mutations.Where(x => x.GetProperty("type").GetString() == "order.upsert").Should().ContainSingle().Subject;
        pulledOrder.GetProperty("payload").GetProperty("id").GetString().Should().Be(s.OrderId);
        pulledOrder.GetProperty("payload").GetProperty("legacyHistoryStatus").GetString().Should().Be("entregue");
        var pulledBatch = mutations.Where(x => x.GetProperty("type").GetString() == "batch.upsert").Should().ContainSingle().Subject;
        pulledBatch.GetProperty("payload").GetProperty("legacyImported").GetBoolean().Should().BeTrue();
        pulledBatch.GetProperty("payload").GetProperty("id").GetString().Should().Be(s.BatchId);
        pulledBatch.GetProperty("payload").GetProperty("lote").GetString().Should().Be("LOT-261026");
        pulledBatch.GetProperty("payload").GetProperty("code").GetString().Should().Be("BATCH-X");
        var readback = await s.Http.GetFromJsonAsync<JsonElement>($"/api/mobile/legacy-import/{s.Request.ImportId}/snapshot");
        readback.GetProperty("snapshotJson").GetString().Should().Be(s.Request.SnapshotJson);
        readback.GetProperty("snapshotSha256").GetString().Should().Be(s.Request.SnapshotSha256);
        var replay = await ImportAsync(s);
        replay.Replayed.Should().BeTrue();
        // O ID coberto ja esta refletido no snapshot. Reenvio do delta nao soma +4 outra vez.
        await PushAsync(s, s.Request.OriginMutationIds[0], "stock.delta", new { id = "sd-origin", productId = s.ProductId, qty = 4 });
        await ReadDbAsync(s.Device.EmpresaId, async db =>
        {
            (await db.Set<Product>().SingleAsync(p => p.Id == s.ProductId)).Stock.Should().Be(7);
            (await db.Set<ItemEstoque>().CountAsync()).Should().Be(1);
            (await db.Set<Batch>().CountAsync(x => x.EmpresaId == s.Device.EmpresaId)).Should().Be(1);
            (await db.Set<MovimentoCaixa>().CountAsync()).Should().Be(1);
            return 0;
        });
        // Rotacao de backups comuns nunca apaga o original da primeira carga.
        for (var i = 0; i < 9; i++)
            (await s.Http.PostAsJsonAsync("/api/mobile/devices/me/backup", new { snapshotJson = "{}", note = "auto" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.Http.GetAsync($"/api/mobile/legacy-import/{s.Request.ImportId}/snapshot")).StatusCode.Should().Be(HttpStatusCode.OK);
        var echo = JsonDocument.Parse(s.Request.SnapshotJson).RootElement.GetProperty("orders")[0];
        await PushAsync(s, "mut_" + Guid.NewGuid(), "order.upsert", echo);
        // Backfill tambem nao pode criar uma venda/pagamento para o historico importado.
        (await s.Http.PostAsync("/api/mobile/sync/backfill-erp-link", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await ReadDbAsync(s.Device.EmpresaId, async db => { (await db.Set<PedidoPagamento>().Where(payment => db.Set<Pedido>().Any(order => order.Id == payment.PedidoId && order.EmpresaId == s.Device.EmpresaId)).CountAsync()).Should().Be(0); (await db.Set<Venda>().CountAsync()).Should().Be(0); return 0; });
    }

    [SkippableFact]
    public async Task PrimeiraCarga_HashContextoColisaoERepeticaoDivergenteNaoSubstituemDados()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var s = await ScenarioAsync();
        (await s.Http.PostAsJsonAsync("/api/mobile/legacy-import", s.Request with { SnapshotSha256 = new string('0', 64) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Http.PostAsJsonAsync("/api/mobile/legacy-import", s.Request with { SourceNeverSynced = false })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await s.Http.PostAsJsonAsync("/api/mobile/legacy-import", s.Request with { ExpectedEmpresaId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ReadDbAsync(s.Device.EmpresaId, async db => { (await db.Set<Product>().CountAsync(x => x.EmpresaId == s.Device.EmpresaId)).Should().Be(0); return 0; });
        await ImportAsync(s);
        var changed = s.Request.SnapshotJson.Replace("\"stock\":7", "\"stock\":8");
        (await s.Http.PostAsJsonAsync("/api/mobile/legacy-import", s.Request with { SnapshotJson = changed, SnapshotSha256 = Sha(changed) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var (otherEmpresa, otherLoja) = await fixture.SeedEmpresaELojaAsync();
        var other = await fixture.SeedMobileDeviceAsync(otherEmpresa, otherLoja);
        using var otherHttp = fixture.CreateMobileClient(other.ApiKey);
        var collision = s.Request with { ImportId = Guid.NewGuid(), ExpectedEmpresaId = otherEmpresa, ExpectedLojaId = otherLoja, OriginMutationIds = [] };
        (await otherHttp.PostAsJsonAsync("/api/mobile/legacy-import", collision)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await otherHttp.GetAsync($"/api/mobile/legacy-import/{s.Request.ImportId}/snapshot")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ReadDbAsync(otherEmpresa, async db => { (await db.Set<Pedido>().CountAsync()).Should().Be(0); return 0; });
        using var anonymous = fixture.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/mobile/legacy-import", s.Request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task PrimeiraCarga_NovaVendaDepoisDoSnapshotDescontaUmaVez_EHistoricoNaoMuda()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var s = await ScenarioAsync();
        await ImportAsync(s);
        var newOrder = "new-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = new { id = newOrder, clientSnapshot = new { name = "Pedido novo" },
            items = new[] { new { productId = s.ProductId, name = "Produto", qty = 1, unitPrice = 10m } }, total = 10m,
            status = "pronto", createdAt = now, updatedAt = now };
        await PushAsync(s, "mut_" + Guid.NewGuid(), "order.upsert", payload);
        await PushAsync(s, "mut_" + Guid.NewGuid(), "stock.delta", new { id = "sd-new", productId = s.ProductId, qty = -1 });
        await ReadDbAsync(s.Device.EmpresaId, async db =>
        {
            (await db.Set<Product>().SingleAsync(x => x.Id == s.ProductId)).Stock.Should().Be(6);
            (await db.Set<ItemEstoque>().SingleAsync()).QuantidadeAtual.Value.Should().Be(6);
            (await db.Set<Pedido>().SingleAsync(x => x.MobileOrderId == newOrder)).Status.Should().Be("pronto");
            return 0;
        });
        var historical = System.Text.Json.Nodes.JsonNode.Parse(s.Request.SnapshotJson)!["orders"]![0]!;
        historical["status"] = "cancelado";
        foreach (var keyShape in new[] { "id", "Id", "ambiguous" })
        {
            var variant = historical.DeepClone().AsObject();
            if (keyShape != "id") { variant["Id"] = variant["id"]!.GetValue<string>(); variant.Remove("id"); }
            if (keyShape == "ambiguous") variant["id"] = "outro-id";
            var attempt = await s.Http.PostAsJsonAsync("/api/mobile/sync", new { deviceId = s.Device.DeviceId,
                mutations = new object[] { new { id = "mut_" + Guid.NewGuid(), deviceId = s.Device.DeviceId, type = "order.upsert", payload = variant, ts = now },
                    new { id = "mut_" + Guid.NewGuid(), deviceId = s.Device.DeviceId, type = "stock.delta", payload = new { id = "sd-forbidden", productId = s.ProductId, qty = 3 }, ts = now } } });
            var result = await attempt.Content.ReadFromJsonAsync<JsonElement>();
            result.GetProperty("rejected")[0].GetProperty("reason").GetString().Should().Contain("LEGACY_HISTORY_READ_ONLY");
            result.GetProperty("acceptedIds").GetArrayLength().Should().Be(0, keyShape);
            result.GetProperty("rejected").GetArrayLength().Should().Be(2);
        }
        await ReadDbAsync(s.Device.EmpresaId, async db => { (await db.Set<Product>().SingleAsync(x => x.Id == s.ProductId)).Stock.Should().Be(6); return 0; });
    }

    [SkippableFact]
    public async Task PrimeiraCarga_FalhaAposGravacaoParcialReverteTudo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var s = await ScenarioAsync();
        // Um nome com markup passa pela guarda inicial, mas o linker sanitiza e encontra o produto ERP.
        // O estoque ja existente e detectado depois das insercoes mobile: a transacao deve desfazer tudo.
        var existingId = "existing-" + Guid.NewGuid().ToString("N");
        var name = "Existente " + Guid.NewGuid().ToString("N");
        await PushAsync(s, "mut_" + Guid.NewGuid(), "product.upsert", new { id = existingId, name, category = "massa", stock = 1, price = 10m });
        await PushAsync(s, "mut_" + Guid.NewGuid(), "batch.upsert", new { id = "b-" + Guid.NewGuid().ToString("N"), code = "B1", createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            items = new[] { new { productId = existingId, name, qty = 1 } } });
        using var original = JsonDocument.Parse(s.Request.SnapshotJson);
        var oldName = original.RootElement.GetProperty("products")[0].GetProperty("name").GetString()!;
        var json = s.Request.SnapshotJson.Replace(oldName, "<b>" + name + "</b>");
        var response = await s.Http.PostAsJsonAsync("/api/mobile/legacy-import", s.Request with { SnapshotJson = json, SnapshotSha256 = Sha(json) });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        await ReadDbAsync(s.Device.EmpresaId, async db =>
        {
            (await db.Set<Product>().AnyAsync(x => x.Id == s.ProductId)).Should().BeFalse();
            (await db.Set<MobileOrder>().AnyAsync(x => x.Id == s.OrderId)).Should().BeFalse();
            (await db.Set<DeviceBackup>().AnyAsync(x => x.Id == s.Request.ImportId)).Should().BeFalse();
            (await db.MobileProcessedMutations.AnyAsync(x => x.MutationId == "legacy:" + s.Request.ImportId.ToString("N"))).Should().BeFalse();
            (await db.Set<ItemEstoque>().SingleAsync()).QuantidadeAtual.Value.Should().Be(1);
            return 0;
        });
    }

    [SkippableFact]
    public async Task PrimeiraCarga_PedidoEmPreparoContinuaNaEsteira_EProntoEntregaSemOutraBaixa()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        foreach (var initial in new[] { "preparando", "pronto" })
        {
            var seed = await ScenarioAsync();
            var json = seed.Request.SnapshotJson.Replace("\"status\":\"entregue\"", "\"status\":\"" + initial + "\"");
            var s = seed with { Request = seed.Request with { SnapshotJson = json, SnapshotSha256 = Sha(json) } };
            await ImportAsync(s);
            var state = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            var order = state["orders"]![0]!;
            if (initial == "pronto")
            {
                order["confirmedBy"] = "Operacao";
                order["confirmedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                order["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await PushAsync(s, "mut_" + Guid.NewGuid(), "order.upsert", order);
            }
            order["status"] = initial == "pronto" ? "entregue" : "pronto";
            order["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            order["metodo"] = "pix";
            var id = "mut_" + Guid.NewGuid();
            await PushAsync(s, id, "order.upsert", order);
            await PushAsync(s, id, "order.upsert", order);
            if (initial == "preparando") await PushAsync(s, "mut_" + Guid.NewGuid(), "stock.delta", new { id = "sd-next", productId = s.ProductId, qty = -3 });
            await ReadDbAsync(s.Device.EmpresaId, async db =>
            {
                var expected = initial == "pronto" ? 7 : 4;
                (await db.Set<Product>().SingleAsync(x => x.Id == s.ProductId)).Stock.Should().Be(expected);
                (await db.Set<ItemEstoque>().SingleAsync()).QuantidadeAtual.Value.Should().Be(expected);
                (await db.Set<Pedido>().SingleAsync()).Status.Should().Be(initial == "pronto" ? "entregue" : "pronto");
                (await db.Set<PedidoPagamento>().Where(payment => db.Set<Pedido>().Any(order => order.Id == payment.PedidoId && order.EmpresaId == s.Device.EmpresaId)).CountAsync()).Should().Be(initial == "pronto" ? 1 : 0);
                if (initial == "pronto") (await db.Set<PedidoPagamento>().Where(payment => db.Set<Pedido>().Any(order => order.Id == payment.PedidoId && order.EmpresaId == s.Device.EmpresaId)).SingleAsync()).Metodo.Should().Be("pix");
                return 0;
            });
        }
    }

    [SkippableFact]
    public async Task PrimeiraCarga_DuasTentativasConcorrentesTemUmaUnicaAbertura()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var s = await ScenarioAsync();
        var results = await Task.WhenAll(ImportAsync(s), ImportAsync(s));
        results.Count(x => x.Replayed).Should().Be(1);
        await ReadDbAsync(s.Device.EmpresaId, async db =>
        {
            (await db.Set<ItemEstoque>().CountAsync()).Should().Be(1);
            (await db.Set<Pedido>().CountAsync()).Should().Be(1);
            return 0;
        });
    }

    [SkippableFact]
    public async Task PrimeiraCarga_ChavesIdAmbiguasNaoContornamCreateIfAbsent()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var s = await ScenarioAsync();
        var existing = "existing-" + Guid.NewGuid().ToString("N");
        await PushAsync(s, "mut_" + Guid.NewGuid(), "product.upsert", new { id = existing, name = "Original " + existing, category = "massa", stock = 5, price = 10m });
        var json = JsonSerializer.Serialize(new { products = new[] { new { id = s.ProductId, name = "Alterado", category = "massa", stock = 999, price = 99m } },
            clients = Array.Empty<object>(), orders = Array.Empty<object>(), batches = Array.Empty<object>(), cashEntries = Array.Empty<object>(), cashClosings = Array.Empty<object>() }, Json);
        json = json.Replace("\"id\":\"" + s.ProductId + "\"", "\"id\":\"" + s.ProductId + "\",\"Id\":\"" + existing + "\"");
        var response = await s.Http.PostAsJsonAsync("/api/mobile/legacy-import", s.Request with { SnapshotJson = json, SnapshotSha256 = Sha(json), OriginMutationIds = [] });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("LEGACY_AMBIGUOUS_JSON");
        await ReadDbAsync(s.Device.EmpresaId, async db =>
        {
            var product = await db.Set<Product>().SingleAsync(x => x.Id == existing);
            product.Name.Should().Be("Original " + existing); product.Stock.Should().Be(5);
            (await db.Set<DeviceBackup>().AnyAsync(x => x.Id == s.Request.ImportId)).Should().BeFalse();
            return 0;
        });
    }

    [SkippableFact]
    public async Task PrimeiraCarga_ERPAlteradoBloqueiaEntregaEBackfillSemRessuscitarPedido()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        foreach (var entregueAntesDoCancelamento in new[] { false, true })
        {
            var seed = await ScenarioAsync();
            var json = seed.Request.SnapshotJson.Replace("\"status\":\"entregue\"", "\"status\":\"pronto\"");
            var s = seed with { Request = seed.Request with { SnapshotJson = json, SnapshotSha256 = Sha(json) } };
            await ImportAsync(s);
            var order = System.Text.Json.Nodes.JsonNode.Parse(json)!["orders"]![0]!;
            order["status"] = "entregue"; order["metodo"] = "pix";
            order["updatedAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (entregueAntesDoCancelamento) await PushAsync(s, "mut_" + Guid.NewGuid(), "order.upsert", order);
            await ReadDbAsync(s.Device.EmpresaId, async db =>
            {
                var pedido = await db.Set<Pedido>().SingleAsync();
                pedido.Cancelar(); await db.SaveChangesAsync(); return 0;
            });
            if (!entregueAntesDoCancelamento)
            {
                var response = await s.Http.PostAsJsonAsync("/api/mobile/sync", new { deviceId = s.Device.DeviceId,
                    mutations = new[] { new { id = "mut_" + Guid.NewGuid(), deviceId = s.Device.DeviceId, type = "order.upsert", payload = order, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } } });
                var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                result.GetProperty("acceptedIds").GetArrayLength().Should().Be(0);
                result.GetProperty("rejected")[0].GetProperty("reason").GetString().Should().Contain("LEGACY_HISTORY_READ_ONLY");
            }
            (await s.Http.PostAsync("/api/mobile/sync/backfill-erp-link", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            await ReadDbAsync(s.Device.EmpresaId, async db =>
            {
                (await db.Set<Pedido>().SingleAsync()).Status.Should().Be("cancelado");
                (await db.Set<MobileOrder>().SingleAsync(x => x.Id == s.OrderId)).Status.Should().Be(entregueAntesDoCancelamento ? "entregue" : "pronto");
                (await db.Set<Product>().SingleAsync(x => x.Id == s.ProductId)).Stock.Should().Be(7);
                (await db.Set<ItemEstoque>().SingleAsync()).QuantidadeAtual.Value.Should().Be(7);
                (await db.Set<PedidoPagamento>().Where(payment => db.Set<Pedido>().Any(p => p.Id == payment.PedidoId && p.EmpresaId == s.Device.EmpresaId)).CountAsync()).Should().Be(entregueAntesDoCancelamento ? 1 : 0);
                (await db.Set<Venda>().CountAsync()).Should().Be(entregueAntesDoCancelamento ? 1 : 0);
                return 0;
            });
        }
    }
    private async Task<Scenario> ScenarioAsync()
    {
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresa, loja);
        var product = "p-" + Guid.NewGuid().ToString("N"); var order = "o-" + Guid.NewGuid().ToString("N"); var batch = "b-" + Guid.NewGuid().ToString("N");
        var when = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        var name = "Produto " + product;
        var clientId = "c-" + Guid.NewGuid().ToString("N");
        var state = new { products = new[] { new { id = product, name, category = "massa", stock = 7, price = 10m, unit = "un" } }, clients = new[] { new { id = clientId, name = "Cliente " + clientId, apt = "62", address = "Endereco preservado", phone = "11999999999", lastOrder = when, orderCount = 1 } },
            orders = new[] { new { id = order, clientId, clientSnapshot = new { name = "Cliente " + clientId }, items = new[] { new { productId = product, name, qty = 3, unitPrice = 10m } }, total = 30m, status = "entregue", createdAt = when, updatedAt = when } },
            batches = new[] { new { id = batch, code = "LEGADO", createdAt = when, items = new[] { new { productId = product, name, qty = 10, weightG = 100, validityDays = 30 } } } },
            cashEntries = new[] { new { id = "cash-" + Guid.NewGuid().ToString("N"), type = "expense", amount = 4.5m, description = "Despesa historica", createdAt = when } },
            cashClosings = new[] { new { id = "closing-old", dateKey = "2026-10-01", saldoFinal = 25.5m, closedAt = when, notes = "Preservar integralmente" } } };
        var json = JsonSerializer.Serialize(state, Json);
        return new Scenario(device, fixture.CreateMobileClient(device.ApiKey), product, order, batch,
            new LegacySnapshotRequest(Guid.NewGuid(), json, Sha(json), ["mut_" + Guid.NewGuid()], true, empresa, loja, "Operacao"));
    }

    private async Task<LegacySnapshotResponse> ImportAsync(Scenario s)
    {
        var response = await s.Http.PostAsJsonAsync("/api/mobile/legacy-import", s.Request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<LegacySnapshotResponse>())!;
    }
    private static string Sha(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    private static async Task PushAsync(Scenario s, string id, string type, object payload)
    {
        var response = await s.Http.PostAsJsonAsync("/api/mobile/sync", new { deviceId = s.Device.DeviceId,
            mutations = new[] { new { id, deviceId = s.Device.DeviceId, type, payload, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() } } });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        (result.TryGetProperty("rejected", out var rejected) && rejected.ValueKind == JsonValueKind.Array ? rejected.GetArrayLength() : 0).Should().Be(0, result.ToString());
    }
    private async Task<T> ReadDbAsync<T>(Guid empresa, Func<EasyStockDbContext, Task<T>> action)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresa);
        return await action(db);
    }
}
