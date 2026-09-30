using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Tests.Services.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Campanhas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Storefront.Checkout;

/// <summary>
/// Checkout guest do site (#680) e a conversão da campanha (#1226): o pedido de quem recebeu campanha
/// nos últimos 7 dias marca o destinatário como <c>Pediu</c>, pela mesma regra do pedido da conversa.
/// </summary>
public class IniciarCheckoutGuestUseCaseTests
{
    private const string Telefone = "+5511999998888";

    private readonly CheckoutCoreServiceTests.Cenario _c = new();
    private readonly IClienteStorefrontRepository _clientes = Substitute.For<IClienteStorefrontRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();

    private IniciarCheckoutGuestUseCase UseCase() => new(
        _c.StorefrontRepo,
        _c.Servico(),
        _c.FreteZonaRepo,
        _clientes,
        _c.PedidoRepo,
        _uow,
        new AcompanhamentoTokenService(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Acompanhamento:JwtSecret"] = new string('s', 32),
                })
                .Build(),
            _c.Relogio),
        _c.Atribuicao(),
        _tenant,
        _c.Relogio,
        NullLogger<IniciarCheckoutGuestUseCase>.Instance);

    private IniciarCheckoutGuestInput Input() => new(
        Slug: "casa-da-baba",
        Nome: "Maria",
        Telefone: Telefone,
        Cep: "01310-100",
        Numero: "10",
        Items: new List<CheckoutItemInput> { new(_c.CardapioItemId, 1) });

    [Fact]
    public async Task PedidoGuestDeQuemRecebeuCampanhaMarcaPediu()
    {
        var empresaId = _c.Storefront.EmpresaId;
        var cliente = new Cliente { Id = Guid.NewGuid(), EmpresaId = empresaId, Nome = "Maria", Telefone = Telefone };
        _clientes.GetByTelefoneHashAsync(empresaId, ClienteOtp.CalcularTelefoneHash(Telefone), Arg.Any<CancellationToken>())
            .Returns(cliente);

        var agora = _c.Relogio.GetUtcNow().UtcDateTime;
        var campanha = Campanha.Criar(empresaId, Guid.NewGuid(),
            new DadosCampanha("Bolo de fubá", "Oi {{nome}}", null, null, FiltroCampanha.ParaTodos, [], null, false, null),
            agora.AddDays(-3));
        var destinatario = CampanhaDestinatario.Criar(campanha, cliente.Id);
        destinatario.Enfileirar(1, Guid.NewGuid());
        destinatario.MarcarEnviado(agora.AddDays(-2));
        _c.CampanhaRepo.ObterEnviadoParaAtribuirAsync(empresaId, cliente.Id, agora.AddDays(-7), Arg.Any<CancellationToken>())
            .Returns(destinatario);

        var resultado = await UseCase().ExecuteAsync(Input());

        destinatario.Status.Should().Be(StatusCampanhaDestinatario.Pediu);
        destinatario.PedidoId.Should().Be(resultado.PedidoId);
        _tenant.Received().SetCurrentTenant(empresaId);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task SemCampanhaRecenteOPedidoSegueNormal()
    {
        // Cliente novo (nenhum envio de campanha): o repositório não acha destinatário e nada muda.
        var resultado = await UseCase().ExecuteAsync(Input());

        resultado.PedidoId.Should().NotBeEmpty();
        _c.PedidosAdicionados.Should().ContainSingle(p => p.Id == resultado.PedidoId);
        await _uow.Received(1).CommitAsync();
    }
}
