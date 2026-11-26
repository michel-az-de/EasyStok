using System.Net;
using EasyStock.Api.Mobile.Services;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #3375: o pedido do app que ficou sem vínculo no ERP só era tentado de novo dentro de um envio do mesmo aparelho
/// que trouxesse produto, cliente, pedido, lote ou caixa. Na Casa da Baba o aparelho ficou 27 horas sem enviar e o
/// pedido seguiu só na tela dele. O aparelho lê do servidor o tempo todo: a leitura passa a tentar de novo.
/// </summary>
[Collection("MobileE2E")]
public class SyncPullRepescaOrfaoE2ETests(MobileE2EFixture fixture)
{
    [SkippableFact]
    public async Task Pull_VinculaOPedidoOrfaoDoAparelho_SemEnvioNovo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        var (outraEmpresaId, _) = await fixture.SeedEmpresaELojaAsync();
        var (orfao, deOutroAparelho, deOutraEmpresa, cancelado) = (NovoId(), NovoId(), NovoId(), NovoId());
        await PedidoOrfaoAsync(empresaId, aparelho.DeviceId, orfao);
        await PedidoOrfaoAsync(empresaId, "dev-outro-aparelho", deOutroAparelho);
        await PedidoOrfaoAsync(outraEmpresaId, aparelho.DeviceId, deOutraEmpresa);
        await PedidoOrfaoAsync(empresaId, aparelho.DeviceId, cancelado, "cancelado");

        await PullAsync(aparelho);

        (await ErpDoPedidoAsync(orfao)).Should().NotBeNull("a leitura do próprio aparelho tenta de novo o pedido que ficou sem vínculo");
        (await PedidosNoErpAsync(orfao)).Should().Be(1);
        (await ErpDoPedidoAsync(deOutroAparelho)).Should().BeNull("o órfão de outro aparelho não é promovido por este");
        (await ErpDoPedidoAsync(deOutraEmpresa)).Should().BeNull("o órfão de outra empresa nunca é tocado");
        (await ErpDoPedidoAsync(cancelado)).Should().BeNull("cancelado que nunca foi ao ERP fica de fora");
    }

    [SkippableFact]
    public async Task Pull_ComATravaDosPedidosOcupada_NaoPromove_ELiberadaPromoveUmaVezSo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        var orfao = NovoId();
        await PedidoOrfaoAsync(empresaId, aparelho.DeviceId, orfao);

        // Outra conexão segura a trava, como um envio do mesmo aparelho no meio do vínculo.
        await using (var outroVinculo = NovoContexto())
        {
            await outroVinculo.Database.OpenConnectionAsync();
            var chave = SyncAutoLinker.ChaveDaTravaDePedidos(empresaId);
            await outroVinculo.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock(hashtextextended({chave}, 0))");

            await PullAsync(aparelho);

            (await ErpDoPedidoAsync(orfao)).Should().BeNull("com o vínculo de pedidos em andamento a leitura não promove, para não sair Pedido em dobro");
            // Solta de propósito: fechar a conexão só a devolve ao pool, e a trava de sessão seguiria com ela.
            await outroVinculo.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({chave}, 0))");
        }

        // A tentativa pulada não gasta o intervalo: a leitura seguinte já promove.
        await PullAsync(aparelho);
        await PullAsync(aparelho);

        (await ErpDoPedidoAsync(orfao)).Should().NotBeNull();
        (await PedidosNoErpAsync(orfao)).Should().Be(1);
    }

    [SkippableFact]
    public async Task Pull_DentroDoIntervalo_NaoTentaDeNovo()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        var (primeiro, segundo) = (NovoId(), NovoId());
        await PedidoOrfaoAsync(empresaId, aparelho.DeviceId, primeiro);
        await PullAsync(aparelho);
        (await ErpDoPedidoAsync(primeiro)).Should().NotBeNull();

        await PedidoOrfaoAsync(empresaId, aparelho.DeviceId, segundo);
        await PullAsync(aparelho);

        (await ErpDoPedidoAsync(segundo)).Should().BeNull("a leitura acontece a cada poucos segundos; a nova tentativa espera o intervalo");
    }

    [SkippableFact]
    public async Task Pull_PedidoOrfaoJaEntregue_RecebeNaHoraDoAparelho()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresaId, aparelho) = await AparelhoAsync();
        var orfao = NovoId();
        var entregueNoAparelho = DateTime.UtcNow.AddDays(-3);
        await PedidoOrfaoAsync(empresaId, aparelho.DeviceId, orfao, "entregue", entregueNoAparelho);

        await PullAsync(aparelho);

        var pagoEm = await LerAsync(db => db.Set<PedidoPagamento>().IgnoreQueryFilters()
            .Where(pp => db.Set<Pedido>().IgnoreQueryFilters().Any(p => p.Id == pp.PedidoId && p.MobileOrderId == orfao))
            .Select(pp => pp.PagoEm).SingleAsync());
        pagoEm.Should().BeCloseTo(entregueNoAparelho, TimeSpan.FromSeconds(1),
            "o recebimento é do dia em que o aparelho entregou, não do dia em que o vínculo foi tentado de novo");
    }

    // ---------- preparo ----------

    private sealed record Aparelho(string DeviceId, HttpClient Http);

    private async Task<(Guid EmpresaId, Aparelho Aparelho)> AparelhoAsync()
    {
        var (empresaId, lojaId) = await fixture.SeedEmpresaELojaAsync();
        var cred = await fixture.SeedMobileDeviceAsync(empresaId, lojaId);
        return (empresaId, new Aparelho(cred.DeviceId, fixture.CreateMobileClient(cred.ApiKey)));
    }

    /// <summary>Pedido gravado pelo aparelho num envio cujo vínculo caiu, com a hora marcada que depois venceu.</summary>
    private async Task PedidoOrfaoAsync(Guid empresaId, string deviceId, string id, string status = "preparando", DateTime? alteradoNoAparelho = null)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste lê e semeia entre empresas
        db.Set<Order>().Add(new Order
        {
            Id = id, ClientSnapshotName = "Andreza", Total = 90m, Status = status, EmpresaId = empresaId, LastDeviceId = deviceId,
            ScheduledDeliveryAt = DateTime.UtcNow.AddDays(-6),
            UpdatedAt = alteradoNoAparelho ?? DateTime.UtcNow, Metodo = status == "entregue" ? "pix" : null,
            Items = [new OrderItem { OrderId = id, ProductId = "p-x", Name = "Lasanha Bolonhesa Clássica", Unit = "800g", Qty = 2, UnitPrice = 45m }]
        });
        await db.SaveChangesAsync();
    }

    private static async Task PullAsync(Aparelho aparelho)
    {
        var resp = await aparelho.Http.GetAsync($"/api/mobile/sync/pull?since=0&deviceId={aparelho.DeviceId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
    }

    private static string NovoId() => $"o-{Guid.NewGuid():N}"[..24];

    /// <summary>Contexto próprio do teste, para segurar a trava numa conexão que não é da API.</summary>
    private EasyStockDbContext NovoContexto()
    {
        var conexao = fixture.Factory!.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        return new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>().UseNpgsql(conexao).Options);
    }

    // ---------- conferência no banco (só leitura) ----------

    private async Task<T> LerAsync<T>(Func<EasyStockDbContext, Task<T>> consulta)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        using var semRls = db.UseRowLevelSecurityBypass(); // teste lê entre empresas
        return await consulta(db);
    }

    private Task<Guid?> ErpDoPedidoAsync(string id) => LerAsync(db =>
        db.Set<Order>().IgnoreQueryFilters().AsNoTracking().Where(o => o.Id == id).Select(o => o.ErpPedidoId).SingleAsync());

    private Task<int> PedidosNoErpAsync(string mobileOrderId) => LerAsync(db =>
        db.Set<Pedido>().IgnoreQueryFilters().CountAsync(p => p.MobileOrderId == mobileOrderId));
}
