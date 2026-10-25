using System.Reflection;
using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.Producao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// Regressao #1458: um lote do PWA entrava duas vezes no estoque do ERP.
/// O <see cref="SyncMutationDispatcher"/> somava a quantidade no ItemEstoque existente
/// (via <see cref="MobileStockReconciler"/>) e o <see cref="BatchLinker"/> criava outro
/// ItemEstoque com a mesma quantidade. Reproduz o caminho do SyncController:
/// dispatcher, SaveChanges, linker.
/// </summary>
public sealed class LoteMobileEntradaEstoqueTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly EasyStockDbContext _db;
    private readonly SyncMutationDispatcher _dispatcher;
    private readonly BatchLinker _linker;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();
    private readonly Guid _produtoErpId = Guid.NewGuid();
    private const string ProdutoMobileId = "p-massa";

    public LoteMobileEntradaEstoqueTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"lote-mobile-{Guid.NewGuid()}")
                .Options,
            currentUser);

        var produtoRepo = Substitute.For<IProdutoRepository>();
        produtoRepo.GetTipoEmbalagemMapAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, TipoEmbalagem>());

        var reconciler = new MobileStockReconciler(
            _db, new MobileSystemUserResolver(_db), NullLogger<MobileStockReconciler>.Instance);
        var loteEstado = new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance);
        _dispatcher = new SyncMutationDispatcher(
            _db, reconciler, loteEstado,
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            produtoRepo,
            DispatcherDeTeste.EstornoDeCaixa(_db),
            NullLogger<SyncMutationDispatcher>.Instance);
        _linker = new BatchLinker(_db, new LoteRepository(_db), loteEstado, NullLogger<BatchLinker>.Instance);

        _db.Add(new Produto
        {
            Id = _produtoErpId,
            EmpresaId = _empresaId,
            Nome = "Massa de lasanha",
            Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo,
            CategoriaId = Guid.NewGuid()
        });
        _db.Add(new Product
        {
            Id = ProdutoMobileId,
            Name = "Massa de lasanha",
            Category = "massa",
            ErpProductId = _produtoErpId,
            EmpresaId = _empresaId,
            LojaId = _lojaId
        });
        _db.SaveChanges();
    }

    private static BatchDto Lote(string batchId, int qtd) => new(
        batchId, "LOT-261008",
        [new BatchItemDto(ProdutoMobileId, "Massa de lasanha", null, "un", qtd, null)],
        null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private Task SincronizarLoteAsync(string batchId, int qtd) => SincronizarAsync(Lote(batchId, qtd));

    private async Task SincronizarAsync(BatchDto dto)
    {
        await AplicarAsync(dto);
        await _linker.ExecuteAsync([dto.Id], _empresaId);
    }

    /// <summary>O envio do lote, num SaveChanges só, sem o vínculo com o ERP (que vem depois).</summary>
    private async Task AplicarAsync(BatchDto dto)
    {
        var mutation = new MutationDto(
            $"m-{dto.Id}-{Guid.NewGuid():N}", "dev-1", "batch.upsert",
            JsonSerializer.SerializeToElement(dto, Json),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await _dispatcher.ApplyMutationAsync(mutation, "dev-1", "Thati", _empresaId, _lojaId);
        await _db.SaveChangesAsync();
    }

    private decimal SaldoDoProdutoNaLoja() => _db.Set<ItemEstoque>().IgnoreQueryFilters()
        .Where(i => i.EmpresaId == _empresaId && i.ProdutoId == _produtoErpId && i.LojaId == _lojaId)
        .AsEnumerable()
        .Sum(i => i.QuantidadeAtual?.Value ?? 0);

    private int EntradasDeProducao() => _db.Set<MovimentacaoEstoque>().IgnoreQueryFilters()
        .Count(m => m.EmpresaId == _empresaId
                    && m.ProdutoId == _produtoErpId
                    && m.Natureza == NaturezaMovimentacaoEstoque.Producao);

    [Fact]
    public async Task DoisLotesDoMesmoProduto_EntramUmaVezCadaNoEstoqueDoErp()
    {
        await SincronizarLoteAsync("b-1", 5);
        await SincronizarLoteAsync("b-2", 3);

        SaldoDoProdutoNaLoja().Should().Be(8, "5 + 3 produzidos, nenhum contado em dobro");
        EntradasDeProducao().Should().Be(2, "uma entrada de producao por lote");
    }

    private ItemEstoque EntradaDoLote() => _db.Set<ItemEstoque>().IgnoreQueryFilters()
        .Single(i => i.EmpresaId == _empresaId && i.ProdutoId == _produtoErpId && i.CodigoInterno!.StartsWith("lote:"));

    [Fact]
    public async Task EntradaDoLote_NasceComOCodigoDoLote()
    {
        // #2003 (estoque-20): o lote do PWA nascia sem CodigoLote, e aparecia como "sem lote" em "Vencidos com
        // saldo" e no Estoque do dia; a cozinheira não sabia qual lote jogar fora. A chave de pesquisa leva o código.
        await SincronizarLoteAsync("b-1", 5);

        var codigoDoLote = _db.Set<Lote>().IgnoreQueryFilters().Single(l => l.MobileBatchId == "b-1").Codigo.ToUpperInvariant();
        var entrada = EntradaDoLote();
        entrada.CodigoLote!.Value.Should().Be(codigoDoLote).And.StartWith("LOT-261008");
        entrada.ChavePesquisa.Should().Contain(codigoDoLote);
    }

    [Fact]
    public async Task EntradaDoLote_LevaOsLimiaresDoProduto()
    {
        // #2003 (estoque-20): sem os limiares do produto, o lote do PWA ficava com o padrão global.
        var produto = _db.Set<Produto>().IgnoreQueryFilters().Single(p => p.Id == _produtoErpId);
        produto.QuantidadeMinima = 12;
        produto.QuantidadeCritica = 4;
        _db.SaveChanges();

        await SincronizarLoteAsync("b-1", 5);

        var entrada = EntradaDoLote();
        (entrada.QuantidadeMinima, entrada.QuantidadeCritica).Should().Be((12, 4));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoteNaoVinculaProdutoDeOutraEmpresa(bool produtoMobileDeOutraEmpresa)
    {
        var outraEmpresaId = Guid.NewGuid();
        var outroProdutoId = Guid.NewGuid();
        _db.Add(new Produto
        {
            Id = outroProdutoId, EmpresaId = outraEmpresaId, Nome = "Produto de outra empresa",
            Tipo = TipoProduto.Fisico, Status = StatusProduto.Ativo, CategoriaId = Guid.NewGuid()
        });
        _db.Add(new Product
        {
            Id = "p-outra-empresa", Name = "Produto de outra empresa", Category = "massa",
            EmpresaId = produtoMobileDeOutraEmpresa ? outraEmpresaId : _empresaId,
            ErpProductId = outroProdutoId
        });
        await _db.SaveChangesAsync();

        await SincronizarAsync(Lote("b-referencia-alheia", 3) with
        {
            Items = [new BatchItemDto("p-outra-empresa", "Massa", null, "un", 3, null)]
        });

        var lote = await _db.Set<Lote>().IgnoreQueryFilters().Include(l => l.Itens)
            .SingleAsync(l => l.MobileBatchId == "b-referencia-alheia");
        lote.Itens.Should().ContainSingle().Which.ProdutoId.Should().BeNull(
            "nem o produto mobile nem seu vinculo ERP podem pertencer a outra empresa");
        _db.Set<ItemEstoque>().IgnoreQueryFilters().Should().NotContain(i => i.ProdutoId == outroProdutoId);
        _db.Set<MovimentacaoEstoque>().IgnoreQueryFilters().Should().NotContain(m => m.ProdutoId == outroProdutoId);
    }
    private List<MovimentacaoEstoque> Saidas(NaturezaMovimentacaoEstoque natureza) =>
        _db.Set<MovimentacaoEstoque>().IgnoreQueryFilters()
            .Where(m => m.EmpresaId == _empresaId && m.ProdutoId == _produtoErpId
                        && m.Tipo == TipoMovimentacaoEstoque.Saida && m.Natureza == natureza)
            .ToList();

    private string StatusDoLoteErp(string batchId) => _db.Set<Lote>().IgnoreQueryFilters()
        .Single(l => l.MobileBatchId == batchId).Status;

    private static long Agora() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Fact]
    public async Task DescartarLote_BaixaOSaldoRestanteComoPerda()
    {
        await SincronizarLoteAsync("b-1", 5);

        await SincronizarAsync(Lote("b-1", 5) with
        {
            Discarded = true, DiscardedAt = Agora(), DiscardedBy = "Thati", DiscardReason = "vencido"
        });

        SaldoDoProdutoNaLoja().Should().Be(0);
        Saidas(NaturezaMovimentacaoEstoque.Perda).Should().ContainSingle()
            .Which.Quantidade.Value.Should().Be(5);
        StatusDoLoteErp("b-1").Should().Be("descartado");
        _db.Set<Batch>().Single(b => b.Id == "b-1").DiscardReason.Should().Be("vencido");
    }

    [Fact]
    public async Task ReenviarOMesmoDescarte_NaoBaixaDeNovo()
    {
        await SincronizarLoteAsync("b-1", 5);
        var descartado = Lote("b-1", 5) with { Discarded = true, DiscardedAt = Agora() };

        await SincronizarAsync(descartado);
        await SincronizarAsync(descartado);

        Saidas(NaturezaMovimentacaoEstoque.Perda).Should().ContainSingle();
        SaldoDoProdutoNaLoja().Should().Be(0);
    }

    [Fact]
    public async Task DesfazerDescarte_EstornaAPerda()
    {
        await SincronizarLoteAsync("b-1", 5);
        await SincronizarAsync(Lote("b-1", 5) with { Discarded = true, DiscardedAt = Agora() });

        await SincronizarAsync(Lote("b-1", 5));

        SaldoDoProdutoNaLoja().Should().Be(5);
        Saidas(NaturezaMovimentacaoEstoque.Perda).Should().ContainSingle()
            .Which.EstornadaEm.Should().NotBeNull();
        StatusDoLoteErp("b-1").Should().Be("finalizado");
    }

    [Fact]
    public async Task ExcluirERestaurarLote_TiraEDevolveOSaldo()
    {
        await SincronizarLoteAsync("b-1", 5);

        await SincronizarAsync(Lote("b-1", 5) with { Deleted = true, DeletedAt = Agora(), DeletedBy = "Thati" });
        SaldoDoProdutoNaLoja().Should().Be(0);
        Saidas(NaturezaMovimentacaoEstoque.Ajuste).Should().ContainSingle();
        StatusDoLoteErp("b-1").Should().Be("excluido");

        await SincronizarAsync(Lote("b-1", 5));
        SaldoDoProdutoNaLoja().Should().Be(5);
        StatusDoLoteErp("b-1").Should().Be("finalizado");
    }

    [Fact]
    public async Task LoteQueJaChegaExcluido_NaoFicaNoEstoque()
    {
        await SincronizarAsync(Lote("b-1", 5) with { Deleted = true, DeletedAt = Agora() });

        SaldoDoProdutoNaLoja().Should().Be(0);
        StatusDoLoteErp("b-1").Should().Be("excluido");
    }

    [Fact]
    public async Task BaixaDeInsumoQueFalhaNoMeio_NaoDeixaMetadeGravada()
    {
        // #1543: a baixa de insumo do lote mexeu num lote de molho e estourou. O linker engole a falha
        // (o lote e a entrada do prato ficam), mas o proximo SaveChanges gravava a metade que sobrou no
        // contexto: molho baixado sem a saida que o explica.
        var molho = new Produto
        {
            Id = Guid.NewGuid(), EmpresaId = _empresaId, Nome = "Molho", Tipo = TipoProduto.Fisico,
            Status = StatusProduto.Ativo, CategoriaId = Guid.NewGuid()
        };
        var loteDeMolho = ItemEstoque.CriarParaEntrada(
            Guid.NewGuid(), _empresaId, molho, null, Quantidade.From(10), Dinheiro.Zero, null,
            DateTime.UtcNow, "MOLHO-1", null, null, null, null, null, null, null, null, null, null, DateTime.UtcNow);
        _db.AddRange(molho, loteDeMolho);
        _db.SaveChanges();

        var receita = Substitute.For<IProdutoComposicaoRepository>();
        receita.GetByProdutoFinalAsync(_empresaId, _produtoErpId, null, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyCollection<ProdutoComposicao>>(_ =>
            {
                _db.Set<ItemEstoque>().IgnoreQueryFilters().Single(i => i.Id == loteDeMolho.Id)
                    .RegistrarSaida(Quantidade.From(4), DateTime.UtcNow, DateTime.UtcNow);
                throw new InvalidOperationException("a baixa parou no meio");
            });
        var baixaQueFalha = new BaixaDeInsumosDaProducao(
            receita, Substitute.For<IItemEstoqueRepository>(), null!, Substitute.For<IProdutoRepository>());
        var linker = new BatchLinker(_db, new LoteRepository(_db),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            NullLogger<BatchLinker>.Instance, baixaQueFalha);

        await AplicarAsync(Lote("b-1", 5));
        await linker.ExecuteAsync(["b-1"], _empresaId);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        _db.Set<ItemEstoque>().IgnoreQueryFilters().Single(i => i.Id == loteDeMolho.Id).QuantidadeAtual.Value
            .Should().Be(10m, "a baixa falhou: nada dela pode ficar gravado");
        SaldoDoProdutoNaLoja().Should().Be(5, "o lote e a entrada do prato nao dependem da baixa de insumo");
        StatusDoLoteErp("b-1").Should().Be("finalizado");
    }

    [Fact]
    public async Task PullDevolveAsMarcasDoLote()
    {
        await SincronizarLoteAsync("b-1", 5);
        await SincronizarAsync(Lote("b-1", 5) with { Discarded = true, DiscardedAt = Agora(), DiscardReason = "caiu" });

        var batch = _db.Set<Batch>().Include(b => b.Items).Single(b => b.Id == "b-1");
        // SyncDtoConverters e internal: e o mesmo conversor que o pull usa (SyncController).
        var conversor = typeof(SyncMutationDispatcher).Assembly
            .GetType("EasyStock.Api.Mobile.Services.SyncDtoConverters")!;
        var dto = conversor.GetMethod("ToDto", BindingFlags.Static | BindingFlags.NonPublic, [typeof(Batch)])!
            .Invoke(null, [batch])!;
        var json = JsonSerializer.SerializeToElement(dto, Json);

        json.GetProperty("discarded").GetBoolean().Should().BeTrue();
        json.GetProperty("discardReason").GetString().Should().Be("caiu");
    }

    public void Dispose() => _db.Dispose();
}
