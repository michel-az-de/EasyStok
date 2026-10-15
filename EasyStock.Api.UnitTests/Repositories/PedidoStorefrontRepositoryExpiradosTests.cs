using EasyStock.Application.UseCases.CriarPedido;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories.Storefront;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.UnitTests.Repositories;

/// <summary>
/// #1291: o <c>CancelarPedidosAbandonadosBackgroundService</c> libera a vaga do pedido que ficou em
/// <c>AguardandoPagamento</c> sem cobrança (o Mercado Pago caiu na fase 3). A varredura olhava só
/// <c>storefront</c>; o guest do site e o pedido da conversa ficavam com a vaga presa para sempre.
/// A query usa IgnoreQueryFilters (job cross-tenant), então o teste independe de contexto.
/// </summary>
public class PedidoStorefrontRepositoryExpiradosTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Limite = Agora.AddMinutes(-30);

    private readonly EasyStockDbContext _db;
    private readonly PedidoStorefrontRepository _repo;

    public PedidoStorefrontRepositoryExpiradosTests()
    {
        _db = new EasyStockDbContext(new DbContextOptionsBuilder<EasyStockDbContext>()
            .UseInMemoryDatabase($"pedido-storefront-expirados-{Guid.NewGuid()}")
            .Options);
        _repo = new PedidoStorefrontRepository(_db);
    }

    [Fact]
    public async Task PegaAsTresOrigensDoCheckoutSemCobranca()
    {
        var site = Seed(OrigemPedido.Storefront);
        var guest = Seed(OrigemPedido.StorefrontGuest);
        var conversa = Seed(OrigemPedido.WhatsApp);
        var balcao = Seed("web");
        var comCobranca = Seed(OrigemPedido.StorefrontGuest);
        var recente = Seed(OrigemPedido.WhatsApp, criadoEm: Agora.AddMinutes(-5));
        var pago = Seed(OrigemPedido.Storefront, status: StatusPedidoMapper.Aguardando);
        _db.CobrancasPedido.Add(CobrancaPedido.CriarOnline(
            comCobranca.EmpresaId, comCobranca.Id, 25m, "pref-1", "https://mp.test/pref-1",
            Agora.AddMinutes(30), 1, Agora.AddMinutes(-40)));
        await _db.SaveChangesAsync();

        var ids = (await _repo.GetAguardandoPagamentoExpiradosAsync(Limite)).Select(p => p.Id).ToList();

        ids.Should().BeEquivalentTo(new[] { site.Id, guest.Id, conversa.Id });
        ids.Should().NotContain(new[] { balcao.Id, comCobranca.Id, recente.Id, pago.Id });
    }

    private Pedido Seed(string origem, string status = StatusPedidoMapper.AguardandoPagamento, DateTime? criadoEm = null)
    {
        var pedido = Pedido.Criar(Guid.NewGuid(), origem: origem);
        pedido.Status = status;
        pedido.CriadoEm = criadoEm ?? Agora.AddMinutes(-40);
        _db.Pedidos.Add(pedido);
        return pedido;
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
