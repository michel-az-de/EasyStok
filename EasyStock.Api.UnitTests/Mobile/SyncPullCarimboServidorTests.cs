using System.Text.Json;
using EasyStock.Api.Mobile.Controllers;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Security;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Data.Interceptors;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #1520 (ADR-0060): o pull filtrava pela hora do APARELHO (UpdatedAt do pedido, CreatedAt do
/// lote e do lancamento). Edicao feita offline com hora antiga ficava atras do cursor dos
/// outros aparelhos e nunca chegava; relogio adiantado gerava conflito falso. Agora o filtro,
/// o cursor e a deteccao de conflito usam o carimbo do servidor (server_updated_at), gravado
/// em toda insercao e alteracao pelo AuditTimestampsInterceptor.
/// </summary>
public sealed class SyncPullCarimboServidorTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly InMemoryDatabaseRoot _raiz = new();
    private readonly string _banco = $"carimbo-servidor-{Guid.NewGuid()}";
    private readonly EasyStockDbContext _db;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();
    private readonly DateTime _ontem = DateTime.UtcNow.AddDays(-1);
    private int _seq;

    public SyncPullCarimboServidorTests() => _db = Contexto(comCarimbo: true);

    public void Dispose() => _db.Dispose();

    // ---------- o pull enxerga o que mudou no servidor, nao a hora do aparelho ----------

    [Fact]
    public async Task PedidoEditadoOfflineComHoraAntiga_ChegaAoOutroAparelho()
    {
        var cursorDoOutro = HaCincoMinutos();

        await Push("dev-a", Pedido("o-1", "preparando", horaDoAparelho: _ontem), tsDoAparelho: _ontem);

        var recebido = await Pull("dev-b", cursorDoOutro);
        recebido.Should().ContainSingle(m => m.Type == "order.upsert" && Id(m) == "o-1",
            "o que conta e quando o servidor recebeu, nao a hora do aparelho que editou");
    }

    [Fact]
    public async Task LoteDescartadoDepois_ChegaAoOutroAparelho()
    {
        Semear(new Batch { Id = "b-1", Code = "LOT-1", CreatedAt = _ontem, ServerUpdatedAt = _ontem, LastDeviceId = "dev-a", EmpresaId = _empresaId, LojaId = _lojaId });
        var cursorDoOutro = HaCincoMinutos();

        await Push("dev-a", Mutation("batch.upsert", new BatchDto(
            "b-1", "LOT-1", [], null, Ms(_ontem), Discarded: true, DiscardedAt: Ms(DateTime.UtcNow))));

        var recebido = await Pull("dev-b", cursorDoOutro);
        recebido.Should().ContainSingle(m => m.Type == "batch.upsert" && Id(m) == "b-1")
            .Which.Payload.GetProperty("discarded").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task LancamentoEditadoDepois_ChegaAoOutroAparelho()
    {
        Semear(new CashEntry { Id = "cash-1", Type = "expense", Amount = 30m, Description = "Gas", CreatedAt = _ontem, ServerUpdatedAt = _ontem, LastDeviceId = "dev-a", EmpresaId = _empresaId, LojaId = _lojaId });
        var cursorDoOutro = HaCincoMinutos();

        await Push("dev-a", Mutation("cashEntry.upsert", new CashEntryDto("cash-1", "expense", 45m, "Gas", Ms(_ontem))));

        var recebido = await Pull("dev-b", cursorDoOutro);
        recebido.Should().ContainSingle(m => m.Type == "cashEntry.upsert" && Id(m) == "cash-1")
            .Which.Payload.GetProperty("amount").GetDecimal().Should().Be(45m);
    }

    [Fact]
    public async Task LancamentoExcluidoDepois_ChegaAoOutroAparelhoComoEstornado()
    {
        Semear(new CashEntry { Id = "cash-1", Type = "expense", Amount = 30m, Description = "Gas", CreatedAt = _ontem, ServerUpdatedAt = _ontem, LastDeviceId = "dev-a", EmpresaId = _empresaId, LojaId = _lojaId });
        var cursorDoOutro = HaCincoMinutos();

        await Push("dev-a", Mutation("cashEntry.delete", new { id = "cash-1" }));

        var recebido = await Pull("dev-b", cursorDoOutro);
        recebido.Should().ContainSingle(m => m.Type == "cashEntry.upsert" && Id(m) == "cash-1")
            .Which.Payload.GetProperty("estornado").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task QuemExcluiuOLancamento_NaoRecebeDeVolta_OAparelhoQueCriouRecebe()
    {
        Semear(new CashEntry { Id = "cash-1", Type = "expense", Amount = 30m, Description = "Gas", CreatedAt = _ontem, ServerUpdatedAt = _ontem, LastDeviceId = "dev-b", EmpresaId = _empresaId, LojaId = _lojaId });
        var cursor = HaCincoMinutos();

        await Push("dev-a", Mutation("cashEntry.delete", new { id = "cash-1" }));

        (await Pull("dev-a", cursor)).Should().BeEmpty("o aparelho que excluiu ja tirou o lancamento da tela");
        (await Pull("dev-b", cursor)).Should().ContainSingle(m => Id(m) == "cash-1");
    }

    [Fact]
    public async Task QuemDescartouOLote_NaoRecebeDeVolta_OAparelhoQueCriouRecebe()
    {
        Semear(new Batch { Id = "b-1", Code = "LOT-1", CreatedAt = _ontem, ServerUpdatedAt = _ontem, LastDeviceId = "dev-b", EmpresaId = _empresaId, LojaId = _lojaId });
        var cursor = HaCincoMinutos();

        await Push("dev-a", Mutation("batch.upsert", new BatchDto(
            "b-1", "LOT-1", [], null, Ms(_ontem), Discarded: true, DiscardedAt: Ms(DateTime.UtcNow))));

        (await Pull("dev-a", cursor)).Should().BeEmpty("o aparelho que descartou ja tem a marca");
        (await Pull("dev-b", cursor)).Should().ContainSingle(m => Id(m) == "b-1");
    }

    [Fact]
    public async Task ReenvioDoLoteSemMudanca_NaoTrocaOAutorNemCarimba()
    {
        // "Sincronizar tudo" de outro aparelho reenvia o lote igual: ninguem precisa receber de novo.
        Semear(new Batch { Id = "b-1", Code = "LOT-1", CreatedAt = _ontem, ServerUpdatedAt = _ontem, LastDeviceId = "dev-b", EmpresaId = _empresaId, LojaId = _lojaId });

        await Push("dev-a", Mutation("batch.upsert", new BatchDto("b-1", "LOT-1", [], null, Ms(_ontem))));

        (await Pull("dev-b", HaCincoMinutos())).Should().BeEmpty();
        _db.Set<Batch>().AsNoTracking().Single(b => b.Id == "b-1").LastDeviceId.Should().Be("dev-b");
    }

    [Fact]
    public async Task AlteracaoFeitaForaDoSync_TambemEntraNoPull()
    {
        // Ex.: reconciliacao de estoque pelo painel. O carimbo e do interceptor, nao de quem grava.
        Semear(new Product { Id = "p-1", Name = "Lasanha", Stock = 3, UpdatedAt = _ontem, ServerUpdatedAt = _ontem, LastDeviceId = "dev-a", EmpresaId = _empresaId, LojaId = _lojaId });
        var cursorDoOutro = HaCincoMinutos();

        _db.Set<Product>().Single(p => p.Id == "p-1").Stock = 8;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        (await Pull("dev-b", cursorDoOutro)).Should().ContainSingle(m => Id(m) == "p-1");
    }

    [Fact]
    public async Task RegistroQueNaoMudou_NaoVoltaNoPull()
    {
        Semear(new Product { Id = "p-1", Name = "Lasanha", UpdatedAt = _ontem, ServerUpdatedAt = _ontem, LastDeviceId = "dev-a", EmpresaId = _empresaId, LojaId = _lojaId });

        (await Pull("dev-b", HaCincoMinutos())).Should().BeEmpty();
    }

    [Fact]
    public async Task PullDevolveOCarimboDoServidor_NuncaOQueOAparelhoMandou()
    {
        var antes = DateTime.UtcNow.AddSeconds(-2);

        // O aparelho tenta ditar a hora: pedido "de ontem" e um campo de carimbo no payload.
        await Push("dev-a", Mutation("order.upsert", new
        {
            id = "o-1", clientSnapshot = new { name = "Ana" }, items = Array.Empty<object>(), total = 10m,
            status = "preparando", createdAt = Ms(_ontem), updatedAt = Ms(_ontem), serverUpdatedAt = 0
        }), tsDoAparelho: _ontem);

        var carimbo = _db.Set<Order>().AsNoTracking().Single(o => o.Id == "o-1").ServerUpdatedAt;
        carimbo.Should().BeAfter(antes).And.BeOnOrBefore(DateTime.UtcNow);
        (await Pull("dev-b", 0)).Should().ContainSingle(m => Id(m) == "o-1")
            .Which.Ts.Should().Be(Ms(carimbo), "o ts da mutation do pull e o carimbo do servidor");
    }

    // ---------- conflito compara com o carimbo do servidor ----------

    [Fact]
    public async Task RelogioAdiantadoDeUmAparelho_NaoGeraConflitoFalsoNoOutro()
    {
        var daquiADuasHoras = DateTime.UtcNow.AddHours(2);
        await Push("dev-a", Pedido("o-1", "preparando", horaDoAparelho: daquiADuasHoras), tsDoAparelho: daquiADuasHoras);

        // Aparelho B, relogio certo, edita depois de A ter sincronizado.
        var resposta = await Push("dev-b", Pedido("o-1", "pronto", horaDoAparelho: DateTime.UtcNow.AddSeconds(30)),
            tsDoAparelho: DateTime.UtcNow.AddSeconds(30));

        resposta.Rejected.Should().BeNullOrEmpty("a hora adiantada do aparelho A nao e a hora em que o servidor recebeu");
        _db.Set<Order>().AsNoTracking().Single(o => o.Id == "o-1").Status.Should().Be("pronto");
    }

    [Fact]
    public async Task EdicaoAnteriorAoQueOServidorJaTem_ContinuaConflito_MesmoComRelogioAtrasadoNoOutro()
    {
        // Aparelho A, relogio um dia atrasado, sincroniza agora.
        await Push("dev-a", Pedido("o-1", "pronto", horaDoAparelho: _ontem), tsDoAparelho: _ontem);

        // Aparelho B manda uma edicao feita dez minutos antes de A sincronizar.
        var resposta = await Push("dev-b", Pedido("o-1", "preparando", horaDoAparelho: DateTime.UtcNow.AddMinutes(-10)),
            tsDoAparelho: DateTime.UtcNow.AddMinutes(-10));

        resposta.Rejected.Should().ContainSingle().Which.Reason.Should().StartWith("conflict:");
        _db.Set<Order>().AsNoTracking().Single(o => o.Id == "o-1").Status.Should().Be("pronto");
    }

    // ---------- apoio ----------

    private EasyStockDbContext Contexto(bool comCarimbo)
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);
        var opcoes = new DbContextOptionsBuilder<EasyStockDbContext>().UseInMemoryDatabase(_banco, _raiz);
        // Em producao o interceptor entra pelo AddDbContext (ServiceCollectionExtensions).
        if (comCarimbo) opcoes.AddInterceptors(new AuditTimestampsInterceptor());
        return new EasyStockDbContext(opcoes.Options, currentUser);
    }

    /// <summary>Grava linha "antiga" sem o interceptor, para o carimbo ficar no passado.</summary>
    private void Semear(object entidade)
    {
        using var semCarimbo = Contexto(comCarimbo: false);
        semCarimbo.Add(entidade);
        semCarimbo.SaveChanges();
    }

    private SyncController Controller(string deviceId)
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
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MobileSync:AutoLink:Product"] = "false", ["MobileSync:AutoLink:Client"] = "false",
            ["MobileSync:AutoLink:Order"] = "false", ["MobileSync:AutoLink:Batch"] = "false",
            ["MobileSync:AutoLink:CashEntry"] = "false", ["MobileSync:PullReverse:Enabled"] = "false"
        }).Build();
        var http = new DefaultHttpContext();
        http.Items[MobileAuth.HttpContextItemDevice] = new MobileDevice
        {
            Id = deviceId, ApiKeyHash = "h", EmpresaId = _empresaId, LojaId = _lojaId
        };
        return new SyncController(
            _db, dispatcher,
            new SyncAutoLinker(_db, config, NullLogger<SyncAutoLinker>.Instance, null!, null!, null!, null!, null!),
            null!, broker, produtoRepo, config, TimeProvider.System,
            NullLogger<SyncController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private async Task<SyncPushResponse> Push(string deviceId, MutationDto mutation, DateTime? tsDoAparelho = null)
    {
        if (tsDoAparelho is { } ts) mutation = mutation with { Ts = Ms(ts) };
        var resultado = await Controller(deviceId).Push(new SyncPushRequest(deviceId, [mutation], "Thati"));
        _db.ChangeTracker.Clear();
        return (resultado.Result as OkObjectResult)!.Value.Should().BeOfType<SyncPushResponse>().Subject;
    }

    private async Task<List<MutationDto>> Pull(string deviceId, long since)
    {
        var resultado = await Controller(deviceId).Pull(since, deviceId);
        _db.ChangeTracker.Clear();
        return (resultado.Result as OkObjectResult)!.Value.Should().BeOfType<SyncPullResponse>().Subject.Mutations;
    }

    private MutationDto Mutation(string tipo, object payload) => new(
        $"mut_{++_seq}_{Guid.NewGuid():N}", "dev", tipo,
        JsonSerializer.SerializeToElement(payload, Json), Ms(DateTime.UtcNow));

    private MutationDto Pedido(string id, string status, DateTime horaDoAparelho) => Mutation("order.upsert", new OrderDto(
        id, null, new ClientSnapshotDto("Ana", null), [], null, 10m, status, Ms(horaDoAparelho), Ms(horaDoAparelho)));

    private static string? Id(MutationDto m) => m.Payload.GetProperty("id").GetString();
    private static long Ms(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    private static long HaCincoMinutos() => Ms(DateTime.UtcNow.AddMinutes(-5));
}
