using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
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
        _dispatcher = new SyncMutationDispatcher(
            _db, reconciler,
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            produtoRepo,
            NullLogger<SyncMutationDispatcher>.Instance);
        _linker = new BatchLinker(_db, new LoteRepository(_db), NullLogger<BatchLinker>.Instance);

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

    private async Task SincronizarLoteAsync(string batchId, int qtd)
    {
        var dto = new BatchDto(
            batchId, "LOT-261008",
            [new BatchItemDto(ProdutoMobileId, "Massa de lasanha", null, "un", qtd, null)],
            null, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var mutation = new MutationDto(
            $"m-{batchId}", "dev-1", "batch.upsert",
            JsonSerializer.SerializeToElement(dto, Json),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        await _dispatcher.ApplyMutationAsync(mutation, "dev-1", "Thati", _empresaId, _lojaId);
        await _db.SaveChangesAsync();
        await _linker.ExecuteAsync([batchId], _empresaId);
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

    public void Dispose() => _db.Dispose();
}
