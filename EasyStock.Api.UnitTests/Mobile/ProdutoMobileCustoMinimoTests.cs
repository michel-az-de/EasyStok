using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Mobile.Services.Linkers;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// Regressao #1467: o PWA manda <c>cost</c> e <c>minStock</c> no product.upsert, o servidor
/// descartava e o pull apagava os dois no aparelho. Agora persistem, voltam no pull e o
/// produto vinculado espelha no Produto ERP (CustoReferencia, QuantidadeMinima).
/// </summary>
public sealed class ProdutoMobileCustoMinimoTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly EasyStockDbContext _db;
    private readonly SyncMutationDispatcher _dispatcher;
    private readonly ProductLinker _linker;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();

    public ProdutoMobileCustoMinimoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"produto-mobile-{Guid.NewGuid()}")
                .Options,
            currentUser);

        var sysUser = new MobileSystemUserResolver(_db);
        _dispatcher = new SyncMutationDispatcher(
            _db,
            new MobileStockReconciler(_db, sysUser, NullLogger<MobileStockReconciler>.Instance),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            Substitute.For<IProdutoRepository>(),
            DispatcherDeTeste.EstornoDeCaixa(_db),
            NullLogger<SyncMutationDispatcher>.Instance);
        _linker = new ProductLinker(_db, sysUser, NullLogger<ProductLinker>.Instance);
    }

    private async Task PushAsync(JsonObject produto)
    {
        var mutation = new MutationDto(
            $"m-{Guid.NewGuid():N}", "dev-1", "product.upsert",
            JsonSerializer.SerializeToElement(produto, Json), 0);
        await _dispatcher.ApplyMutationAsync(mutation, "dev-1", "Thati", _empresaId, _lojaId);
        await _db.SaveChangesAsync();
        await _linker.ExecuteAsync([(string)produto["id"]!], _empresaId);
        await _db.SaveChangesAsync();
    }

    private static JsonObject Produto(decimal? custo = 7.5m, int? minimo = 4) => new()
    {
        ["id"] = "p-molho",
        ["name"] = "Molho de tomate",
        ["category"] = "molho",
        ["unit"] = "pote",
        ["price"] = 18m,
        ["stock"] = 10,
        ["custom"] = true,
        ["cost"] = custo,
        ["minStock"] = minimo
    };

    private Product Mobile() => _db.Set<Product>().Single(p => p.Id == "p-molho");

    private Produto Erp() => _db.Set<Produto>().IgnoreQueryFilters().Single(p => p.Id == Mobile().ErpProductId);

    [Fact]
    public async Task CustoEMinimo_PersistemEVoltamNoPull()
    {
        await PushAsync(Produto());

        Mobile().Cost.Should().Be(7.5m);
        Mobile().MinStock.Should().Be(4);

        // Mesmo conversor do pull (SyncController); SyncDtoConverters e internal.
        var conversor = typeof(SyncMutationDispatcher).Assembly
            .GetType("EasyStock.Api.Mobile.Services.SyncDtoConverters")!;
        var dto = conversor.GetMethod("ToDto", BindingFlags.Static | BindingFlags.NonPublic, [typeof(Product)])!
            .Invoke(null, [Mobile()])!;
        var json = JsonSerializer.SerializeToElement(dto, Json);
        json.GetProperty("cost").GetDecimal().Should().Be(7.5m);
        json.GetProperty("minStock").GetInt32().Should().Be(4);
    }

    [Fact]
    public async Task ProdutoCriadoNoErp_LevaCustoEMinimo()
    {
        await PushAsync(Produto());

        Erp().CustoReferencia!.Valor.Should().Be(7.5m);
        Erp().QuantidadeMinima.Should().Be(4);
    }

    [Fact]
    public async Task EditarProdutoVinculado_AtualizaOErp()
    {
        await PushAsync(Produto());

        await PushAsync(Produto(custo: 9m, minimo: 6));

        Erp().CustoReferencia!.Valor.Should().Be(9m);
        Erp().QuantidadeMinima.Should().Be(6);
    }

    [Fact]
    public async Task PayloadSemOsCampos_NaoApagaOQueJaExiste()
    {
        await PushAsync(Produto());

        var semCampos = Produto();
        semCampos.Remove("cost");
        semCampos.Remove("minStock");
        await PushAsync(semCampos);

        Mobile().Cost.Should().Be(7.5m);
        Mobile().MinStock.Should().Be(4);
    }

    public void Dispose() => _db.Dispose();
}
