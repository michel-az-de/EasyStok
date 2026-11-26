using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// S67 (#2534): cada turno que chama o modelo grava um <see cref="TurnoAgente"/> com os números que o cliente LLM
/// devolveu, as ferramentas pedidas, o desfecho e os ids das mensagens. Portão silencioso (janela, situação) não grava.
/// </summary>
public partial class AgenteAtendimentoServiceTests
{
    private const string ModeloRespondido = "claude-sonnet-5-20261101";

    private readonly ITurnoAgenteRepository _turnos = Substitute.For<ITurnoAgenteRepository>();

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
        // #3365 (D2): o custo vem de cada chamada (quem a registra em chamada_ia o calcula); o turno só soma. Os valores
        // não saem de nenhuma tabela, para provar que o serviço não recalcula.
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            UsoFerramenta("consultar_pedido") with { TokensCacheLido = 500, Modelo = ModeloRespondido, CustoUsd = 0.5m },
            Texto("Seu pedido fica pronto por volta das 15h40.") with { Modelo = ModeloRespondido, CustoUsd = 0.25m });

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
        turno.CustoEstimado.Should().Be(0.75m, "é a soma do custo das duas chamadas, sem tabela de preço no turno");
        turno.DuracaoMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ChamadaSemCustoDeixaOCustoDoTurnoGravadoNulo()
    {
        // #3365 (D2): sem custo vindo da chamada (modelo sem preço ou cliente sem registro), o turno fica sem custo e o painel
        // o conta como "fora da conta"; o serviço não tem mais tabela de preço para recalcular.
        VerificarConsulta("Seu pedido fica pronto por volta das 15h40.");
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            UsoFerramenta("consultar_pedido") with { Modelo = ModeloRespondido, CustoUsd = 0.5m },
            Texto("Seu pedido fica pronto por volta das 15h40.") with { Modelo = ModeloRespondido });

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().CustoEstimado.Should().BeNull("uma das duas chamadas veio sem custo");
    }

    [Fact]
    public async Task TodaChamadaDoTurnoLevaOIdDoTurnoGravado()
    {
        // #3287 (R5): o id nasce antes das chamadas e é o mesmo do TurnoAgente, para ligar chamada_ia ao turno.
        var requisicoes = new List<RequisicaoLlm>();
        _llm.EnviarAsync(Arg.Do<RequisicaoLlm>(requisicoes.Add), Arg.Any<CancellationToken>()).Returns(
            UsoFerramenta("consultar_pedido"), Texto("Seu pedido fica pronto por volta das 15h40."));
        VerificarConsulta("Seu pedido fica pronto por volta das 15h40.");

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        requisicoes.Should().HaveCount(2).And.OnlyContain(r => r.TurnoAgenteId == turno.Id);
    }

    [Fact]
    public async Task ChamadaQueFalhaTambemLevaOIdDoTurnoGravadoComErro()
    {
        var requisicoes = new List<RequisicaoLlm>();
        _llm.EnviarAsync(Arg.Do<RequisicaoLlm>(requisicoes.Add), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new HttpRequestException("fora do ar"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Erro);
        requisicoes.Should().NotBeEmpty().And.OnlyContain(r => r.TurnoAgenteId == turno.Id);
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
    public async Task PromessaComONomeDaDonaDaLojaEBarradaNoTurno()
    {
        // #3278 (R4): a guarda conhece o nome configurado da dona; loja que não é a Casa da Baba também escala.
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        configuracao.Ativo = true;
        configuracao.DefinirAssistente("Fulana", null, null, null, null);
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns(configuracao);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(Texto("A Fulana vai combinar com você a entrega e o frete."));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Escalou.Should().BeTrue();
        await _cloudClient.DidNotReceive().EnviarTextoAsync(WaId, Arg.Is<string>(t => t.Contains("Fulana vai combinar")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        TurnoGravado().Motivo.Should().Be("guarda:promete_encaminhar");
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
    public async Task PortaoSilenciosoNaoGravaTurno()
    {
        // #3175: os portoes que escalam gravam turno (motivo portao:*); a janela de 24 h barra calada e nao grava.
        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora.AddHours(25));

        await _turnos.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }
}
