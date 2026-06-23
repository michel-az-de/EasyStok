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
/// Testes unitários do <see cref="ObterPedidoClienteUseCase"/> (issue #670).
///
/// <para>
/// Foco no comportamento próprio do "obter um": validação de input, resolução de
/// storefront, posse (null → 404 no controller) e delegação ao mapper compartilhado.
/// A regra de mapeamento (itens/frete/status/avaliação/janela) já é coberta em
/// <see cref="ListarPedidosClienteUseCaseTests"/> — aqui só um happy path confirma o reuso.
/// </para>
/// </summary>
public class ObterPedidoClienteUseCaseTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid PedidoId = Guid.NewGuid();
    private const string Slug = "casa-da-baba";

    private sealed record Sut(
        ObterPedidoClienteUseCase UseCase,
        IPedidoStorefrontRepository PedidoRepo);

    private static Sut BuildSut(
        Domain.Entities.Storefront.Storefront? storefront = null,
        PedidoEntity? pedido = null,
        IReadOnlyDictionary<Guid, PedidoAvaliacao>? avaliacoes = null,
        IReadOnlyDictionary<Guid, (VagaOcupada, JanelaEntrega)>? vagas = null)
    {
        var storefrontRepo = Substitute.For<IStorefrontRepository>();
        storefrontRepo.GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => storefront);

        var pedidoRepo = Substitute.For<IPedidoStorefrontRepository>();
        pedidoRepo.ObterDoClienteAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PedidoEntity?>(pedido));

        var avaliacaoRepo = Substitute.For<IPedidoAvaliacaoRepository>();
        avaliacaoRepo.GetByPedidoIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, PedidoAvaliacao>>(
                avaliacoes ?? new Dictionary<Guid, PedidoAvaliacao>()));

        var vagaRepo = Substitute.For<IVagaOcupadaRepository>();
        vagaRepo.GetByPedidoIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, (VagaOcupada, JanelaEntrega)>>(
                vagas ?? new Dictionary<Guid, (VagaOcupada, JanelaEntrega)>()));

        var useCase = new ObterPedidoClienteUseCase(
            storefrontRepo, pedidoRepo, avaliacaoRepo, vagaRepo,
            NullLogger<ObterPedidoClienteUseCase>.Instance);

        return new Sut(useCase, pedidoRepo);
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
        string status = StatusPedidoMapper.Entregue,
        decimal total = 100m,
        IEnumerable<PedidoItem>? itens = null)
    {
        var agora = DateTime.UtcNow;
        var pedido = new PedidoEntity
        {
            Id = PedidoId,
            EmpresaId = EmpresaId,
            ClienteId = ClienteId,
            Status = status,
            Total = Dinheiro.FromDecimal(total),
            Origem = "storefront",
            CriadoEm = agora,
            AlteradoEm = agora,
        };
        if (itens is not null)
            pedido.Itens = itens.ToList();
        return pedido;
    }

    // ── Validação de input ──────────────────────────────────────────────────

    [Fact]
    public async Task ClienteId_vazio_lanca_ArgumentException()
    {
        var sut = BuildSut(storefront: StorefrontAtivo());

        Func<Task> act = async () =>
            await sut.UseCase.ExecuteAsync(new ObterPedidoClienteInput(Slug, Guid.Empty, PedidoId));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*ClienteId*");
    }

    [Fact]
    public async Task PedidoId_vazio_lanca_ArgumentException()
    {
        var sut = BuildSut(storefront: StorefrontAtivo());

        Func<Task> act = async () =>
            await sut.UseCase.ExecuteAsync(new ObterPedidoClienteInput(Slug, ClienteId, Guid.Empty));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*PedidoId*");
    }

    [Fact]
    public async Task Storefront_inexistente_lanca_StorefrontNaoEncontradoException()
    {
        var sut = BuildSut(storefront: null);

        Func<Task> act = async () =>
            await sut.UseCase.ExecuteAsync(new ObterPedidoClienteInput(Slug, ClienteId, PedidoId));

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task Storefront_inativo_lanca_StorefrontNaoEncontradoException()
    {
        var storefront = StorefrontAtivo();
        storefront.Desativar();
        var sut = BuildSut(storefront: storefront);

        Func<Task> act = async () =>
            await sut.UseCase.ExecuteAsync(new ObterPedidoClienteInput(Slug, ClienteId, PedidoId));

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    // ── Posse / 404 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Pedido_inexistente_ou_de_outro_cliente_retorna_null()
    {
        // Repo devolve null quando o pedido não é do cliente (filtro de posse).
        var sut = BuildSut(storefront: StorefrontAtivo(), pedido: null);

        var result = await sut.UseCase.ExecuteAsync(
            new ObterPedidoClienteInput(Slug, ClienteId, PedidoId));

        result.Should().BeNull();
    }

    [Fact]
    public async Task Passa_empresaId_clienteId_pedidoId_ao_repo()
    {
        var sut = BuildSut(storefront: StorefrontAtivo(), pedido: PedidoStub());

        await sut.UseCase.ExecuteAsync(new ObterPedidoClienteInput(Slug, ClienteId, PedidoId));

        await sut.PedidoRepo.Received(1).ObterDoClienteAsync(
            EmpresaId, ClienteId, PedidoId, Arg.Any<CancellationToken>());
    }

    // ── Happy path (confirma reuso do mapper compartilhado) ──────────────────

    [Fact]
    public async Task Pedido_encontrado_mapeia_dto_no_contrato()
    {
        var pedido = PedidoStub(status: StatusPedidoMapper.Pronto, total: 99.50m, itens: new[]
        {
            new PedidoItem
            {
                Id = Guid.NewGuid(), ProdutoId = Guid.NewGuid(),
                Nome = "Lasanha", Quantidade = 1, PrecoUnitario = 84m, Subtotal = 84m,
            },
            new PedidoItem
            {
                Id = Guid.NewGuid(), ProdutoId = null,
                Nome = "Entrega — Butantã", Quantidade = 1, PrecoUnitario = 15.50m, Subtotal = 15.50m,
            },
        });
        var sut = BuildSut(storefront: StorefrontAtivo(), pedido: pedido);

        var result = await sut.UseCase.ExecuteAsync(
            new ObterPedidoClienteInput(Slug, ClienteId, PedidoId));

        result.Should().NotBeNull();
        result!.Pedido.PedidoId.Should().Be(PedidoId);
        result.Pedido.Status.Should().Be("SaiuParaEntrega");   // Pronto → contrato
        result.Pedido.Itens.Should().HaveCount(1);
        result.Pedido.Itens[0].Nome.Should().Be("Lasanha");
        result.Pedido.SubtotalCentavos.Should().Be(8400);
        result.Pedido.FreteCentavos.Should().Be(1550);
        result.Pedido.TotalCentavos.Should().Be(9950);
    }
}
