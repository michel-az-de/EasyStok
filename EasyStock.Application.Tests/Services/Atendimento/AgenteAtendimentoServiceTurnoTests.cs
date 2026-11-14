using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// S67 (#2534): cada turno que chama o modelo grava um <see cref="TurnoAgente"/> com os números que o cliente LLM
/// devolveu, as ferramentas pedidas, o desfecho e os ids das mensagens. Turno que nem chama o modelo não grava.
/// </summary>
public partial class AgenteAtendimentoServiceTests
{
    private const string ModeloRespondido = "claude-sonnet-5-20261101";

    private readonly ITurnoAgenteRepository _turnos = Substitute.For<ITurnoAgenteRepository>();

    private readonly PrecosModelosAgenteOptions _precos = TabelaDePrecos();

    private static PrecosModelosAgenteOptions TabelaDePrecos()
    {
        var tabela = new PrecosModelosAgenteOptions();
        tabela.Modelos["claude-sonnet-5"] = new PrecoModeloAgente(3m, 15m, 0.3m);
        return tabela;
    }

    private TurnoAgente TurnoGravado() =>
        _turnos.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(ITurnoAgenteRepository.AddAsync))
            .Select(c => (TurnoAgente)c.GetArguments()[0]!)
            .Should().ContainSingle().Subject;

    private List<Mensagem> CapturarSaidas()
    {
        var saidas = new List<Mensagem>();
        _conversaRepository.When(r => r.AddMensagemAsync(Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Agente), Arg.Any<CancellationToken>()))
            .Do(c => saidas.Add(c.Arg<Mensagem>()));
        return saidas;
    }

    [Fact]
    public async Task TurnoRespondidoGravaTokensModeloFerramentasEIds()
    {
        VerificarConsulta("Seu pedido fica pronto por volta das 15h40.");
        var saidas = CapturarSaidas();
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            UsoFerramenta("consultar_pedido") with { TokensCacheLido = 500, Modelo = ModeloRespondido },
            Texto("Seu pedido fica pronto por volta das 15h40.") with { Modelo = ModeloRespondido });

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.EmpresaId.Should().Be(_empresaId);
        turno.ConversaId.Should().Be(_conversa.Id);
        turno.MensagemClienteId.Should().Be(_historico[0].Id);
        turno.MensagemRespostaId.Should().Be(saidas.Should().ContainSingle().Subject.Id);
        turno.Modelo.Should().Be(ModeloRespondido);
        turno.TokensEntrada.Should().Be(180);
        turno.TokensSaida.Should().Be(30);
        turno.TokensCacheLido.Should().Be(500);
        turno.Passos.Should().Be(2);
        turno.Ferramentas.Should().Equal(new FerramentaDoTurno("consultar_pedido", true));
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Respondeu);
        // 180 × 3 + 30 × 15 + 500 × 0,30, por milhão.
        turno.CustoEstimado.Should().Be(0.00114m);
        turno.DuracaoMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task FalhaDoLlmGravaTurnoComErro()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new HttpRequestException("fora do ar"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Erro);
        turno.Passos.Should().Be(0);
        turno.TotalTokens.Should().Be(0);
        turno.Modelo.Should().Be("desconhecido");
        turno.CustoEstimado.Should().BeNull("modelo sem preço dá custo nulo");
    }

    [Fact]
    public async Task RespostaBarradaGravaTurnoQueEscalou()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("Seu estorno já caiu na conta."));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Escalou);
        turno.Passos.Should().Be(1);
        turno.Ferramentas.Should().BeEmpty();
    }

    [Fact]
    public async Task FerramentaQueFalhaFicaMarcadaSemOk()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(UsoFerramenta("inexistente"), Texto("Texto livre ignorado."));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().Ferramentas.Should().Equal(new FerramentaDoTurno("inexistente", false));
    }

    [Fact]
    public async Task ConversaCorrigidaDuranteOTurnoGravaInterrompido()
    {
        var aguardando = new TaskCompletionSource<RespostaLlm>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ => { chamou.SetResult(); return aguardando.Task; });
        var turno = CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        await chamou.Task;
        _conversa.VincularCliente(Guid.NewGuid());
        aguardando.SetResult(Texto("Resposta da pessoa anterior."));
        await turno;

        var gravado = TurnoGravado();
        gravado.Desfecho.Should().Be(DesfechoTurnoAgente.Interrompido);
        gravado.MensagemRespostaId.Should().BeNull();
    }

    [Fact]
    public async Task TurnoQueNaoChamaOModeloNaoGrava()
    {
        RespostasDoAgenteNaUltimaHora(LimiteRespostasAgenteOptions.PadraoPorHora);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        await _turnos.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
