using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2083: o pedido do console com 1,5 kg descia para o aparelho com qty 1, porque o pull fazia
/// <c>(int)i.Quantidade</c>. Agora a quantidade desce como está, e o inteiro sai como inteiro.
/// </summary>
public sealed class SyncReversePullQtyFracionadaTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly SyncReversePullService _servico;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncReversePullQtyFracionadaTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"reverse-pull-qty-{Guid.NewGuid()}")
                .Options,
            currentUser);
        _servico = new SyncReversePullService(
            _db, new ConfigurationBuilder().Build(), NullLogger<SyncReversePullService>.Instance,
            new NumeroCurtoDosLotesMobile(_db, Substitute.For<EasyStock.Application.Ports.Output.Persistence.ILoteNumeroCurtoRepository>(), NullLogger<NumeroCurtoDosLotesMobile>.Instance));
    }

    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("0.250", "0.25")]
    [InlineData("2", "2")]
    [InlineData("2.000", "2")]
    public async Task PedidoDoConsole_DesceAQuantidadeSemTruncar(string quantidade, string jsonEsperado)
    {
        var pedido = Pedido.Criar(_empresaId, cliente: null, lojaId: null, "console");
        var qtd = decimal.Parse(quantidade, System.Globalization.CultureInfo.InvariantCulture);
        pedido.Itens.Add(new PedidoItem
        {
            Id = Guid.NewGuid(), PedidoId = pedido.Id, ProdutoId = Guid.NewGuid(), Nome = "Queijo", Quantidade = qtd,
            PrecoUnitario = 30m, Subtotal = 30m * qtd, CriadoEm = DateTime.UtcNow
        });
        pedido.RecalcularTotal();
        _db.Add(pedido);
        await _db.SaveChangesAsync();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        var item = mutations.Should().ContainSingle(m => m.Type == "order.upsert").Subject
            .Payload.GetProperty("items")[0];
        item.GetProperty("qty").GetRawText().Should().Be(jsonEsperado,
            "1,5 kg não pode descer como 1, e o inteiro continua saindo como inteiro para o aparelho");
    }

    public void Dispose() => _db.Dispose();
}
