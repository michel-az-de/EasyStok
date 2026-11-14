using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>#2496: a checagem do console roda as etapas do turno sem escalar, sem chamar o modelo e sem gravar.</summary>
public class ChecarAssistenteConversaUseCaseTests
{
    private const string WaId = "5511999998888";
    private static readonly DateTime Agora = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly ICadernoRepository _caderno = Substitute.For<ICadernoRepository>();
    private readonly IContatoDaEquipeAvisada _equipe = Substitute.For<IContatoDaEquipeAvisada>();
    private readonly IFerramentaAgente _consultarPedido = Substitute.For<IFerramentaAgente>();
    private readonly ConfiguracaoAtendimento _configuracao;
    private readonly Conversa _conversa;
    private readonly List<Mensagem> _historico = [];

    public ChecarAssistenteConversaUseCaseTests()
    {
        _llm.Disponivel.Returns(true);
        _configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        _configuracao.Ativo = true;
        _configuracoes.GetByEmpresaIdAsync(_empresaId).Returns(_configuracao);

        _conversa = Conversa.Abrir(_empresaId, WaId, Agora.AddMinutes(-1), "Maria");
        _conversa.RegistrarEntrada(Agora.AddMinutes(-1));
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "quanto tempo falta?", "wamid.in1"));
        _conversas.ObterComMensagensAsync(_empresaId, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ConversaComMensagens(_conversa, _historico));

        _consultarPedido.Nome.Returns("consultar_pedido");
        _consultarPedido.Descricao.Returns("Consulta o pedido");
    }

    private ChecarAssistenteConversaUseCase CriarUseCase() => new(
        _llm, _conversas, _configuracoes, _clientes, _caderno,
        new ObterDossieClienteUseCase(_clientes, Substitute.For<IClienteCrmRepository>(),
            Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), _conversas),
        [_consultarPedido], _equipe);

    private static EtapaAssistente Etapa(ChecagemAssistenteResult checagem, string codigo) =>
        checagem.Etapas.Single(e => e.Codigo == codigo);

    [Fact]
    public async Task LojaLigadaEContatoComum_RespondeEMostraPromptEFerramentas()
    {
        var checagem = await CriarUseCase().ExecuteAsync(_empresaId, _conversa.Id, Agora);

        checagem.Ativo.Should().BeTrue();
        checagem.Responde.Should().BeTrue();
        checagem.Bloqueio.Should().BeNull();
        checagem.EntradasPendentes.Should().Be(1);
        checagem.Etapas.Select(e => e.Codigo).Should().Equal(
            PortoesDoAssistente.Situacao, PortoesDoAssistente.Vinculo, PortoesDoAssistente.Janela,
            PortoesDoAssistente.Chave, PortoesDoAssistente.Modelo, PortoesDoAssistente.Canal, PortoesDoAssistente.Equipe,
            PortoesDoAssistente.Bloqueado, PortoesDoAssistente.Pausado, PortoesDoAssistente.Limite);
        checagem.Etapas.Should().NotContain(e => e.Barra);
        Etapa(checagem, PortoesDoAssistente.Bloqueado).Estado.Should().Be(EstadoEtapaAssistente.NaoSeAplica);
        checagem.Prompt.Should().StartWith(PromptAtendimento.Montar(_configuracao));
        checagem.Ferramentas.Should().ContainSingle().Which.Should().Be(new FerramentaDoAssistente("consultar_pedido", "Consulta o pedido"));
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task ContatoDaEquipe_BarraNaEtapaDaEquipeComOMesmoMotivoDoRobo()
    {
        _equipe.EhInscritoAsync(_empresaId, WaId, Arg.Any<CancellationToken>()).Returns(true);

        var checagem = await CriarUseCase().ExecuteAsync(_empresaId, _conversa.Id, Agora);

        checagem.Responde.Should().BeFalse();
        var equipe = Etapa(checagem, PortoesDoAssistente.Equipe);
        equipe.Estado.Should().Be(EstadoEtapaAssistente.Barra);
        equipe.MotivoEscalada.Should().Be(AgenteAtendimentoService.MotivoContatoDaEquipe);
        checagem.Bloqueio.Should().Be(equipe.Detalhe);
    }

    [Fact]
    public async Task ContatoDaEquipeComConversaDevolvida_Responde()
    {
        // #2493: a equipe devolveu esta conversa ao robô; vale até ela ser concluída.
        _equipe.EhInscritoAsync(_empresaId, WaId, Arg.Any<CancellationToken>()).Returns(true);
        _conversa.Assumir(Agora.AddMinutes(-5), Guid.NewGuid());
        _conversa.LiberarAutomatico();

        var checagem = await CriarUseCase().ExecuteAsync(_empresaId, _conversa.Id, Agora);

        checagem.Responde.Should().BeTrue();
        Etapa(checagem, PortoesDoAssistente.Equipe).Estado.Should().Be(EstadoEtapaAssistente.Passa);
    }

    [Fact]
    public async Task ChaveDesligada_BarraEContinuaMostrandoTodasAsEtapas()
    {
        _configuracao.Ativo = false;

        var checagem = await CriarUseCase().ExecuteAsync(_empresaId, _conversa.Id, Agora);

        checagem.Ativo.Should().BeFalse();
        checagem.Responde.Should().BeFalse();
        Etapa(checagem, PortoesDoAssistente.Chave).MotivoEscalada.Should().Be(AgenteAtendimentoService.MotivoAtendimentoAutomaticoDesligado);
        checagem.Etapas.Should().HaveCount(10);
    }

    [Fact]
    public async Task ConversaAssumidaHaMaisDeUmDia_BarraNaSituacaoEAJanelaNaoSeAplica()
    {
        _conversa.Assumir(Agora.AddMinutes(-1), Guid.NewGuid());

        var checagem = await CriarUseCase().ExecuteAsync(_empresaId, _conversa.Id, Agora.AddDays(2));

        Etapa(checagem, PortoesDoAssistente.Situacao).Estado.Should().Be(EstadoEtapaAssistente.Barra);
        Etapa(checagem, PortoesDoAssistente.Janela).Estado.Should().Be(EstadoEtapaAssistente.NaoSeAplica);
        checagem.Responde.Should().BeFalse();
    }

    [Fact]
    public async Task NoLimiteDaHora_Barra()
    {
        _conversas.ContarRespostasDoAgenteDesdeAsync(_empresaId, _conversa.Id, Agora.AddHours(-1), Arg.Any<CancellationToken>())
            .Returns(LimiteRespostasAgenteOptions.PadraoPorHora);

        var checagem = await CriarUseCase().ExecuteAsync(_empresaId, _conversa.Id, Agora);

        Etapa(checagem, PortoesDoAssistente.Limite).MotivoEscalada.Should()
            .Be(AgenteAtendimentoService.MotivoLimiteRespostas(LimiteRespostasAgenteOptions.PadraoPorHora));
        checagem.Responde.Should().BeFalse();
    }

    [Fact]
    public async Task ConversaDeOutraEmpresa_NaoEncontrada()
    {
        var acao = () => CriarUseCase().ExecuteAsync(Guid.NewGuid(), _conversa.Id, Agora);

        await acao.Should().ThrowAsync<ConversaNaoEncontradaException>();
    }
}
