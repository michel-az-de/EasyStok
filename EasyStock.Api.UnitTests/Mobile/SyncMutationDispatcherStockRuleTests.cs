using System.Text.Json;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #1520 (ADR-0060): cada movimento de estoque do PWA e contado uma vez so no espelho
/// <c>mobile_products.Stock</c>. Antes o aparelho mandava o saldo absoluto ja descontado e o
/// servidor descontava de novo na troca de status do pedido (aparelho 9, servidor 8), e somava
/// de novo o lote ja somado. Os cenarios passam pelo <see cref="SyncController"/> para cobrir a
/// ordem de chegada e o reenvio (idempotencia por MutationId).
/// </summary>
public sealed class SyncMutationDispatcherStockRuleTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Produto = "p-lasanha";
    private const string Pedido = "o-1";

    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();
    private readonly Guid _produtoErpId = Guid.NewGuid();
    private readonly DefaultHttpContext _http = new();
    private int _seq;

    public SyncMutationDispatcherStockRuleTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"estoque-uma-vez-{Guid.NewGuid()}")
                .Options,
            currentUser);
    }

    /// <summary>Um controller (e um dispatcher) por lote, como o escopo por requisicao da API.</summary>
    private SyncController Controller()
    {
        var produtoRepo = Substitute.For<IProdutoRepository>();
        produtoRepo.GetTipoEmbalagemMapAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, TipoEmbalagem>());

        var broker = new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance);
        var dispatcher = new SyncMutationDispatcher(
            _db,
            new MobileStockReconciler(_db, new MobileSystemUserResolver(_db), NullLogger<MobileStockReconciler>.Instance),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            broker,
            produtoRepo,
            DispatcherDeTeste.EstornoDeCaixa(_db),
            NullLogger<SyncMutationDispatcher>.Instance);

        // O vinculo com o ERP (linkers) roda depois do SaveChanges e fica fora destes cenarios.
        var semAutoLink = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MobileSync:AutoLink:Product"] = "false", ["MobileSync:AutoLink:Client"] = "false",
            ["MobileSync:AutoLink:Order"] = "false", ["MobileSync:AutoLink:Batch"] = "false",
            ["MobileSync:AutoLink:CashEntry"] = "false"
        }).Build();
        return new SyncController(
            _db, dispatcher,
            new SyncAutoLinker(_db, semAutoLink, NullLogger<SyncAutoLinker>.Instance, null!, null!, null!, null!, null!),
            null!, broker, produtoRepo, semAutoLink, TimeProvider.System,
            NullLogger<SyncController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = _http }
        };
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("pronto", true)]
    [InlineData("saiu_para_entrega", true)]
    [InlineData("entregue", true)]
    [InlineData("preparando", false)]
    [InlineData("aguardando", false)]
    [InlineData("cancelado", false)]
    public void StatusDescontaEstoque(string status, bool esperado)
        => SyncMutationDispatcher.StatusDescontaEstoque(status).Should().Be(esperado);

    // ---------- venda, produto sem vinculo com o ERP ----------

    [Fact]
    public async Task VendaSemVinculo_AparelhoAntigoMandaSaldoAbsoluto_NaoDescontaDeNovo()
    {
        SemearProduto(estoque: 10);
        SemearPedido("preparando", qtd: 1);

        await Push("dev-a", ProdutoAbsoluto(9), PedidoEm("pronto", 1));

        Estoque().Should().Be(9, "o aparelho ja descontou a venda no saldo que mandou");
    }

    [Fact]
    public async Task VendaSemVinculo_SaldoAbsoluto_OrdemInversaDaNoMesmo()
    {
        SemearProduto(estoque: 10);
        SemearPedido("preparando", qtd: 1);

        await Push("dev-a", PedidoEm("pronto", 1), ProdutoAbsoluto(9));

        Estoque().Should().Be(9);
    }

    [Fact]
    public async Task VendaSemVinculo_MovimentoDeEstoque_ContaUmaVez()
    {
        SemearProduto(estoque: 10);
        SemearPedido("preparando", qtd: 1);

        await Push("dev-a", ProdutoPorMovimento(saldoNoAparelho: 9), PedidoEm("pronto", 1), Movimento(-1));

        Estoque().Should().Be(9);
    }

    [Fact]
    public async Task ProdutoPorMovimento_NaoSobrescreveOSaldoDoServidor()
    {
        SemearProduto(estoque: 7);

        await Push("dev-a", ProdutoPorMovimento(saldoNoAparelho: 3));

        Estoque().Should().Be(7, "com movimento de estoque o saldo do cadastro e so informativo");
    }

    // ---------- venda, produto com vinculo com o ERP ----------

    [Fact]
    public async Task VendaComVinculo_BaixaUmaVezNoErpENoEspelho()
    {
        SemearProduto(estoque: 10, comVinculo: true);
        SemearItemEstoque(10);
        SemearPedido("preparando", qtd: 1);

        await Push("dev-a", ProdutoPorMovimento(saldoNoAparelho: 9), PedidoEm("pronto", 1), Movimento(-1));

        Estoque().Should().Be(9);
        SaldoErp().Should().Be(9);
        SaidasDeVendaNoErp().Should().Be(1);
    }

    [Fact]
    public async Task VendaComVinculo_DoisLotesNoErp_EspelhoSegueOAparelho()
    {
        // #1458: cada lote vira um ItemEstoque. A baixa cai em um deles e o saldo desse item
        // (4) era copiado para o espelho do produto, que no aparelho vale 9.
        SemearProduto(estoque: 10, comVinculo: true);
        SemearItemEstoque(5);
        SemearItemEstoque(5);
        SemearPedido("preparando", qtd: 1);

        await Push("dev-a", ProdutoAbsoluto(9), PedidoEm("pronto", 1));

        Estoque().Should().Be(9);
        SaldoErp().Should().Be(9);
        SaidasDeVendaNoErp().Should().Be(1);
    }

    [Fact]
    public async Task CancelarPedidoPronto_DevolveUmaVez()
    {
        SemearProduto(estoque: 9);
        SemearPedido("pronto", qtd: 1);

        await Push("dev-a", ProdutoPorMovimento(saldoNoAparelho: 10), PedidoEm("cancelado", 1), Movimento(+1));

        Estoque().Should().Be(10);
    }

    // ---------- #1543: a baixa do pedido no ERP ----------

    [Fact]
    public async Task VendaComVinculo_BaixaDoLoteQueVencePrimeiro_EAtravessaOsLotes()
    {
        // Antes a baixa caia num lote qualquer (o primeiro que o banco devolvesse) e parava nele.
        SemearProduto(estoque: 8, comVinculo: true);
        var venceDepois = SemearItemEstoque(5, venceEmDias: 10);
        var venceAntes = SemearItemEstoque(3, venceEmDias: 2);
        SemearPedido("preparando", qtd: 4);

        await Push("dev-a", PedidoEm("pronto", 4));

        (SaldoDoLote(venceAntes), SaldoDoLote(venceDepois)).Should().Be((0m, 4m), "sai primeiro do lote que vence antes");
        SaidasDeVendaNoErp().Should().Be(2, "uma saida por lote que baixou");
    }

    [Fact]
    public async Task VendaMaiorQueOSaldo_AFaltaViraDescobertoNoLote()
    {
        // Antes o saldo era truncado em zero e a falta sumia: ficava so um movimento marcador.
        SemearProduto(estoque: 5, comVinculo: true);
        var lote = SemearItemEstoque(2);
        SemearPedido("preparando", qtd: 5);

        await Push("dev-a", PedidoEm("pronto", 5));

        (SaldoDoLote(lote), DescobertoDoLote(lote)).Should().Be((0m, 3m));
        _db.Set<MovimentacaoEstoque>().IgnoreQueryFilters().Should().ContainSingle()
            .Which.Quantidade.Value.Should().Be(5m, "a saida registra o que o pedido levou, inclusive a parte a descoberto");
    }

    [Fact]
    public async Task CancelarPedidoQueNaoBaixouNoErp_NaoDevolveNada()
    {
        // O pedido ja estava "pronto" no servidor sem nenhuma saida no ERP. Antes o cancelamento somava +1.
        SemearProduto(estoque: 10, comVinculo: true);
        var lote = SemearItemEstoque(10);
        SemearPedido("pronto", qtd: 1);

        await Push("dev-a", PedidoEm("cancelado", 1));

        SaldoDoLote(lote).Should().Be(10m, "nao saiu nada por este pedido: nao ha o que devolver");
        _db.Set<MovimentacaoEstoque>().IgnoreQueryFilters().Should().BeEmpty();
    }

    [Fact]
    public async Task CancelarPedidoQueBaixou_DevolveAosLotesDeOndeSaiu_UmaVez()
    {
        SemearProduto(estoque: 8, comVinculo: true);
        var venceDepois = SemearItemEstoque(5, venceEmDias: 10);
        var venceAntes = SemearItemEstoque(3, venceEmDias: 2);
        SemearPedido("preparando", qtd: 4);
        await Push("dev-a", PedidoEm("pronto", 4));

        await Push("dev-a", PedidoEm("cancelado", 4));
        await Push("dev-a", PedidoEm("cancelado", 4));

        (SaldoDoLote(venceAntes), SaldoDoLote(venceDepois)).Should().Be((3m, 5m));
        var movimentos = _db.Set<MovimentacaoEstoque>().IgnoreQueryFilters().AsNoTracking().ToList();
        movimentos.Count(m => m.Natureza == NaturezaMovimentacaoEstoque.Estorno).Should().Be(2, "um estorno por saida, uma vez so");
        movimentos.Where(m => m.Natureza == NaturezaMovimentacaoEstoque.Venda).Should().OnlyContain(m => m.EstornadaEm != null);
    }

    [Fact]
    public async Task CancelarVendaComDescoberto_AbateODescobertoEmVezDeFabricarSaldo()
    {
        SemearProduto(estoque: 5, comVinculo: true);
        var lote = SemearItemEstoque(2);
        SemearPedido("preparando", qtd: 5);
        await Push("dev-a", PedidoEm("pronto", 5));

        await Push("dev-a", PedidoEm("cancelado", 5));

        (SaldoDoLote(lote), DescobertoDoLote(lote)).Should().Be((2m, 0m), "voltam as 2 que sairam; as 3 a descoberto nunca sairam");
    }

    [Fact]
    public async Task PedidoQueChegaJaPronto_BaixaNoErp_EOReenvioNaoBaixaDeNovo()
    {
        // Antes so a troca de status baixava: pedido que nascia "pronto" no servidor ficava sem saida.
        SemearProduto(estoque: 10, comVinculo: true);
        var lote = SemearItemEstoque(10);

        await Push("dev-a", PedidoEm("pronto", 2));
        await Push("dev-a", PedidoEm("pronto", 2));
        await Push("dev-a", PedidoEm("entregue", 2));

        SaldoDoLote(lote).Should().Be(8m);
        SaidasDeVendaNoErp().Should().Be(1);
    }

    [Fact]
    public async Task VendaNaoTocaOLoteDeOutraLoja()
    {
        SemearProduto(estoque: 5, comVinculo: true);
        var daLoja = SemearItemEstoque(2);
        var deOutraLoja = SemearItemEstoque(9, lojaId: Guid.NewGuid());
        SemearPedido("preparando", qtd: 3);

        await Push("dev-a", PedidoEm("pronto", 3));

        (SaldoDoLote(daLoja), DescobertoDoLote(daLoja), SaldoDoLote(deOutraLoja)).Should().Be((0m, 1m, 9m));
    }

    [Fact]
    public async Task CancelarEReabrirOPedido_BaixaDeNovo()
    {
        SemearProduto(estoque: 10, comVinculo: true);
        var lote = SemearItemEstoque(10);
        SemearPedido("preparando", qtd: 2);
        await Push("dev-a", PedidoEm("pronto", 2));
        await Push("dev-a", PedidoEm("cancelado", 2));

        await Push("dev-a", PedidoEm("pronto", 2));

        SaldoDoLote(lote).Should().Be(8m, "a saida anterior foi estornada; a reabertura e uma saida nova");
    }

    [Fact]
    public async Task PedidoDoErpEcoadoPeloPwa_NaoBaixaPeloCaminhoDoPwa()
    {
        // O pedido nasceu no console e desceu para o aparelho com o id do ERP. O estoque dele e da
        // esteira do ERP (que baixa pelos itens do pedido); baixar aqui tambem contaria duas vezes.
        var pedidoErp = SemearPedidoDoErp();
        SemearProduto(estoque: 10, comVinculo: true);
        var lote = SemearItemEstoque(10);

        await Push("dev-a", PedidoEm("pronto", 2, id: pedidoErp.ToString()));

        SaldoDoLote(lote).Should().Be(10m);
        SaidasDeVendaNoErp().Should().Be(0);
    }

    // ---------- producao e descarte de lote ----------

    [Fact]
    public async Task Producao_AparelhoAntigoMandaSaldoAbsoluto_NaoSomaOLoteDeNovo()
    {
        SemearProduto(estoque: 10);

        await Push("dev-a", ProdutoAbsoluto(15), Lote("b-1", 5));

        Estoque().Should().Be(15, "o aparelho ja somou o lote no saldo que mandou");
    }

    [Fact]
    public async Task Producao_MovimentoDeEstoque_SomaUmaVez()
    {
        SemearProduto(estoque: 10);

        await Push("dev-a", ProdutoPorMovimento(saldoNoAparelho: 15), Lote("b-1", 5), Movimento(+5));

        Estoque().Should().Be(15);
    }

    [Fact]
    public async Task AcrescimoEmLoteExistente_ChegaPeloMovimento()
    {
        // appendProductionToBatch: os itens do lote sao imutaveis no servidor, o acrescimo so
        // chega pelo estoque do produto.
        SemearProduto(estoque: 10);
        await Push("dev-a", ProdutoPorMovimento(15), Lote("b-1", 5), Movimento(+5));

        await Push("dev-a", ProdutoPorMovimento(17), Lote("b-1", 7), Movimento(+2));

        Estoque().Should().Be(17);
    }

    [Fact]
    public async Task DescarteDeLote_BaixaUmaVez()
    {
        SemearProduto(estoque: 10);
        await Push("dev-a", ProdutoPorMovimento(15), Lote("b-1", 5), Movimento(+5));

        await Push("dev-a", ProdutoPorMovimento(10), Lote("b-1", 5, descartado: true), Movimento(-5));

        Estoque().Should().Be(10);
    }

    [Fact]
    public async Task DescarteDeLote_AparelhoAntigo_SaldoAbsolutoVale()
    {
        SemearProduto(estoque: 10);
        await Push("dev-a", ProdutoAbsoluto(15), Lote("b-1", 5));

        await Push("dev-a", ProdutoAbsoluto(10), Lote("b-1", 5, descartado: true));

        Estoque().Should().Be(10);
    }

    // ---------- reenvio e ordem de chegada ----------

    [Fact]
    public async Task ReenvioDoMesmoMovimento_NaoContaDeNovo()
    {
        SemearProduto(estoque: 10);
        var movimento = Movimento(-1);

        var primeira = await Push("dev-a", movimento);
        var segunda = await Push("dev-a", movimento);

        Estoque().Should().Be(9);
        primeira.AcceptedIds.Should().Contain(movimento.Id);
        segunda.AcceptedIds.Should().Contain(movimento.Id, "o reenvio recebe a mesma resposta");
    }

    [Fact]
    public async Task ProdutoNovoNoServidor_NasceComOSaldoDoAparelho_SemRecontarOsMovimentosDoLote()
    {
        // Aparelho tinha 10 e vendeu 1 antes de o servidor conhecer o produto. A fila reordena o
        // cadastro (dedup por id), entao o movimento pode vir na frente dele.
        await Push("dev-a", Movimento(-1), ProdutoPorMovimento(saldoNoAparelho: 9));
        Estoque().Should().Be(9, "o saldo de nascimento ja inclui o movimento do mesmo lote");

        await Push("dev-a", ProdutoPorMovimento(saldoNoAparelho: 8), Movimento(-1));
        Estoque().Should().Be(8, "dali em diante cada movimento conta");
    }

    [Fact]
    public async Task SincronizarTudoEVendaNoMesmoLote_AbsolutoDefineEMovimentoConta()
    {
        // "Sincronizar tudo" (saldo absoluto 10) e, antes do envio, uma venda.
        await Push("dev-a", ProdutoAbsoluto(10), ProdutoPorMovimento(saldoNoAparelho: 9), Movimento(-1));

        Estoque().Should().Be(9);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DoisAparelhosVendemOMesmoProduto_QualquerOrdem_ContaOsDois(bool aPrimeiro)
    {
        SemearProduto(estoque: 10);
        var deA = Movimento(-1);
        var deB = Movimento(-2);

        if (aPrimeiro) { await Push("dev-a", deA); await Push("dev-b", deB); }
        else { await Push("dev-b", deB); await Push("dev-a", deA); }

        Estoque().Should().Be(7);
    }

    [Fact]
    public async Task MovimentoDeProdutoQueNaoExiste_RecusaComMotivo()
    {
        var resposta = await Push("dev-a", Movimento(-1));

        resposta.Rejected.Should().ContainSingle()
            .Which.Reason.Should().Contain(Produto);
    }

    // ---------- apoio ----------

    private async Task<SyncPushResponse> Push(string deviceId, params MutationDto[] mutations)
    {
        _http.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice
        {
            Id = deviceId, ApiKeyHash = "h", EmpresaId = _empresaId, LojaId = _lojaId
        };
        var resultado = await Controller().Push(new SyncPushRequest(deviceId, [.. mutations], "Thati"));
        _db.ChangeTracker.Clear();
        return (resultado.Result as OkObjectResult)!.Value.Should().BeOfType<SyncPushResponse>().Subject;
    }

    private MutationDto Mutation(string tipo, object payload) => new(
        $"mut_{++_seq}_{Guid.NewGuid():N}", "dev", tipo,
        JsonSerializer.SerializeToElement(payload, Json),
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>Aparelho antigo: o cadastro leva o saldo absoluto e o servidor grava como veio.</summary>
    private MutationDto ProdutoAbsoluto(int saldo) => Mutation("product.upsert",
        new { id = Produto, name = "Lasanha", category = "massa", price = 30m, stock = saldo });

    /// <summary>Aparelho atual: o saldo do cadastro e informativo; quem conta e o movimento.</summary>
    private MutationDto ProdutoPorMovimento(int saldoNoAparelho) => Mutation("product.upsert",
        new { id = Produto, name = "Lasanha", category = "massa", price = 30m, stock = saldoNoAparelho, stockByDelta = true });

    private MutationDto Movimento(int qtd) => Mutation("stock.delta",
        new { id = $"sd_{Guid.NewGuid():N}", productId = Produto, qty = qtd });

    private MutationDto PedidoEm(string status, int qtd, string id = Pedido)
    {
        var agora = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Mutation("order.upsert", new OrderDto(
            id, null, new ClientSnapshotDto("Ana", null),
            [new OrderItemDto(Produto, "Lasanha", null, "un", qtd, 30m)],
            null, 30m * qtd, status, agora, agora));
    }

    private MutationDto Lote(string id, int qtd, bool descartado = false) => Mutation("batch.upsert", new BatchDto(
        id, "LOT-261010",
        [new BatchItemDto(Produto, "Lasanha", null, "un", qtd, null)],
        null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Discarded: descartado ? true : null,
        DiscardedAt: descartado ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : null));

    private void SemearProduto(int estoque, bool comVinculo = false)
    {
        if (comVinculo)
            _db.Add(new Produto
            {
                Id = _produtoErpId, EmpresaId = _empresaId, Nome = "Lasanha",
                Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CategoriaId = Guid.NewGuid()
            });
        _db.Add(new Product
        {
            Id = Produto, Name = "Lasanha", Category = "massa", Stock = estoque,
            LastDeviceId = "dev-a", EmpresaId = _empresaId, LojaId = _lojaId,
            ErpProductId = comVinculo ? _produtoErpId : null
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private Guid SemearItemEstoque(int quantidade, int? venceEmDias = null, Guid? lojaId = null)
    {
        var produto = _db.Set<Produto>().IgnoreQueryFilters().Single(p => p.Id == _produtoErpId);
        var validade = venceEmDias is { } dias ? Validade.From(DateTime.UtcNow.Date.AddDays(dias)) : null;
        var item = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), _empresaId, produto, null, Quantidade.From(quantidade), Dinheiro.Zero, null,
            DateTime.UtcNow, $"T-{Guid.NewGuid():N}"[..10], null, null, null, null, null, null, null, null, validade, null,
            DateTime.UtcNow);
        item.LojaId = lojaId ?? _lojaId;
        _db.Add(item);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return item.Id;
    }

    /// <summary>Pedido que nasceu no ERP (console ou site), com o id que o pull leva ao aparelho.</summary>
    private Guid SemearPedidoDoErp()
    {
        var pedido = EasyStock.Domain.Entities.Pedido.Criar(_empresaId, cliente: null, lojaId: _lojaId, "web");
        _db.Add(pedido);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return pedido.Id;
    }

    private void SemearPedido(string status, int qtd)
    {
        var pedido = new Order
        {
            Id = Pedido, ClientSnapshotName = "Ana", Status = status, Total = 30m * qtd,
            LastDeviceId = "dev-a", EmpresaId = _empresaId, LojaId = _lojaId
        };
        pedido.Items.Add(new OrderItem { OrderId = Pedido, ProductId = Produto, Name = "Lasanha", Qty = qtd, UnitPrice = 30m });
        _db.Add(pedido);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private int Estoque() => _db.Set<Product>().AsNoTracking().Single(p => p.Id == Produto).Stock;

    private decimal SaldoErp() => _db.Set<ItemEstoque>().IgnoreQueryFilters().AsNoTracking()
        .Where(i => i.EmpresaId == _empresaId && i.ProdutoId == _produtoErpId)
        .AsEnumerable().Sum(i => i.QuantidadeAtual?.Value ?? 0);

    private ItemEstoque LoteDoErp(Guid id) => _db.Set<ItemEstoque>().IgnoreQueryFilters().AsNoTracking().Single(i => i.Id == id);
    private decimal SaldoDoLote(Guid id) => LoteDoErp(id).QuantidadeAtual?.Value ?? 0;
    private decimal DescobertoDoLote(Guid id) => LoteDoErp(id).QuantidadeDescoberta?.Value ?? 0;

    private int SaidasDeVendaNoErp() => _db.Set<MovimentacaoEstoque>().IgnoreQueryFilters()
        .Count(m => m.EmpresaId == _empresaId && m.ProdutoId == _produtoErpId
                    && m.Natureza == NaturezaMovimentacaoEstoque.Venda);
}
