using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Sales;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Comanda;

/// <summary>F03: o console acompanha pelo polling o pedido e o pagamento da conversa.</summary>
public class ObterPedidoConversaUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly ICobrancaPedidoRepository _cobrancas = Substitute.For<ICobrancaPedidoRepository>();
    private readonly ObterPedidoConversaUseCase _useCase;

    public ObterPedidoConversaUseCaseTests()
    {
        _useCase = new ObterPedidoConversaUseCase(_conversas, _pedidos, _cobrancas);
    }

    private Conversa NovaConversa()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria");
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        return conversa;
    }

    private Pedido NovoPedido(Conversa conversa, string status = StatusPedidoMapper.AguardandoPagamento)
    {
        var cardapioItemId = Guid.NewGuid();
        var pedido = new Pedido
        {
            Id = Guid.NewGuid(), EmpresaId = _empresaId, Status = status, Total = Dinheiro.FromDecimal(33m), CriadoEm = Agora,
        };
        pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id, CardapioItemId = cardapioItemId,
            Nome = "Lasanha", Quantidade = 1, PrecoUnitario = 25m, Subtotal = 25m, Observacao = "sem cebola" });
        pedido.Itens.Add(new PedidoItem { Id = Guid.NewGuid(), PedidoId = pedido.Id,
            Nome = "Entrega — Centro", Quantidade = 1, PrecoUnitario = 8m, Subtotal = 8m });
        conversa.DefinirPedidoEmAndamento(pedido.Id);
        _pedidos.GetByIdWithDetailsAsync(_empresaId, pedido.Id).Returns(pedido);
        return pedido;
    }

    [Fact]
    public async Task PedidoQueRequerAprovacao_AvisaOConsoleAntesDaBaixa()
    {
        // #1474: a baixa manual com link pendente troca a forma e registra o pagamento; com pedido
        // que requer aprovação o registro falha depois da troca. O console precisa saber antes.
        var conversa = NovaConversa();
        var pedido = NovoPedido(conversa);
        pedido.MarcarRequerAprovacao("fora da área");

        var resultado = await _useCase.ExecuteAsync(_empresaId, conversa.Id);

        resultado!.RequerAprovacao.Should().BeTrue();
    }

    [Fact]
    public async Task SemPedido_DevolveNulo()
    {
        var conversa = NovaConversa();

        (await _useCase.ExecuteAsync(_empresaId, conversa.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ConversaDeOutraEmpresa_NaoEncontrada()
    {
        var conversa = NovaConversa();
        NovoPedido(conversa);

        var act = () => _useCase.ExecuteAsync(Guid.NewGuid(), conversa.Id);

        await act.Should().ThrowAsync<ConversaNaoEncontradaException>();
    }

    [Fact]
    public async Task SeparaItensDoFreteETrazACobrancaMaisRecente()
    {
        var conversa = NovaConversa();
        var pedido = NovoPedido(conversa);
        var expirada = CobrancaPedido.CriarOnline(_empresaId, pedido.Id, 33m, "pref-1", "https://mp/1",
            Agora.AddMinutes(-40), 1, Agora.AddMinutes(-70), conversa.Id);
        expirada.Expirar(Agora.AddMinutes(-39));
        var atual = CobrancaPedido.CriarOnline(_empresaId, pedido.Id, 33m, "pref-2", "https://mp/2",
            Agora.AddMinutes(25), 2, Agora.AddMinutes(-5), conversa.Id);
        _cobrancas.ListarDoPedidoAsync(_empresaId, pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new List<CobrancaPedido> { expirada, atual });

        var resultado = await _useCase.ExecuteAsync(_empresaId, conversa.Id);

        resultado!.PedidoId.Should().Be(pedido.Id);
        resultado.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        resultado.Total.Should().Be(33m);
        resultado.Frete.Should().Be(8m);
        resultado.Itens.Should().ContainSingle().Which.Observacao.Should().Be("sem cebola");
        resultado.Cobranca!.LinkPagamento.Should().Be("https://mp/2");
        resultado.Cobranca.Status.Should().Be("Pendente");
        resultado.Cobranca.Tentativa.Should().Be(2);
    }

    [Fact]
    public async Task CobrancaPagaVenceAsOutras()
    {
        var conversa = NovaConversa();
        var pedido = NovoPedido(conversa, StatusPedidoMapper.Aguardando);
        var paga = CobrancaPedido.CriarOnline(_empresaId, pedido.Id, 33m, "pref-1", "https://mp/1",
            Agora.AddMinutes(20), 1, Agora.AddMinutes(-10), conversa.Id);
        paga.MarcarPaga("pay-1", 33m, "pix", Agora.AddMinutes(-2));
        var cancelada = CobrancaPedido.CriarOnline(_empresaId, pedido.Id, 33m, "pref-2", "https://mp/2",
            Agora.AddMinutes(25), 1, Agora.AddMinutes(-1), conversa.Id);
        cancelada.Cancelar("teste", Agora);
        _cobrancas.ListarDoPedidoAsync(_empresaId, pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new List<CobrancaPedido> { paga, cancelada });

        var resultado = await _useCase.ExecuteAsync(_empresaId, conversa.Id);

        resultado!.Cobranca!.Status.Should().Be("Paga");
        resultado.Cobranca.ValorPago.Should().Be(33m);
        resultado.Cobranca.MetodoPagamento.Should().Be("pix");
    }
}
