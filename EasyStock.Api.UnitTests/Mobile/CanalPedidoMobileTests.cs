using System.Text.Json;
using EasyStock.Api.Mobile.DTOs;
using EasyStock.Api.Mobile.Services;
using EasyStock.Api.Services.Operacao;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Mobile;

/// <summary>
/// #409: o PWA já marcava o canal do pedido, mas o servidor descartava o campo. Reproduz o caminho do
/// SyncController (dispatcher e SaveChanges) e a volta ao aparelho.
/// </summary>
public sealed class CanalPedidoMobileTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly EasyStockDbContext _db;
    private readonly SyncMutationDispatcher _dispatcher;
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _lojaId = Guid.NewGuid();

    public CanalPedidoMobileTests()
    {
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.IsAuthenticated.Returns(true);
        currentUser.EmpresaId.Returns(_empresaId);

        _db = new EasyStockDbContext(
            new DbContextOptionsBuilder<EasyStockDbContext>()
                .UseInMemoryDatabase($"canal-pedido-{Guid.NewGuid()}")
                .Options,
            currentUser);

        var produtoRepo = Substitute.For<IProdutoRepository>();
        produtoRepo.GetTipoEmbalagemMapAsync(Arg.Any<Guid>(), Arg.Any<IEnumerable<Guid>>())
            .Returns(new Dictionary<Guid, TipoEmbalagem>());

        _dispatcher = new SyncMutationDispatcher(
            _db,
            new MobileStockReconciler(_db, new MobileSystemUserResolver(_db), NullLogger<MobileStockReconciler>.Instance),
            new LoteMobileEstadoReconciler(_db, NullLogger<LoteMobileEstadoReconciler>.Instance),
            new MobileSaleSyncService(_db, NullLogger<MobileSaleSyncService>.Instance),
            new OperacaoEventBroker(NullLogger<OperacaoEventBroker>.Instance),
            produtoRepo,
            DispatcherDeTeste.EstornoDeCaixa(_db),
            NullLogger<SyncMutationDispatcher>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private static long Agora() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static OrderDto Pedido(string id, string? canal, string? notas = null) => new(
        id, null, new ClientSnapshotDto("Cliente de teste", null),
        [new OrderItemDto("p-1", "Massa", null, "un", 1, 80m)],
        notas, 80m, "aguardando", Agora(), Agora(), Channel: canal);

    private async Task AplicarAsync(object dto)
    {
        var mutation = new MutationDto(
            $"m-{Guid.NewGuid():N}", "dev-1", "order.upsert",
            JsonSerializer.SerializeToElement(dto, Json),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        await _dispatcher.ApplyMutationAsync(mutation, "dev-1", "Operadora", _empresaId, _lojaId);
        await _db.SaveChangesAsync();
    }

    private Order Gravado(string id) =>
        _db.Set<Order>().IgnoreQueryFilters().Include(o => o.Items).Single(o => o.Id == id);

    [Theory]
    [InlineData("whatsapp", "whatsapp")]
    [InlineData("balcao", "balcao")]
    [InlineData("  WhatsApp ", "whatsapp")]
    [InlineData("whatsapp-pessoal", "whatsapp-pessoal")]
    [InlineData("whatsapp-loja", "whatsapp-loja")]
    public async Task PedidoNovo_GuardaOCanalMarcadoNoPwa(string enviado, string esperado)
    {
        await AplicarAsync(Pedido("o-1", enviado));

        Gravado("o-1").Channel.Should().Be(esperado);
    }

    [Fact]
    public async Task PedidoNovo_DevolveOCanalAoAparelho()
    {
        await AplicarAsync(Pedido("o-volta", "telefone"));

        // Mesmo conversor do pull (SyncController); SyncDtoConverters é internal.
        var conversor = typeof(SyncMutationDispatcher).Assembly
            .GetType("EasyStock.Api.Mobile.Services.SyncDtoConverters")!
            .GetMethod("ToDto", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static, [typeof(Order)])!;
        var dto = (OrderDto)conversor.Invoke(null, [Gravado("o-volta")])!;

        dto.Channel.Should().Be("telefone");
    }

    [Fact]
    public async Task ReenvioSemCanal_NaoApagaOCanalGravado()
    {
        await AplicarAsync(Pedido("o-2", "ifood"));
        await AplicarAsync(Pedido("o-2", null, notas: "sem cebola"));

        Gravado("o-2").Channel.Should().Be("ifood", "aparelho antigo reenvia o pedido sem o campo");
    }

    [Theory]
    [InlineData("whatsapp")]
    [InlineData("canal invalido")]
    public async Task ReenvioComOutroCanal_MantemODaCriacao(string reenviado)
    {
        await AplicarAsync(Pedido("o-4", "balcao"));
        await AplicarAsync(Pedido("o-4", reenviado));

        Gravado("o-4").Channel.Should().Be("balcao", "o pedido do ERP copia o canal uma vez só");
    }

    [Fact]
    public void Normalizar_RespeitaOLimiteDaColuna()
    {
        CanalPedidoMobile.Normalizar(new string('a', 20)).Should().Be(new string('a', 20));
        CanalPedidoMobile.Normalizar(new string('a', 21)).Should().BeNull();
    }

    [Fact]
    public async Task PayloadAntigoSemOCampo_ContinuaAceito()
    {
        var antigo = new
        {
            id = "o-antigo",
            clientSnapshot = new { name = "Cliente de teste" },
            items = new[] { new { productId = "p-1", name = "Massa", unit = "un", qty = 1, unitPrice = 80m } },
            total = 80m,
            status = "aguardando",
            createdAt = Agora(),
            updatedAt = Agora()
        };

        await AplicarAsync(antigo);

        Gravado("o-antigo").Channel.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("canal com espaço")]
    [InlineData("balcão")]
    [InlineData("<script>")]
    [InlineData("um-codigo-longo-demais-para-a-coluna")]
    public void Normalizar_SemValorOuForaDoFormato_ViraNaoInformado(string? canal)
    {
        CanalPedidoMobile.Normalizar(canal).Should().BeNull();
    }
}
