using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #2307: pedido quitado no console ou no Mercado Pago chegava ao app sem nenhuma informacao de
/// pagamento, e o card seguia oferecendo "Preparar cobranca". O pull agora manda quanto falta receber.
/// </summary>
public sealed class SyncPullAReceberTests : IDisposable
{
    private readonly EasyStockDbContext _db;
    private readonly SyncReversePullService _servico;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncPullAReceberTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"pull-a-receber-{Guid.NewGuid()}")
                .Options,
            currentUser);
        _servico = new SyncReversePullService(
            _db, new ConfigurationBuilder().Build(), NullLogger<SyncReversePullService>.Instance,
            new NumeroCurtoDosLotesMobile(_db, Substitute.For<EasyStock.Application.Ports.Output.Persistence.ILoteNumeroCurtoRepository>(), NullLogger<NumeroCurtoDosLotesMobile>.Instance));
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData(45.90, 45.90, 0)]
    [InlineData(50, 25, 25)]
    [InlineData(31.41, 0, 31.41)]
    public async Task PedidoDoErp_ChegaComOQueFaltaReceber(decimal total, decimal pago, decimal esperado)
    {
        var pedido = Pedido.Criar(_empresaId);
        pedido.AlteradoEm = DateTime.UtcNow;
        pedido.Total = Dinheiro.FromDecimal(total);
        if (pago > 0)
            pedido.Pagamentos.Add(new PedidoPagamento
            {
                Id = Guid.NewGuid(), PedidoId = pedido.Id, Metodo = "pix", Valor = pago, PagoEm = DateTime.UtcNow,
            });
        _db.Add(pedido);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var mutations = new List<MutationDto>();
        await _servico.AppendAsync(mutations, DateTime.UtcNow.AddDays(-1), _empresaId, null);

        mutations.Should().ContainSingle(m => m.Type == "order.upsert")
            .Which.Payload.GetProperty("aReceber").GetDecimal().Should().Be(esperado);
    }
}
