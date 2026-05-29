using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Storefront.Avaliacao;

/// <summary>
/// Testes do <see cref="CriarAvaliacaoPedidoUseCase"/> (TASK-EZ-AVAL-001).
///
/// <para>Cobertura:</para>
/// <list type="bullet">
///   <item>Sem cookie → AvaliacaoCookieAusenteException (401).</item>
///   <item>Pedido não encontrado → StorefrontNaoEncontradoException (404).</item>
///   <item>Pedido não entregue → PedidoNaoElegivelParaAvaliacaoException (422).</item>
///   <item>EntregueHáMenos24h → PedidoNaoElegivelParaAvaliacaoException (422).</item>
///   <item>Double submit → AvaliacaoDuplicadaException (409).</item>
///   <item>Nota fora 1-5 → RegraDeDominioVioladaException (400).</item>
///   <item>Happy path → cria PedidoAvaliacao, retorna dto 201.</item>
/// </list>
/// </summary>
public sealed class CriarAvaliacaoPedidoUseCaseTests
{
    private static readonly Guid PedidoId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private const string Slug = "casa-da-baba";
    private const string CookieValido = "cookie-valido-base64url-value";

    // ── Fixture ────────────────────────────────────────────────────────────

    private sealed record Fakes(
        IPedidoStorefrontRepository PedidoRepo,
        IPedidoAvaliacaoRepository AvaliacaoRepo,
        ICacheService Cache,
        ComentarioSanitizer Sanitizer);

    private static Fakes BuildFakes(
        Pedido? pedido = null,
        PedidoAvaliacao? avaliacaoExistente = null,
        bool cookieNoCache = true)
    {
        var cache = Substitute.For<ICacheService>();

        if (cookieNoCache)
        {
            // Simula cookie válido no cache: hash do CookieValido armazenado na chave
            var hash = AvaliacaoCookieStore.ComputeHash(CookieValido);
            cache.GetAsync<string>(AvaliacaoCookieStore.CacheKey(PedidoId)).Returns(hash);
        }
        else
        {
            cache.GetAsync<string>(AvaliacaoCookieStore.CacheKey(PedidoId)).Returns((string?)null);
        }

        var pedidoRepo = Substitute.For<IPedidoStorefrontRepository>();
        if (pedido is not null)
            pedidoRepo.GetByIdAsync(PedidoId, Arg.Any<CancellationToken>()).Returns(pedido);

        var avaliacaoRepo = Substitute.For<IPedidoAvaliacaoRepository>();
        avaliacaoRepo.GetByPedidoAsync(PedidoId, Arg.Any<CancellationToken>()).Returns(avaliacaoExistente);

        return new Fakes(pedidoRepo, avaliacaoRepo, cache, new ComentarioSanitizer());
    }

    private static CriarAvaliacaoPedidoUseCase BuildUseCase(Fakes fakes) =>
        new(fakes.PedidoRepo, fakes.AvaliacaoRepo,
            new AvaliacaoCookieStore(fakes.Cache),
            fakes.Sanitizer,
            TimeProvider.System,
            NullLogger<CriarAvaliacaoPedidoUseCase>.Instance);

    private static Pedido BuildPedidoEntregue(int horasAtras = 25)
    {
        var pedido = new Pedido
        {
            Id = PedidoId,
            EmpresaId = EmpresaId,
            ClienteId = ClienteId,
            ClienteNome = "Maria",
            Status = StatusPedidoMapper.Entregue,
            EntreguEm = DateTime.UtcNow.AddHours(-horasAtras),
            AvaliacaoSolicitadaEm = DateTime.UtcNow.AddHours(-horasAtras + 1),
            CriadoEm = DateTime.UtcNow.AddDays(-2),
            AlteradoEm = DateTime.UtcNow.AddDays(-2),
        };
        return pedido;
    }

    // ── Testes ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SemCookie_LancaAvaliacaoCookieAusenteException()
    {
        var fakes = BuildFakes(cookieNoCache: false);
        var useCase = BuildUseCase(fakes);
        var input = new CriarAvaliacaoPedidoInput(Slug, PedidoId, 5, null, false, null, CookieValido);

        var act = () => useCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<AvaliacaoCookieAusenteException>();
    }

    [Fact]
    public async Task PedidoNaoEncontrado_LancaStorefrontNaoEncontrado()
    {
        var fakes = BuildFakes(); // pedido null
        var useCase = BuildUseCase(fakes);
        var input = new CriarAvaliacaoPedidoInput(Slug, PedidoId, 5, null, false, null, CookieValido);

        var act = () => useCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task PedidoNaoEntregue_LancaPedidoNaoElegivel()
    {
        var pedido = BuildPedidoEntregue();
        pedido.Status = StatusPedidoMapper.Preparando;
        var fakes = BuildFakes(pedido: pedido);
        var useCase = BuildUseCase(fakes);
        var input = new CriarAvaliacaoPedidoInput(Slug, PedidoId, 5, null, false, null, CookieValido);

        var act = () => useCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<PedidoNaoElegivelParaAvaliacaoException>();
    }

    [Fact]
    public async Task EntregueHaMenos24h_LancaPedidoNaoElegivel()
    {
        var pedido = BuildPedidoEntregue(horasAtras: 10); // apenas 10h atrás
        var fakes = BuildFakes(pedido: pedido);
        var useCase = BuildUseCase(fakes);
        var input = new CriarAvaliacaoPedidoInput(Slug, PedidoId, 5, null, false, null, CookieValido);

        var act = () => useCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<PedidoNaoElegivelParaAvaliacaoException>();
    }

    [Fact]
    public async Task DoubleSubmit_LancaAvaliacaoDuplicada()
    {
        var pedido = BuildPedidoEntregue();
        var avaliacaoExistente = PedidoAvaliacao.Criar(
            PedidoId, ClienteId, EmpresaId, 5, null, true, null,
            DateTime.UtcNow.AddHours(-24));
        var fakes = BuildFakes(pedido: pedido, avaliacaoExistente: avaliacaoExistente);
        var useCase = BuildUseCase(fakes);
        var input = new CriarAvaliacaoPedidoInput(Slug, PedidoId, 5, null, false, null, CookieValido);

        var act = () => useCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<AvaliacaoDuplicadaException>();
    }

    [Fact]
    public async Task NotaForaDe1A5_LancaRegraDeDominio()
    {
        var pedido = BuildPedidoEntregue();
        var fakes = BuildFakes(pedido: pedido);
        var useCase = BuildUseCase(fakes);
        var input = new CriarAvaliacaoPedidoInput(Slug, PedidoId, 6, null, true, null, CookieValido);

        var act = () => useCase.ExecuteAsync(input);

        await act.Should().ThrowAsync<RegraDeDominioVioladaException>();
    }

    [Fact]
    public async Task HappyPath_CriaAvaliacaoRetorna201Dto()
    {
        var pedido = BuildPedidoEntregue();
        var fakes = BuildFakes(pedido: pedido);
        var useCase = BuildUseCase(fakes);
        var input = new CriarAvaliacaoPedidoInput(
            Slug, PedidoId, 5, "Excelente serviço!", true, null, CookieValido);

        var result = await useCase.ExecuteAsync(input);

        result.Nota.Should().Be(5);
        result.Comentario.Should().Be("Excelente serviço!");
        await fakes.AvaliacaoRepo.Received(1).AddAsync(
            Arg.Is<PedidoAvaliacao>(a => a.Estrelas == 5 && a.PedidoId == PedidoId),
            Arg.Any<CancellationToken>());
    }
}
