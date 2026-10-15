using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Storefront.Avaliacao;

/// <summary><see cref="RegistrarAvaliacaoSimplesUseCase"/> (S26): positiva ou negativa, uma por pedido.</summary>
public sealed class RegistrarAvaliacaoSimplesUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 13, 40, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly IPedidoStorefrontRepository _pedidos = Substitute.For<IPedidoStorefrontRepository>();
    private readonly IPedidoAvaliacaoRepository _avaliacoes = Substitute.For<IPedidoAvaliacaoRepository>();

    private Pedido PedidoEntregue(string status = StatusPedidoMapper.Entregue)
    {
        var pedido = new Pedido
        {
            Id = Guid.NewGuid(),
            EmpresaId = _empresaId,
            ClienteId = _clienteId,
            ClienteNome = "Maria",
            Status = status,
            EntreguEm = Agora.AddMinutes(-40),
            CriadoEm = Agora.AddHours(-3),
            AlteradoEm = Agora.AddMinutes(-40),
        };
        _pedidos.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);
        return pedido;
    }

    private RegistrarAvaliacaoSimplesUseCase UseCase() => new(_pedidos, _avaliacoes);

    [Fact]
    public async Task NegativaGravaAvaliacao()
    {
        var pedido = PedidoEntregue();

        var avaliacao = await UseCase().ExecutarAsync(_empresaId, _clienteId, pedido.Id, ResultadoAvaliacao.Negativa, null, Agora);

        avaliacao.Resultado.Should().Be(ResultadoAvaliacao.Negativa);
        avaliacao.PedidoId.Should().Be(pedido.Id);
        avaliacao.SolicitadoEm.Should().Be(pedido.EntreguEm!.Value.AddMinutes(30));
        await _avaliacoes.Received(1).AddAsync(avaliacao, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicadaLanca()
    {
        var pedido = PedidoEntregue();
        var existente = PedidoAvaliacao.CriarSimples(pedido.Id, _clienteId, _empresaId, ResultadoAvaliacao.Positiva, null, Agora);
        _avaliacoes.GetByPedidoAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(existente);

        var act = () => UseCase().ExecutarAsync(_empresaId, _clienteId, pedido.Id, ResultadoAvaliacao.Negativa, null, Agora);

        await act.Should().ThrowAsync<AvaliacaoDuplicadaException>();
        await _avaliacoes.DidNotReceive().AddAsync(Arg.Any<PedidoAvaliacao>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PedidoDeOutroClienteNaoEncontrado()
    {
        var pedido = PedidoEntregue();

        var act = () => UseCase().ExecutarAsync(_empresaId, Guid.NewGuid(), pedido.Id, ResultadoAvaliacao.Positiva, null, Agora);

        await act.Should().ThrowAsync<StorefrontNaoEncontradoException>();
    }

    [Fact]
    public async Task PedidoNaoEntregueNaoElegivel()
    {
        var pedido = PedidoEntregue(StatusPedidoMapper.Preparando);

        var act = () => UseCase().ExecutarAsync(_empresaId, _clienteId, pedido.Id, ResultadoAvaliacao.Positiva, null, Agora);

        await act.Should().ThrowAsync<PedidoNaoElegivelParaAvaliacaoException>();
    }
}
