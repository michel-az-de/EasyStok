using EasyStock.Api.Controllers;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Sales;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StorefrontEntity = EasyStock.Domain.Entities.Storefront.Storefront;

namespace EasyStock.Api.UnitTests.Controllers;

/// <summary>
/// Comanda do console (F03): a empresa vem do token; conversa de outra empresa é 404 e gerar pedido
/// exige a permissão de atender. O caminho feliz do pedido e da cobrança está nos testes do use case.
/// </summary>
public class AtendimentoComandaControllerTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IStorefrontRepository _storefronts = Substitute.For<IStorefrontRepository>();
    private readonly ICardapioItemRepository _cardapio = Substitute.For<ICardapioItemRepository>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly AtendimentoComandaController _controller;

    public AtendimentoComandaControllerTests()
    {
        _currentUser.EmpresaId.Returns(_empresaId);
        _currentUser.UsuarioId.Returns(Guid.NewGuid());
        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(true);

        var listarJanelas = new ListarJanelasDisponiveisUseCase(_storefronts, Substitute.For<IJanelaEntregaRepository>(),
            Substitute.For<IBloqueioEntregaRepository>(), Substitute.For<IVagaOcupadaRepository>(),
            Substitute.For<IFreteZonaRepository>(), TimeProvider.System);

        _controller = new AtendimentoComandaController(
            new ListarCardapioComandaUseCase(_storefronts,
                new ListarCardapioPublicoUseCase(_storefronts, _cardapio, Substitute.For<IItemEstoqueRepository>(),
                    NullLogger<ListarCardapioPublicoUseCase>.Instance)),
            new ListarJanelasAtendimentoUseCase(_storefronts, _cardapio, Substitute.For<IConfiguracaoAtendimentoRepository>(), listarJanelas),
            new ObterPedidoConversaUseCase(_conversas, _pedidos, Substitute.For<ICobrancaPedidoRepository>()),
            // Os casos daqui param antes do núcleo do checkout e da cobrança.
            new GerarPedidoConversaUseCase(_conversas, Substitute.For<IClienteRepository>(),
                null!, null!, null!, null!, TimeProvider.System, NullLogger<GerarPedidoConversaUseCase>.Instance),
            _currentUser);
    }

    private Conversa ConversaDaEmpresa(Guid empresaId)
    {
        var conversa = Conversa.Abrir(empresaId, "5511999998888", DateTime.UtcNow.AddMinutes(-5), "Maria");
        _conversas.ObterPorIdAsync(empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        return conversa;
    }

    private static GerarPedidoConversaBody Comanda() => new(
        [new ItemComandaBody(Guid.NewGuid(), 1)], Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

    [Fact]
    public async Task PedidoDeConversaDeOutraEmpresaDevolve404()
    {
        var alheia = ConversaDaEmpresa(Guid.NewGuid());
        var pedido = new Pedido { Id = Guid.NewGuid(), EmpresaId = alheia.EmpresaId, Status = StatusPedidoMapper.AguardandoPagamento };
        alheia.DefinirPedidoEmAndamento(pedido.Id);
        _pedidos.GetByIdWithDetailsAsync(alheia.EmpresaId, pedido.Id).Returns(pedido);

        var resultado = await _controller.Pedido(alheia.Id);

        resultado.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GerarPedidoEmConversaDeOutraEmpresaDevolve404()
    {
        var alheia = ConversaDaEmpresa(Guid.NewGuid());

        var resultado = await _controller.GerarPedido(alheia.Id, Comanda());

        resultado.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GerarPedidoSemPermissaoDeAtenderDevolve403()
    {
        var conversa = ConversaDaEmpresa(_empresaId);
        _currentUser.TemPermissao(Permissao.AtenderConversas).Returns(false);

        var resultado = await _controller.GerarPedido(conversa.Id, Comanda());

        resultado.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ConversaSemPedidoDevolveDataNulo()
    {
        var conversa = ConversaDaEmpresa(_empresaId);

        var resultado = await _controller.Pedido(conversa.Id);

        var ok = resultado.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value!.GetType().GetProperty("Data")!.GetValue(ok.Value).Should().BeNull();
    }

    [Fact]
    public async Task CardapioVemDaVitrineDaEmpresaDoToken()
    {
        var vitrine = StorefrontEntity.Criar(_empresaId, "casa-da-baba", "Casa da Baba", 0m);
        vitrine.Ativar();
        _storefronts.GetByEmpresaAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(vitrine);
        _storefronts.GetBySlugAsync("casa-da-baba", Arg.Any<CancellationToken>()).Returns(vitrine);

        var resultado = await _controller.Cardapio();

        resultado.Should().BeOfType<OkObjectResult>();
        await _storefronts.DidNotReceive().GetByEmpresaAsync(Arg.Is<Guid>(id => id != _empresaId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmpresaSemVitrineDevolve404NoCardapio()
    {
        var resultado = await _controller.Cardapio();

        resultado.Should().BeOfType<NotFoundObjectResult>();
    }
}
