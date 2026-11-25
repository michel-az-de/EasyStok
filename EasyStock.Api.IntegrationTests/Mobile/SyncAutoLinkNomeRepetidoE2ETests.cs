using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2911: um produto do app que não vira Produto do ERP (nome de produto inativo, nome repetido no mesmo envio,
/// nome longo demais) violava o índice único e derrubava o vínculo do envio inteiro: clientes e pedidos ficavam
/// só no aparelho. O envio seguinte também não os tentava de novo.
/// <para>
/// #3328: a venda agendada no aparelho cuja hora passou antes do envio (o APK chegou a atrasar de 5 a 29 dias) era
/// recusada pelo CriarPedidoUseCase ("Data agendada precisa ser no futuro.") e ficava órfã para sempre.
/// </para>
/// </summary>
[Collection("MobileE2E")]
public class SyncAutoLinkNomeRepetidoE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task MesmoNomeDuasVezesNoEnvio_ViraUmProduto_EClienteEPedidoVinculam()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        var (p1, p2, cliente, pedido) = (NovoId("p"), NovoId("p"), NovoId("c"), NovoId("o"));

        await PushAsync(aparelho,
            Produto(p1, "Lasanha Bolonhesa Clássica"), Produto(p2, "Lasanha Bolonhesa Clássica"),
            Cliente(cliente, "Thatiane"), Pedido(pedido, cliente, p1));

        var erp1 = await ErpDoProdutoAsync(p1);
        erp1.Should().NotBeNull();
        (await ErpDoProdutoAsync(p2)).Should().Be(erp1, "o mesmo nome no mesmo envio é o mesmo produto");
        (await ErpDoClienteAsync(cliente)).Should().NotBeNull();
        (await ErpDoPedidoAsync(pedido)).Should().NotBeNull();
        (await LerAsync(db => db.Set<Produto>().IgnoreQueryFilters()
            .CountAsync(p => p.EmpresaId == empresaId && p.Nome == "Lasanha Bolonhesa Clássica"))).Should().Be(1);
    }

    [SkippableFact]
    public async Task NomeDeProdutoInativo_VinculaAoInativoSemReativar()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        var inativo = await ProdutoNoErpAsync(empresaId, "Ravioli Mussarela & Limão", StatusProduto.Inativo);
        var (produto, pedido) = (NovoId("p"), NovoId("o"));

        await PushAsync(aparelho, Produto(produto, "Ravioli Mussarela & Limão"), Pedido(pedido, null, produto));

        (await ErpDoProdutoAsync(produto)).Should().Be(inativo);
        (await ErpDoPedidoAsync(pedido)).Should().NotBeNull();
        (await LerAsync(db => db.Set<Produto>().IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == inativo).Select(p => p.Status).SingleAsync())).Should().Be(StatusProduto.Inativo);
    }

    [SkippableFact]
    public async Task EnvioComNomeDeInativo_VinculaOsOutrosProdutosClienteEPedido()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        await ProdutoNoErpAsync(empresaId, "Lasanha Bolonhesa tradicional 800g", StatusProduto.Inativo);
        var (colide, bom, cliente, pedido) = (NovoId("p"), NovoId("p"), NovoId("c"), NovoId("o"));

        // O envio de produção de 19/11: com um nome de inativo no meio do lote, o vínculo do envio inteiro caía.
        await PushAsync(aparelho,
            Produto(colide, "Lasanha Bolonhesa tradicional 800g"), Produto(bom, "Nhoque"),
            Cliente(cliente, "Andreza"), Pedido(pedido, cliente, bom));

        (await ErpDoProdutoAsync(colide)).Should().NotBeNull();
        (await ErpDoProdutoAsync(bom)).Should().NotBeNull();
        (await ErpDoClienteAsync(cliente)).Should().NotBeNull();
        (await ErpDoPedidoAsync(pedido)).Should().NotBeNull();
    }

    [SkippableFact]
    public async Task EnvioSeguinte_VinculaClienteEPedidoQueFicaramSemVinculo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        var (cliente, pedido, cancelado) = (NovoId("c"), NovoId("o"), NovoId("o"));
        var (clienteDoOutro, pedidoDoOutro) = (NovoId("c"), NovoId("o"));
        await OrfaosAsync(empresaId, aparelho.DeviceId, cliente, pedido, cancelado);
        await OrfaosAsync(empresaId, "dev-outro", clienteDoOutro, pedidoDoOutro, NovoId("o"));

        await PushAsync(aparelho, Produto(NovoId("p"), "Talharim Fresco"));

        (await ErpDoClienteAsync(cliente)).Should().NotBeNull();
        (await ErpDoPedidoAsync(pedido)).Should().NotBeNull();
        (await ErpDoPedidoAsync(cancelado)).Should().BeNull("pedido cancelado que nunca foi ao ERP não vira pedido");
        // O órfão de outro aparelho espera o envio dele: dois envios simultâneos promoveriam o mesmo pedido duas vezes.
        (await ErpDoClienteAsync(clienteDoOutro)).Should().BeNull();
        (await ErpDoPedidoAsync(pedidoDoOutro)).Should().BeNull();

        // Reenvio: o pedido já promovido não vira um segundo Pedido no ERP.
        await PushAsync(aparelho, Produto(NovoId("p"), "Talharim Fresco"));
        (await LerAsync(db => db.Set<Pedido>().IgnoreQueryFilters().CountAsync(p => p.MobileOrderId == pedido))).Should().Be(1);
    }

    [SkippableFact]
    public async Task PedidoComAgendaJaVencidaNoEnvio_ViraPedidoSemAgenda()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (_, aparelho) = await AparelhoAsync();
        var (produto, pedido) = (NovoId("p"), NovoId("o"));
        var agenda = DateTime.UtcNow.AddDays(-7);

        await PushAsync(aparelho, Produto(produto, "Canelone de Frango"), Pedido(pedido, null, produto, agenda));

        (await ErpDoPedidoAsync(pedido)).Should().NotBeNull("a hora marcada já passou, mas a venda existe e precisa chegar ao ERP");
        (await AgendaDoPedidoNoErpAsync(pedido)).Should().BeNull();
        (await LerAsync(db => db.Set<Order>().IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == pedido).Select(o => o.ScheduledDeliveryAt).SingleAsync()))
            .Should().BeCloseTo(agenda, TimeSpan.FromSeconds(1), "o aparelho guarda a hora que o cliente marcou");
    }

    [SkippableFact]
    public async Task PedidoComAgendaFutura_ContinuaAgendadoNoErp()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (_, aparelho) = await AparelhoAsync();
        var (produto, pedido) = (NovoId("p"), NovoId("o"));
        var agenda = DateTime.UtcNow.AddDays(3);

        await PushAsync(aparelho, Produto(produto, "Canelone de Frango"), Pedido(pedido, null, produto, agenda));

        (await ErpDoPedidoAsync(pedido)).Should().NotBeNull();
        (await AgendaDoPedidoNoErpAsync(pedido)).Should().BeCloseTo(agenda, TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task EnvioSeguinte_VinculaOPedidoDeAgendaVencidaQueFicouOrfao_SemTocarOutraEmpresa()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        var (outraEmpresaId, _) = await fixture.SeedEmpresaELojaAsync();
        var (orfao, orfaoDaOutraEmpresa) = (NovoId("o"), NovoId("o"));
        var agenda = DateTime.UtcNow.AddDays(-20);
        await PedidoOrfaoAsync(empresaId, aparelho.DeviceId, orfao, agenda);
        // Mesmo id de aparelho em outra empresa: só o EmpresaId separa as duas.
        await PedidoOrfaoAsync(outraEmpresaId, aparelho.DeviceId, orfaoDaOutraEmpresa, agenda);

        await PushAsync(aparelho, Produto(NovoId("p"), "Talharim Fresco"));

        (await ErpDoPedidoAsync(orfao)).Should().NotBeNull("o pedido de 20 dias atrás é de um envio anterior do mesmo aparelho");
        (await AgendaDoPedidoNoErpAsync(orfao)).Should().BeNull();
        (await ErpDoPedidoAsync(orfaoDaOutraEmpresa)).Should().BeNull("o órfão de outra empresa nunca é tocado");

        // Reenvio: não vira um segundo Pedido.
        await PushAsync(aparelho, Produto(NovoId("p"), "Talharim Fresco"));
        (await LerAsync(db => db.Set<Pedido>().IgnoreQueryFilters().CountAsync(p => p.MobileOrderId == orfao))).Should().Be(1);
    }

    [SkippableFact]
    public async Task EnvioSemOrfaos_NaoCriaPedido()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();

        await PushAsync(aparelho, Produto(NovoId("p"), "Talharim Fresco"));

        (await LerAsync(db => db.Set<Pedido>().IgnoreQueryFilters().CountAsync(p => p.EmpresaId == empresaId))).Should().Be(0);
    }

    // ---------- preparo ----------

    private sealed record Aparelho(string DeviceId, HttpClient Http);
    private sealed record Envio(string Id, string Type, JsonElement Payload, long Ts);

    private async Task<(Guid EmpresaId, Aparelho Aparelho)> AparelhoAsync()
    {
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var cred = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
        return (empresaId, new Aparelho(cred.DeviceId, fixture.CreateMobileClient(cred.ApiKey)));
    }

    private async Task<Guid> ProdutoNoErpAsync(Guid empresaId, string nome, StatusProduto status)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste lê e semeia entre empresas
        var categoria = new Categoria { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Massas" };
        var produto = new Produto { Id = Guid.NewGuid(), EmpresaId = empresaId, CategoriaId = categoria.Id, Nome = nome, Status = status };
        db.Add(categoria);
        db.Add(produto);
        await db.SaveChangesAsync();
        return produto.Id;
    }

    /// <summary>Cliente e pedidos gravados pelo aparelho num envio cujo vínculo caiu, como na Casa da Baba.</summary>
    private async Task OrfaosAsync(Guid empresaId, string deviceId, string cliente, string pedido, string cancelado)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste lê e semeia entre empresas
        db.Set<Client>().Add(new Client { Id = cliente, Name = "Thatiane", Phone = "11988887777", EmpresaId = empresaId, LastDeviceId = deviceId });
        foreach (var (id, status) in new[] { (pedido, "preparando"), (cancelado, "cancelado") })
            db.Set<Order>().Add(new Order
            {
                Id = id, ClientId = cliente, ClientSnapshotName = "Thatiane", Total = 30m, Status = status, EmpresaId = empresaId, LastDeviceId = deviceId,
                Items = [new OrderItem { OrderId = id, ProductId = "p-x", Name = "Lasanha", Unit = "300g", Qty = 1, UnitPrice = 30m }]
            });
        await db.SaveChangesAsync();
    }

    /// <summary>Pedido gravado pelo aparelho num envio cujo vínculo caiu, com a hora marcada que depois venceu.</summary>
    private async Task PedidoOrfaoAsync(Guid empresaId, string deviceId, string id, DateTime agenda)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste lê e semeia entre empresas
        db.Set<Order>().Add(new Order
        {
            Id = id, ClientSnapshotName = "Thatiane", Total = 30m, Status = "aguardando", EmpresaId = empresaId, LastDeviceId = deviceId,
            ScheduledDeliveryAt = agenda,
            Items = [new OrderItem { OrderId = id, ProductId = "p-x", Name = "Lasanha", Unit = "300g", Qty = 1, UnitPrice = 30m }]
        });
        await db.SaveChangesAsync();
    }

    // ---------- HTTP e mutations como o PWA manda ----------

    private static async Task PushAsync(Aparelho aparelho, params Envio[] mutations)
    {
        var resp = await aparelho.Http.PostAsJsonAsync("/api/mobile/sync", new
        {
            deviceId = aparelho.DeviceId,
            operatorName = "Thati",
            mutations = mutations.Select(m => new { id = m.Id, deviceId = aparelho.DeviceId, type = m.Type, payload = m.Payload, ts = m.Ts })
        }, Json);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("acceptedIds").GetArrayLength().Should().Be(mutations.Length);
    }

    private static Envio Mutation(string tipo, object payload) => new(
        "mut_" + Guid.NewGuid(), tipo, JsonSerializer.SerializeToElement(payload, Json), Ms(DateTime.UtcNow));

    private static Envio Produto(string id, string nome) => Mutation("product.upsert",
        new { id, name = nome, category = "massa", unit = "300g", price = 30m, stock = 0, custom = true });

    private static Envio Cliente(string id, string nome) => Mutation("client.upsert",
        new { id, name = nome, apt = (string?)null, address = (string?)null, phone = "11988887777" });

    private static Envio Pedido(string id, string? cliente, string produto, DateTime? agenda = null)
    {
        var hora = Ms(DateTime.UtcNow);
        return Mutation("order.upsert", new
        {
            id, clientId = cliente, clientSnapshot = new { name = "Thatiane", @ref = (string?)null },
            items = new[] { new { productId = produto, name = "Lasanha", emoji = (string?)null, unit = "300g", qty = 1, unitPrice = 30m } },
            notes = (string?)null, total = 30m, status = "aguardando", createdAt = hora, updatedAt = hora,
            scheduledDeliveryAt = agenda is { } a ? Ms(a) : (long?)null
        });
    }

    private static string NovoId(string prefixo) => $"{prefixo}-{Guid.NewGuid():N}"[..24];
    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    // ---------- conferência no banco (só leitura) ----------

    private async Task<T> LerAsync<T>(Func<EasyStockDbContext, Task<T>> consulta)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste lê entre empresas
        return await consulta(db);
    }

    private Task<Guid?> ErpDoProdutoAsync(string id) => LerAsync(db =>
        db.Set<Product>().IgnoreQueryFilters().AsNoTracking().Where(p => p.Id == id).Select(p => p.ErpProductId).SingleAsync());

    private Task<Guid?> ErpDoClienteAsync(string id) => LerAsync(db =>
        db.Set<Client>().IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == id).Select(c => c.ErpClienteId).SingleAsync());

    private Task<Guid?> ErpDoPedidoAsync(string id) => LerAsync(db =>
        db.Set<Order>().IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == id).Select(o => o.ErpPedidoId).SingleAsync());

    private Task<DateTime?> AgendaDoPedidoNoErpAsync(string mobileOrderId) => LerAsync(db =>
        db.Set<Pedido>().IgnoreQueryFilters().AsNoTracking().Where(p => p.MobileOrderId == mobileOrderId).Select(p => p.AgendadoParaEm).SingleAsync());
}
