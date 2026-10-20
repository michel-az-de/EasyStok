using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #1630-1: a Venda do pedido entregue pelo PWA nascia sem forma de pagamento, enquanto o <c>PedidoPagamento</c>
/// do mesmo pedido já levava a forma escolhida no aparelho (#1493). O relatório de vendas não sabia como entrou.
/// </summary>
public sealed class MobileSaleSyncFormaPagamentoTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _produtoErpId = Guid.NewGuid();

    public MobileSaleSyncFormaPagamentoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"venda-forma-{Guid.NewGuid()}").Options,
            currentUser);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("pix", "pix")]
    [InlineData("credito", "credito")]
    [InlineData(null, "dinheiro")]
    [InlineData("vale", "outro")]
    public async Task VendaDoPedidoEntregueNasceComAFormaDoPedido(string? metodoDoAparelho, string formaEsperada)
    {
        _db.Add(new Produto
        {
            Id = _produtoErpId, EmpresaId = _empresaId, Nome = "Lasanha",
            Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CategoriaId = Guid.NewGuid()
        });
        _db.Add(new Product
        {
            Id = "p-lasanha", Name = "Lasanha", Category = "massa", Stock = 5,
            LastDeviceId = "dev-a", EmpresaId = _empresaId, ErpProductId = _produtoErpId
        });
        var order = new Order
        {
            Id = "o-forma", ClientSnapshotName = "Ana", Total = 30m, Status = "entregue",
            EmpresaId = _empresaId, Metodo = metodoDoAparelho, LastDeviceId = "dev-a"
        };
        _db.Add(order);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var criou = await new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance)
            .CreateVendaForDeliveredOrderAsync(order, [new OrderItemDto("p-lasanha", "Lasanha", null, null, 1, 30m)]);
        await _db.SaveChangesAsync();

        criou.Should().BeTrue();
        var venda = await _db.Set<Venda>().IgnoreQueryFilters().SingleAsync();
        venda.FormaPagamentoPrincipal.Should().Be(formaEsperada);
    }
}
