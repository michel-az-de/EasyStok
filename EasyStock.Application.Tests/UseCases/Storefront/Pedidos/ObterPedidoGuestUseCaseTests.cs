using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Pedidos;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.Tests.UseCases.Storefront.Pedidos;

/// <summary>
/// Testes unitários do <see cref="ObterPedidoGuestUseCase"/> (issues #684/#854).
///
/// <para>
/// Foco na regra de posse do caminho guest: o TOKEN prova a posse (validado no
/// controller, fora deste use case) e aqui so entram as checagens de existencia,
/// tenant (empresa do storefront) e origem "storefront-guest" — pedido logado
/// NAO e destravavel por token guest (anti-enumeração, retorna null → 404).
/// Mapeamento do DTO ja e coberto em <see cref="ListarPedidosClienteUseCaseTests"/>.
/// </para>
/// </summary>
public class ObterPedidoGuestUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid PedidoId = Guid.NewGuid();
    private const string Slug = "casa-da-baba";

    private static ObterPedidoGuestUseCase BuildSut(
        Domain.Entities.Storefront.Storefront? storefront = null,
        PedidoEntity? pedido = null)
    {
        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => storefront);

        var pedidoRepo = Substitute.For<IPedidoStorefrontRepository>();
        pedidoRepo.GetByIdComItensAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PedidoEntity?>(pedido));

        var avaliacaoRepo = Substitute.For<IPedidoAvaliacaoRepository>();
        avaliacaoRepo.GetByPedidoIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, PedidoAvaliacao>>(
                new Dictionary<Guid, PedidoAvaliacao>()));

        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        vagaRepo.GetByPedidoIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, (VagaOcupada, JanelaEntrega)>>(
                new Dictionary<Guid, (VagaOcupada, JanelaEntrega)>()));

        return new ObterPedidoGuestUseCase(
            storefrontRepo, pedidoRepo, avaliacaoRepo, vagaRepo,
            NullLogger<ObterPedidoGuestUseCase>.Instance);
    }

    private static Domain.Entities.Storefront.Storefront StorefrontAtivo()
    {
        var s = Domain.Entities.Storefront.Storefront.Criar(
            empresaId: EmpresaId,
            slug: Slug,
            tituloPublico: "Casa da Babá",
            pedidoMinimoEntrega: 0m);
        s.Ativar();
        return s;
    }

    private static PedidoEntity PedidoStub(
        Guid? empresaId = null,
        string origem = "storefront-guest")
    {
        var agora = DateTime.UtcNow;
        return new PedidoEntity
        {
            Id = PedidoId,
            EmpresaId = empresaId ?? EmpresaId,
            Status = StatusPedidoMapper.Entregue,
            Total = Dinheiro.FromDecimal(100m),
            Origem = origem,
            CriadoEm = agora,
            AlteradoEm = agora,
        };
    }

    // ── Validação de input ──────────────────────────────────────────────────

    [Fact]
    public async Task Slug_vazio_lanca_ArgumentException()
    {
        var sut = BuildSut(storefront: StorefrontAtivo());

        Func<Task> act = async () => await sut.ExecuteAsync(" ", PedidoId);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*Slug*");
    }

    [Fact]
    public async Task PedidoId_vazio_lanca_ArgumentException()
    {
        var sut = BuildSut(storefront: StorefrontAtivo());

        Func<Task> act = async () => await sut.ExecuteAsync(Slug, Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*PedidoId*");
    }

    [Fact]
    public async Task Storefront_inexistente_lanca_StorefrontNaoEncontradoException()
    {
        var sut = BuildSut(storefront: null);

        Func<Task> act = async () => await sut.ExecuteAsync(Slug, PedidoId);

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task Storefront_inativo_lanca_StorefrontNaoEncontradoException()
    {
        var storefront = StorefrontAtivo();
        storefront.Desativar();
        var sut = BuildSut(storefront: storefront);

        Func<Task> act = async () => await sut.ExecuteAsync(Slug, PedidoId);

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    // ── Posse / anti-enumeração (null → 404 no controller) ──────────────────

    [Fact]
    public async Task Pedido_inexistente_retorna_null()
    {
        var sut = BuildSut(storefront: StorefrontAtivo(), pedido: null);

        var result = await sut.ExecuteAsync(Slug, PedidoId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Pedido_de_outra_empresa_retorna_null()
    {
        var pedido = PedidoStub(empresaId: Guid.NewGuid());
        var sut = BuildSut(storefront: StorefrontAtivo(), pedido: pedido);

        var result = await sut.ExecuteAsync(Slug, PedidoId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Pedido_logado_nao_e_destravavel_por_token_guest()
    {
        // Origem != "storefront-guest": token guest nao abre pedido de cliente
        // logado, mesmo com ID certeiro (anti-enumeração).
        var pedido = PedidoStub(origem: "storefront");
        var sut = BuildSut(storefront: StorefrontAtivo(), pedido: pedido);

        var result = await sut.ExecuteAsync(Slug, PedidoId);

        result.Should().BeNull();
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Pedido_guest_do_storefront_retorna_dto()
    {
        var sut = BuildSut(storefront: StorefrontAtivo(), pedido: PedidoStub());

        var result = await sut.ExecuteAsync(Slug, PedidoId);

        result.Should().NotBeNull();
        result!.Pedido.PedidoId.Should().Be(PedidoId);
        result.Pedido.Status.Should().Be("Entregue");
    }
}
