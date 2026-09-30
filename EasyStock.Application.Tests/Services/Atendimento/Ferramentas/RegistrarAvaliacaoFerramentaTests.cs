using System.Text.Json;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.Services.Atendimento.Ferramentas;

/// <summary><c>registrar_avaliacao</c> (S26): texto livre ("adorei!") vira a mesma avaliação do botão.</summary>
public class RegistrarAvaliacaoFerramentaTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 13, 40, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly IPedidoStorefrontRepository _pedidos = Substitute.For<IPedidoStorefrontRepository>();
    private readonly IPedidoAvaliacaoRepository _avaliacoes = Substitute.For<IPedidoAvaliacaoRepository>();
    private readonly IEscaladorConversa _escalador = Substitute.For<IEscaladorConversa>();

    private RegistrarAvaliacaoFerramenta Ferramenta() => new(new RegistrarAvaliacaoSimplesUseCase(_pedidos, _avaliacoes), _escalador);

    private ContextoTurnoAgente Contexto(Guid? clienteId) =>
        new(_empresaId, Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria", clienteId), Agora);

    private Guid PedidoEntregue()
    {
        var pedido = new Pedido
        {
            Id = Guid.NewGuid(), EmpresaId = _empresaId, ClienteId = _clienteId, ClienteNome = "Maria",
            Status = StatusPedidoMapper.Entregue, EntreguEm = Agora.AddMinutes(-40),
            CriadoEm = Agora.AddHours(-3), AlteradoEm = Agora.AddMinutes(-40),
        };
        _pedidos.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);
        return pedido.Id;
    }

    private static JsonElement Entrada(object valor) => JsonSerializer.SerializeToElement(valor);

    [Fact]
    public async Task PositivaRegistra()
    {
        var pedidoId = PedidoEntregue();

        var resultado = await Ferramenta().ExecutarAsync(Contexto(_clienteId),
            Entrada(new { pedido_id = pedidoId.ToString(), resultado = "positiva", comentario = "adorei!" }));

        resultado.Should().Contain("registrada");
        await _avaliacoes.Received(1).AddAsync(
            Arg.Is<PedidoAvaliacao>(a => a.Resultado == ResultadoAvaliacao.Positiva && a.Comentario == "adorei!"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NegativaPassaParaDona()
    {
        var pedidoId = PedidoEntregue();
        var contexto = Contexto(_clienteId);

        await Ferramenta().ExecutarAsync(contexto, Entrada(new { pedido_id = pedidoId.ToString(), resultado = "negativa" }));

        await _escalador.Received(1).EscalarAsync(_empresaId, contexto.Conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicadaDevolveErro()
    {
        var pedidoId = PedidoEntregue();
        _avaliacoes.GetByPedidoAsync(pedidoId, Arg.Any<CancellationToken>())
            .Returns(PedidoAvaliacao.CriarSimples(pedidoId, _clienteId, _empresaId, ResultadoAvaliacao.Positiva, null, Agora));

        var resultado = await Ferramenta().ExecutarAsync(Contexto(_clienteId),
            Entrada(new { pedido_id = pedidoId.ToString(), resultado = "positiva" }));

        resultado.Should().Contain("avaliacao_duplicada");
    }

    [Fact]
    public async Task SemClienteDevolveErro()
    {
        var resultado = await Ferramenta().ExecutarAsync(Contexto(null),
            Entrada(new { pedido_id = Guid.NewGuid().ToString(), resultado = "positiva" }));

        resultado.Should().Contain("cliente_nao_identificado");
    }
}
