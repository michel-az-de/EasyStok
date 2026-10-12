using System.Text.Json;
using System.Text.Json.Nodes;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// Regressao #1509 (achado 1): texto maior que a coluna (VARCHAR) fazia o SaveChanges do lote
/// inteiro falhar com 22001, o sync respondia 500 e o PWA reenviava a mesma fila para sempre.
/// O dispatcher corta os textos livres no tamanho da coluna antes de gravar.
/// O provider InMemory nao aplica o limite, entao o teste confere o valor gravado.
/// </summary>
public sealed class SyncTextoLongoTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly EasyStockDbContext _db;
    private readonly SyncMutationDispatcher _dispatcher;
    private readonly Guid _empresaId = Guid.NewGuid();

    public SyncTextoLongoTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"sync-texto-longo-{Guid.NewGuid()}")
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
    }

    private static string Longo(int n) => new('x', n);

    private async Task AplicarAsync(string tipo, JsonObject payload, string? operador = "Thati")
    {
        var mutation = new MutationDto($"m-{Guid.NewGuid():N}", "dev-1", tipo,
            JsonSerializer.SerializeToElement(payload, Json), 0);
        await _dispatcher.ApplyMutationAsync(mutation, "dev-1", operador, _empresaId, null);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Produto_TextosLongos_CabemNaColuna()
    {
        await AplicarAsync("product.upsert", new JsonObject
        {
            ["id"] = "p-1", ["name"] = Longo(300), ["emoji"] = Longo(40),
            ["category"] = Longo(40), ["unit"] = Longo(80), ["price"] = 10m,
            ["stock"] = 0, ["sku"] = Longo(80)
        }, operador: Longo(200));

        var p = _db.Set<Product>().Single();
        p.Name.Should().HaveLength(120);
        p.Emoji.Should().HaveLength(16);
        p.Category.Should().HaveLength(16);
        p.Unit.Should().HaveLength(32);
        p.Sku.Should().HaveLength(32);
        p.LastOperatorName.Should().HaveLength(64);
    }

    [Fact]
    public async Task Cliente_EnderecoLongo_CabeNaColuna()
    {
        await AplicarAsync("client.upsert", new JsonObject
        {
            ["id"] = "c-1", ["name"] = Longo(300), ["apt"] = Longo(80),
            ["address"] = Longo(600), ["phone"] = Longo(80), ["lastOrder"] = 0, ["orderCount"] = 0
        });

        var c = _db.Set<Client>().Single();
        c.Name.Should().HaveLength(120);
        c.Apt.Should().HaveLength(32);
        c.Address.Should().HaveLength(255);
        c.Phone.Should().HaveLength(32);
    }

    [Fact]
    public async Task Pedido_NomeDoClienteEItensLongos_CabemNaColuna()
    {
        await AplicarAsync("order.upsert", new JsonObject
        {
            ["id"] = "o-1",
            ["clientSnapshot"] = new JsonObject { ["name"] = Longo(300), ["ref"] = Longo(600) },
            ["items"] = new JsonArray(new JsonObject
            {
                ["productId"] = "p-1", ["name"] = Longo(300), ["emoji"] = Longo(40),
                ["unit"] = Longo(80), ["qty"] = 1, ["unitPrice"] = 10m
            }),
            ["total"] = 10m, ["status"] = "aguardando", ["createdAt"] = 0, ["updatedAt"] = 0,
            ["confirmedBy"] = Longo(200)
        });

        var o = _db.Set<Order>().Include(x => x.Items).Single();
        o.ClientSnapshotName.Should().HaveLength(120);
        o.ClientSnapshotRef.Should().HaveLength(255);
        o.ConfirmedBy.Should().HaveLength(64);
        o.Items.Single().Name.Should().HaveLength(120);
        o.Items.Single().Emoji.Should().HaveLength(16);
        o.Items.Single().Unit.Should().HaveLength(32);
    }

    [Fact]
    public async Task LancamentoDeCaixa_DescricaoLonga_CabeNaColuna()
    {
        await AplicarAsync("cashEntry.upsert", new JsonObject
        {
            ["id"] = "cash-1", ["type"] = "expense", ["amount"] = 5m,
            ["description"] = Longo(1000), ["createdAt"] = 0
        });

        _db.Set<CashEntry>().Single().Description.Should().HaveLength(255);
    }

    [Fact]
    public async Task Lote_CodigoEItensLongos_CabemNaColuna()
    {
        await AplicarAsync("batch.upsert", new JsonObject
        {
            ["id"] = "b-1", ["code"] = Longo(80), ["lote"] = Longo(80), ["createdAt"] = 0,
            ["items"] = new JsonArray(new JsonObject
            {
                ["productId"] = "p-1", ["name"] = Longo(300), ["emoji"] = Longo(40),
                ["unit"] = Longo(80), ["qty"] = 1
            })
        });

        var b = _db.Set<Batch>().Include(x => x.Items).Single();
        b.Code.Should().HaveLength(32);
        b.Lote.Should().HaveLength(32);
        b.Items.Single().Name.Should().HaveLength(120);
        b.Items.Single().Emoji.Should().HaveLength(16);
        b.Items.Single().Unit.Should().HaveLength(32);
    }

    [Fact]
    public async Task Corte_NaoParteEmojiNoMeio()
    {
        // 254 letras + emoji (par substituto): cortar em 255 deixaria meio emoji, que o
        // Npgsql recusa ao codificar em UTF-8.
        await AplicarAsync("cashEntry.upsert", new JsonObject
        {
            ["id"] = "cash-2", ["type"] = "income", ["amount"] = 5m,
            ["description"] = Longo(254) + "\U0001F35D" + Longo(10), ["createdAt"] = 0
        });

        var d = _db.Set<CashEntry>().Single().Description;
        d.Length.Should().BeLessThanOrEqualTo(255);
        char.IsHighSurrogate(d[^1]).Should().BeFalse("meio emoji vira texto invalido");
    }

    public void Dispose() => _db.Dispose();
}
