using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.AcoesBotao;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary><c>acao:avaliacao:positiva|negativa:&lt;pedidoId&gt;</c> (S26): avaliação de um toque, sem LLM.</summary>
public class AvaliacaoAcaoBotaoTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 13, 40, 0, DateTimeKind.Utc);
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly IPedidoStorefrontRepository _pedidos = Substitute.For<IPedidoStorefrontRepository>();
    private readonly IPedidoAvaliacaoRepository _avaliacoes = Substitute.For<IPedidoAvaliacaoRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IEscaladorConversa _escalador = Substitute.For<IEscaladorConversa>();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();

    public AvaliacaoAcaoBotaoTests() => _canal.Canal.Returns(CanalConversa.WhatsApp);

    private Conversa NovaConversa()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria", _clienteId);
        conversa.RegistrarEntrada(Agora);
        return conversa;
    }

    private Pedido PedidoEntregue()
    {
        var pedido = new Pedido
        {
            Id = Guid.NewGuid(), EmpresaId = _empresaId, ClienteId = _clienteId, ClienteNome = "Maria",
            Status = StatusPedidoMapper.Entregue, EntreguEm = Agora.AddMinutes(-40),
            CriadoEm = Agora.AddHours(-3), AlteradoEm = Agora.AddMinutes(-40),
        };
        _pedidos.GetByIdAsync(pedido.Id, Arg.Any<CancellationToken>()).Returns(pedido);
        return pedido;
    }

    private RoteadorAcoesBotao Roteador() => new(
        [new AvaliacaoAcaoBotao(new RegistrarAvaliacaoSimplesUseCase(_pedidos, _avaliacoes), _escalador,
            new ResolvedorCanal([_canal]), _conversas, NullLogger<AvaliacaoAcaoBotao>.Instance)],
        NullLogger<RoteadorAcoesBotao>.Instance);

    [Fact]
    public async Task AvaliacaoNegativaRegistraEPassaParaDona()
    {
        var pedido = PedidoEntregue();
        var conversa = NovaConversa();

        var tratado = await Roteador().ExecutarAsync(_empresaId, conversa, $"acao:avaliacao:negativa:{pedido.Id}", Agora);

        tratado.Should().BeTrue();
        await _avaliacoes.Received(1).AddAsync(
            Arg.Is<PedidoAvaliacao>(a => a.PedidoId == pedido.Id && a.Resultado == ResultadoAvaliacao.Negativa), Arg.Any<CancellationToken>());
        await _escalador.Received(1).EscalarAsync(_empresaId, conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>());
        await _canal.Received(1).EnviarTextoAsync(conversa.ContatoIdExterno, AvaliacaoAcaoBotao.RespostaNegativa, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AvaliacaoPositivaAgradeceSemEscalar()
    {
        var pedido = PedidoEntregue();
        var conversa = NovaConversa();

        await Roteador().ExecutarAsync(_empresaId, conversa, $"acao:avaliacao:positiva:{pedido.Id}", Agora);

        await _avaliacoes.Received(1).AddAsync(
            Arg.Is<PedidoAvaliacao>(a => a.Resultado == ResultadoAvaliacao.Positiva), Arg.Any<CancellationToken>());
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default);
        await _canal.Received(1).EnviarTextoAsync(conversa.ContatoIdExterno, AvaliacaoAcaoBotao.RespostaPositiva, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicadaRespondeComMensagemAmigavel()
    {
        var pedido = PedidoEntregue();
        _avaliacoes.GetByPedidoAsync(pedido.Id, Arg.Any<CancellationToken>())
            .Returns(PedidoAvaliacao.CriarSimples(pedido.Id, _clienteId, _empresaId, ResultadoAvaliacao.Positiva, null, Agora));
        var conversa = NovaConversa();

        await Roteador().ExecutarAsync(_empresaId, conversa, $"acao:avaliacao:negativa:{pedido.Id}", Agora);

        await _avaliacoes.DidNotReceive().AddAsync(Arg.Any<PedidoAvaliacao>(), Arg.Any<CancellationToken>());
        await _canal.Received(1).EnviarTextoAsync(conversa.ContatoIdExterno, AvaliacaoAcaoBotao.RespostaDuplicada, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayloadInvalidoNaoGrava()
    {
        var conversa = NovaConversa();

        await Roteador().ExecutarAsync(_empresaId, conversa, "acao:avaliacao:talvez:xyz", Agora);

        await _avaliacoes.DidNotReceive().AddAsync(Arg.Any<PedidoAvaliacao>(), Arg.Any<CancellationToken>());
    }
}
