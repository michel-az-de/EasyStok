using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.IntegrationTests.Mobile;

/// <summary>
/// #2813: os pedidos entregues do histórico entram quitados, sem pagamento, sem movimento de caixa e sem conta a receber,
/// e o ERP os reconhece pela marca no pedido. Aguardando e cancelado do histórico não levam a marca e seguem como antes.
/// </summary>
[Collection("MobileE2E")]
public class LegacySnapshotImportPagosSemCaixaE2ETests(MobileE2EFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task PrimeiraCarga_EntreguesEntramQuitadosSemCaixa_AguardandoECanceladoSeguemSemMarca()
    {
        Skip.If(!fixture.IsAvailable, "Docker/PostgreSQL unavailable");
        var (empresa, loja) = await fixture.SeedEmpresaELojaAsync();
        var device = await fixture.SeedMobileDeviceAsync(empresa, loja);
        var produto = "p-" + Guid.NewGuid().ToString("N");
        var cliente = "c-" + Guid.NewGuid().ToString("N");
        string[] entregues = ["o-" + Guid.NewGuid().ToString("N"), "o-" + Guid.NewGuid().ToString("N"), "o-" + Guid.NewGuid().ToString("N")];
        var aguardando = "o-" + Guid.NewGuid().ToString("N");
        var cancelado = "o-" + Guid.NewGuid().ToString("N");
        var quando = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeMilliseconds();
        object Ordem(string id, string status, decimal preco) => new
        {
            id, clientId = cliente, clientSnapshot = new { name = "Cliente " + cliente },
            items = new[] { new { productId = produto, name = "Produto " + produto, qty = 1, unitPrice = preco } },
            total = preco, status, createdAt = quando, updatedAt = quando
        };
        var estado = new
        {
            products = new[] { new { id = produto, name = "Produto " + produto, category = "massa", stock = 7, price = 10m, unit = "un" } },
            clients = new[] { new { id = cliente, name = "Cliente " + cliente, apt = "62", address = "Endereco", phone = "11999999999", lastOrder = quando, orderCount = 5 } },
            orders = new[] { Ordem(entregues[0], "entregue", 30m), Ordem(entregues[1], "entregue", 45m), Ordem(entregues[2], "entregue", 12.5m),
                Ordem(aguardando, "aguardando", 22m), Ordem(cancelado, "cancelado", 18m) },
            batches = Array.Empty<object>(), cashEntries = Array.Empty<object>(), cashClosings = Array.Empty<object>()
        };
        var json = JsonSerializer.Serialize(estado, Json);
        var request = new LegacySnapshotRequest(Guid.NewGuid(), json, Sha(json), ["mut_" + Guid.NewGuid()], true, empresa, loja, "Operacao");
        var contasAntes = await ReadAsync(empresa, async (db, _) => await db.Set<ContaReceber>().CountAsync(c => c.EmpresaId == empresa));

        var response = await fixture.CreateMobileClient(device.ApiKey).PostAsJsonAsync("/api/mobile/legacy-import", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        await ReadAsync(empresa, async (db, saldos) =>
        {
            var pedidos = await db.Set<Pedido>().ToListAsync();
            using var escopo = new AssertionScope();
            pedidos.Should().HaveCount(5);
            foreach (var id in entregues)
            {
                var p = pedidos.Single(x => x.MobileOrderId == id);
                p.Origem.Should().Be("mobile-import");
                p.QuitadoHistoricoEm.Should().NotBeNull($"o entregue {id} do histórico entra quitado, pela marca");
                p.QuitadoHistoricoEm.Should().Be(p.EntreguEm, "a marca guarda a data original da entrega, nunca a de hoje");
                (await AReceberDoPedidoMobile.CalcularAsync(p, saldos, CancellationToken.None))
                    .Should().Be(0m, $"o entregue {id} do histórico não pode ficar a receber");
            }
            var ativo = pedidos.Single(x => x.MobileOrderId == aguardando);
            ativo.Origem.Should().Be("mobile");
            ativo.QuitadoHistoricoEm.Should().BeNull("o aguardando segue exatamente como antes");
            (await AReceberDoPedidoMobile.CalcularAsync(ativo, saldos, CancellationToken.None)).Should().Be(22m);
            var cancelado2 = pedidos.Single(x => x.MobileOrderId == cancelado);
            cancelado2.Origem.Should().Be("mobile-import");
            cancelado2.QuitadoHistoricoEm.Should().BeNull("o cancelado segue exatamente como antes");

            (await db.Set<PedidoPagamento>().Where(pg => db.Set<Pedido>().Any(o => o.Id == pg.PedidoId && o.EmpresaId == empresa)).CountAsync())
                .Should().Be(0, "nenhum pagamento é criado");
            (await db.Set<MovimentoCaixa>().CountAsync(m => m.EmpresaId == empresa)).Should().Be(0, "nenhum movimento de caixa por pedido do histórico");
            (await db.Set<ContaReceber>().CountAsync(c => c.EmpresaId == empresa)).Should().Be(contasAntes, "nenhuma conta a receber nova");
            (await db.Set<MovimentacaoEstoque>().CountAsync(m => m.EmpresaId == empresa && m.Natureza == NaturezaMovimentacaoEstoque.Venda))
                .Should().Be(0, "o import não grava saída de estoque para o pedido do histórico");
            return 0;
        });
    }

    private async Task<T> ReadAsync<T>(Guid empresa, Func<EasyStockDbContext, SaldoFinanceiroPedido?, Task<T>> action)
    {
        using var scope = fixture.Factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(empresa);
        return await action(db, scope.ServiceProvider.GetService<SaldoFinanceiroPedido>());
    }

    private static string Sha(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
