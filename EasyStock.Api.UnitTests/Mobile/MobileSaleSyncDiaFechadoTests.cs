using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Common;
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
/// #1621: a Venda do pedido entregue pelo PWA entra no caixa (o pagamento do pedido com venda sai da soma), mas
/// nascia com a hora do aparelho. Sincronizada depois de o console fechar o dia, caía no dia fechado, fora do
/// retrato. Segue a regra do lançamento e do pagamento (#2030): dia fechado, vale o instante do registro.
/// </summary>
public sealed class MobileSaleSyncDiaFechadoTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _produtoErpId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();
    private static readonly DateOnly Ontem = HorarioBrasil.Hoje().AddDays(-1);
    // 20h de ontem em Brasília (23h UTC): o aparelho entregou offline antes do fechamento.
    private static readonly DateTime EntregueNoAparelho = Ontem.ToDateTime(new TimeOnly(23, 0), DateTimeKind.Utc);

    public MobileSaleSyncDiaFechadoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase($"venda-dia-fechado-{Guid.NewGuid()}").Options,
            currentUser);
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(true)]   // fechamento da própria loja
    [InlineData(false)]  // fechamento da empresa inteira, que retrata a loja
    public async Task Venda_sincronizada_depois_do_fechamento_entra_no_instante_do_registro(bool fechamentoDaLoja)
    {
        _db.Add(FechamentoCaixa.Criar(_empresaId, Ontem, 0m, 0m, 0m, 0m, 0m, lojaId: fechamentoDaLoja ? _lojaId : null));
        var antes = DateTime.UtcNow;

        var venda = await SincronizarAsync();

        venda.DataVenda.Should().BeOnOrAfter(antes, "o dia do aparelho já foi fechado");
    }

    [Fact]
    public async Task Venda_de_dia_aberto_mantem_a_hora_do_aparelho()
    {
        var venda = await SincronizarAsync();

        venda.DataVenda.Should().Be(EntregueNoAparelho);
    }

    private async Task<Venda> SincronizarAsync()
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
            Id = "o-tarde", ClientSnapshotName = "Ana", Total = 30m, Status = "entregue",
            EmpresaId = _empresaId, LojaId = _lojaId, Metodo = "dinheiro", LastDeviceId = "dev-a",
            UpdatedAt = EntregueNoAparelho
        };
        _db.Add(order);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var criou = await new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance)
            .CreateVendaForDeliveredOrderAsync(order, [new OrderItemDto("p-lasanha", "Lasanha", null, null, 1, 30m)]);
        await _db.SaveChangesAsync();

        criou.Should().BeTrue();
        return await _db.Set<Venda>().IgnoreQueryFilters().SingleAsync();
    }
}
