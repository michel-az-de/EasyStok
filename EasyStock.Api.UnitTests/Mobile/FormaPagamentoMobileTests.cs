using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// Regressao #1493: pedido entregue e lancamento de caixa do PWA viravam sempre
/// "dinheiro" no ERP, inflando a gaveta no Caixa do dia com Pix e cartao. Reproduz
/// o caminho do SyncController: dispatcher, SaveChanges, linker.
/// </summary>
public sealed class FormaPagamentoMobileTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly EasyStockDbContext _db;
    private readonly SyncMutationDispatcher _dispatcher;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();

    public FormaPagamentoMobileTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"forma-pagamento-{Guid.NewGuid()}")
                .Options,
            currentUser);

        var produtoRepo = Substitute.For<IProdutoRepository>();
        produtoRepo.GetTipoEmbalagemMapAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, TipoEmbalagem>());

        _dispatcher = new SyncMutationDispatcher(
            _db, Reconciler(),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            produtoRepo,
            NullLogger<SyncMutationDispatcher>.Instance);
    }

    private MobileStockReconciler Reconciler() => new(
        _db, new MobileSystemUserResolver(_db), NullLogger<MobileStockReconciler>.Instance);

    private async Task AplicarAsync(string tipo, object dto)
    {
        var mutation = new MutationDto(
            $"m-{Guid.NewGuid():N}", "dev-1", tipo,
            JsonSerializer.SerializeToElement(dto, Json),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await _dispatcher.ApplyMutationAsync(mutation, "dev-1", "Thati", _empresaId, _lojaId);
        await _db.SaveChangesAsync();
    }

    private static long Agora() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ---------- lancamento de caixa ----------

    private async Task<MovimentoCaixa> LancarAsync(string id, string? metodo)
    {
        await AplicarAsync("cashEntry.upsert",
            new CashEntryDto(id, "income", 40m, "venda por fora", Agora(), Metodo: metodo));
        await new CashEntryLinker(_db, NullLogger<CashEntryLinker>.Instance).ExecuteAsync([id], _empresaId);
        return _db.Set<MovimentoCaixa>().IgnoreQueryFilters().Single(m => m.Referencia == $"mobile:{id}");
    }

    [Theory]
    [InlineData("pix")]
    [InlineData("credito")]
    [InlineData("debito")]
    [InlineData("transferencia")]
    [InlineData("outro")]
    [InlineData("dinheiro")]
    public async Task LancamentoDeCaixa_GravaAFormaInformadaPeloPwa(string metodo)
    {
        var mov = await LancarAsync("cash-1", metodo);

        mov.Metodo.Should().Be(metodo);
        _db.Set<CashEntry>().IgnoreQueryFilters().Single(c => c.Id == "cash-1").Metodo.Should().Be(metodo);
    }

    [Fact]
    public async Task LancamentoDeCaixa_SemForma_ContinuaDinheiro()
    {
        var mov = await LancarAsync("cash-antigo", null);

        mov.Metodo.Should().Be("dinheiro", "aparelho antigo nao manda a forma");
    }

    [Fact]
    public async Task LancamentoDeCaixa_FormaDesconhecida_ViraOutroENaoEntraNaGaveta()
    {
        var mov = await LancarAsync("cash-x", "bitcoin");

        mov.Metodo.Should().Be("outro");
    }

    [Fact]
    public async Task LancamentoDeCaixa_FormaComCaixaEEspaco_EhNormalizada()
    {
        var mov = await LancarAsync("cash-pix", "  PIX ");

        mov.Metodo.Should().Be("pix");
    }

    // ---------- pedido entregue ----------

    private static OrderDto Pedido(string id, string status, string? metodo) => new(
        id, null, new ClientSnapshotDto("Maria", null),
        [new OrderItemDto("p-1", "Bolo", null, "un", 1, 80m)],
        null, 80m, status, Agora(), Agora(), Metodo: metodo);

    private async Task<PedidoPagamento?> EntregarAsync(string id, string? metodo)
    {
        var pedidoErp = EasyStock.Domain.Entities.Pedido.Criar(_empresaId, lojaId: _lojaId, origem: "mobile");
        pedidoErp.Total = Dinheiro.FromDecimal(80m);
        _db.Add(pedidoErp);
        await _db.SaveChangesAsync();

        await AplicarAsync("order.upsert", Pedido(id, "pronto", null));
        await AplicarAsync("order.upsert", Pedido(id, "entregue", metodo));
        var mobile = _db.Set<Order>().IgnoreQueryFilters().Single(o => o.Id == id);
        mobile.ErpPedidoId = pedidoErp.Id;
        await _db.SaveChangesAsync();

        var linker = new OrderLinker(
            _db, Substitute.For<IPedidoRepository>(), Reconciler(),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            null!, NullLogger<OrderLinker>.Instance);
        await linker.ExecuteAsync([id], _empresaId);

        return _db.Set<PedidoPagamento>().IgnoreQueryFilters().SingleOrDefault(p => p.PedidoId == pedidoErp.Id);
    }

    [Fact]
    public async Task PedidoEntregue_GuardaAFormaNoPedidoMobile()
    {
        await AplicarAsync("order.upsert", Pedido("o-1", "entregue", "pix"));

        _db.Set<Order>().IgnoreQueryFilters().Single(o => o.Id == "o-1").Metodo.Should().Be("pix");
    }

    [Fact]
    public async Task PedidoEntregue_PagamentoAutomaticoUsaAFormaDoPwa()
    {
        var pagamento = await EntregarAsync("o-pix", "pix");

        pagamento.Should().NotBeNull();
        pagamento!.Metodo.Should().Be("pix");
        pagamento.Valor.Should().Be(80m);
    }

    [Fact]
    public async Task PedidoEntregue_SemForma_PagamentoContinuaDinheiro()
    {
        var pagamento = await EntregarAsync("o-antigo", null);

        pagamento.Should().NotBeNull();
        pagamento!.Metodo.Should().Be("dinheiro", "aparelho antigo nao manda a forma");
    }

    [Fact]
    public async Task PedidoReenviadoSemForma_NaoApagaAFormaJaGravada()
    {
        await AplicarAsync("order.upsert", Pedido("o-2", "entregue", "credito"));
        await AplicarAsync("order.upsert", Pedido("o-2", "entregue", null));

        _db.Set<Order>().IgnoreQueryFilters().Single(o => o.Id == "o-2").Metodo.Should().Be("credito");
    }

    public void Dispose() => _db.Dispose();
}
