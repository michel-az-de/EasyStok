using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>#2885 (S75): saudação solta em conversa parada é respondida sem modelo, só quando todas as condições valem.</summary>
public class RespostaSemIaTests
{
    private const string Destino = "5511999998888";
    private const string TextoRegra = "Oi, {nome}! Como posso te ajudar?";
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTime Agora = new(2026, 11, 19, 15, 0, 0, DateTimeKind.Utc);

    private readonly IRegraAutomaticaRepository _regras = Substitute.For<IRegraAutomaticaRepository>();
    private readonly IConfiguracaoAtendimentoRepository _configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IClienteRepository _clientes = Substitute.For<IClienteRepository>();
    private readonly ITurnoAgenteRepository _turnos = Substitute.For<ITurnoAgenteRepository>();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly ICanalMensageria _canal = Substitute.For<ICanalMensageria>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Conversa _conversa;
    private readonly Mensagem _entrada;
    private readonly List<Mensagem> _historico = [];
    private readonly RespostaSemIa _sut;

    public RespostaSemIaTests()
    {
        _canal.Canal.Returns(CanalConversa.WhatsApp);
        _canal.EnviarTextoAsync(Destino, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("wamid.sem-ia");
        _llm.Disponivel.Returns(true); _llm.DisponivelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        _conversa = Conversa.Abrir(Empresa, Destino, Agora.AddHours(-1), "Ana Souza");
        _conversa.RegistrarEntrada(Agora.AddMinutes(-5)); // a entrada conta para a janela de 24 h
        _entrada = Mensagem.Entrada(Empresa, _conversa.Id, Agora, TipoConteudoMensagem.Texto, "Oi");
        _historico.Add(_entrada);
        _conversas.ObterComMensagensAsync(Empresa, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ConversaComMensagens(_conversa, _historico.ToList()));

        _regras.ObterPorGatilhoAsync(Empresa, GatilhoAutomacao.SaudacaoSolta, Arg.Any<CancellationToken>())
            .Returns(RegraAutomatica.Criar(Empresa, GatilhoAutomacao.SaudacaoSolta, TextoRegra, ligada: true, Agora));
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(Empresa);
        configuracao.Ativo = true; // Assistente ligado na loja: é o caso que o portão deixa passar
        _configuracoes.GetByEmpresaIdAsync(Empresa).Returns(configuracao);

        _sut = new RespostaSemIa(_regras, _configuracoes, _conversas, _clientes, _turnos, _llm,
            new ResolvedorCanal([_canal]), _uow, NullLogger<RespostaSemIa>.Instance,
            Options.Create(new LimiteRespostasAgenteOptions()));
    }

    private Task<bool> Tentar(Mensagem? entrada = null) => _sut.TentarAsync(Empresa, _conversa, entrada ?? _entrada, Agora);

    [Fact]
    public async Task CasoFeliz_EnviaGravaComoAgenteMarcaProcessadaERegistraSemIa()
    {
        var respondeu = await Tentar();

        respondeu.Should().BeTrue();
        await _canal.Received(1).EnviarTextoAsync(Destino, "Oi, Ana! Como posso te ajudar?", Arg.Any<CancellationToken>());
        await _conversas.Received(1).AddMensagemAsync(Arg.Is<Mensagem>(m =>
            m.Autor == AutorMensagem.Agente && m.Direcao == DirecaoMensagem.Saida
            && m.Texto == "Oi, Ana! Como posso te ajudar?"), Arg.Any<CancellationToken>());
        _entrada.ProcessadaEm.Should().Be(Agora);
        await _turnos.Received(1).AddAsync(Arg.Is<TurnoAgente>(t =>
            t.Desfecho == DesfechoTurnoAgente.SemIa && t.Modelo == "sem-ia" && t.TokensEntrada == 0 && t.TokensSaida == 0
            && t.Passos == 0), Arg.Any<CancellationToken>());
        await _uow.Received().CommitAsync();
    }

    [Fact]
    public async Task ClassificacaoQueNaoEhSaudacao_DevolveFalsoSemEfeito()
    {
        var pergunta = Mensagem.Entrada(Empresa, _conversa.Id, Agora, TipoConteudoMensagem.Texto, "Oi, quero 2 bolos");
        _historico.Add(pergunta);

        (await Tentar(pergunta)).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        await _turnos.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        pergunta.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task RegraDesligada_DevolveFalsoSemEfeito()
    {
        _regras.ObterPorGatilhoAsync(Empresa, GatilhoAutomacao.SaudacaoSolta, Arg.Any<CancellationToken>())
            .Returns(RegraAutomatica.Criar(Empresa, GatilhoAutomacao.SaudacaoSolta, TextoRegra, ligada: false, Agora));

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task SemRegraGravada_UsaOPadraoDesligadoEDevolveFalso()
    {
        _regras.ObterPorGatilhoAsync(Empresa, GatilhoAutomacao.SaudacaoSolta, Arg.Any<CancellationToken>()).Returns((RegraAutomatica?)null);

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
    }

    [Fact]
    public async Task ConversaAssumidaPelaEquipe_DevolveFalsoSemEfeito()
    {
        _conversa.Assumir(Agora.AddMinutes(-1));

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task LimiteDeRespostasNaUltimaHora_DevolveFalsoSemEfeito()
    {
        _conversas.ContarRespostasDoAgenteDesdeAsync(Empresa, _conversa.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(LimiteRespostasAgenteOptions.PadraoPorHora);

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task DuasEntradasPendentes_DevolveFalsoSemEfeito()
    {
        var anterior = Mensagem.Entrada(Empresa, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto, "Oi, tudo bem?");
        _historico.Insert(0, anterior);

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task SaidaHaCincoMinutos_ConversaNaoEstaParada_DevolveFalsoSemEfeito()
    {
        var saida = Mensagem.Saida(Empresa, _conversa.Id, AutorMensagem.Agente, Agora.AddMinutes(-5),
            TipoConteudoMensagem.Texto, "Seu pedido sai às 15h.", "wamid.recente");
        _historico.Insert(0, saida);

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task FalhaNoEnvio_GravaFalhaDevolveFalsoENaoMarcaProcessada()
    {
        _canal.EnviarTextoAsync(Destino, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("canal indisponível"));

        (await Tentar()).Should().BeFalse();

        await _conversas.Received(1).AddMensagemAsync(Arg.Is<Mensagem>(m =>
            m.Autor == AutorMensagem.Agente && m.Status == StatusMensagem.Falhou), Arg.Any<CancellationToken>());
        _entrada.ProcessadaEm.Should().BeNull();
        await _turnos.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task EntradaDeImagemComLegendaOi_NaoEhSaudacaoSoltaESegueParaOModelo()
    {
        var imagem = Mensagem.Entrada(Empresa, _conversa.Id, Agora, TipoConteudoMensagem.Imagem, "oi");
        _historico.Clear();
        _historico.Add(imagem);

        (await Tentar(imagem)).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        imagem.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task PodeDispensarOModelo_OiComPortoesAbertos_DevolveVerdadeiro()
    {
        (await _sut.PodeDispensarOModeloAsync(Empresa, _conversa, _entrada, Agora)).Should().BeTrue();
    }

    [Fact]
    public async Task PodeDispensarOModelo_OiComPedido_DevolveFalso()
    {
        var pedido = Mensagem.Entrada(Empresa, _conversa.Id, Agora, TipoConteudoMensagem.Texto, "Oi, quero 2 bolos");

        (await _sut.PodeDispensarOModeloAsync(Empresa, _conversa, pedido, Agora)).Should().BeFalse();
    }

    [Fact]
    public async Task PodeDispensarOModelo_ConversaAssumidaPelaEquipe_DevolveFalso()
    {
        _conversa.Assumir(Agora.AddMinutes(-1));

        (await _sut.PodeDispensarOModeloAsync(Empresa, _conversa, _entrada, Agora)).Should().BeFalse();
    }

    [Fact]
    public async Task AssistenteDesligadoNaLoja_DevolveFalsoSemEfeito()
    {
        var desligado = ConfiguracaoAtendimento.CriarPadrao(Empresa);
        desligado.Ativo = false;
        _configuracoes.GetByEmpresaIdAsync(Empresa).Returns(desligado);

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task ClienteBloqueado_DevolveFalsoSemEfeito()
    {
        var cliente = Cliente.Criar(Empresa, "Ana Souza");
        cliente.Bloquear("teste", Agora);
        _conversa.VincularCliente(cliente.Id);
        _clientes.GetByIdAsync(Empresa, cliente.Id).Returns(cliente);

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task ClienteEncaminhadoAResponsavel_DevolveFalsoSemEfeito()
    {
        var cliente = Cliente.Criar(Empresa, "Ana Souza");
        cliente.PausarAtendimentoAutomatico("teste", Agora);
        _conversa.VincularCliente(cliente.Id);
        _clientes.GetByIdAsync(Empresa, cliente.Id).Returns(cliente);

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task CotaDoMesEstourada_DevolveFalsoSemEfeito()
    {
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(Empresa);
        configuracao.Ativo = true;
        configuracao.DefinirAssistente(null, null, 1000, null, null);
        _configuracoes.GetByEmpresaIdAsync(Empresa).Returns(configuracao);
        _turnos.SomarAsync(Empresa, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(1, 5000, 5000, 0, 0m, 0));

        (await Tentar()).Should().BeFalse();

        await _canal.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default);
        _entrada.ProcessadaEm.Should().BeNull();
    }

    [Fact]
    public async Task DezOisSeguidos_RespostasParamNoLimiteDaHora()
    {
        const int limite = 4;
        var sut = new RespostaSemIa(_regras, _configuracoes, _conversas, _clientes, _turnos, _llm,
            new ResolvedorCanal([_canal]), _uow, NullLogger<RespostaSemIa>.Instance,
            Options.Create(new LimiteRespostasAgenteOptions { MaximoPorHora = limite }));
        var enviadas = 0;
        _conversas.ContarRespostasDoAgenteDesdeAsync(Empresa, _conversa.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => Task.FromResult(enviadas));

        var respondidas = new List<bool>();
        for (var i = 0; i < 10; i++)
        {
            var oi = Mensagem.Entrada(Empresa, _conversa.Id, Agora, TipoConteudoMensagem.Texto, "Oi");
            _historico.Clear();
            _historico.Add(oi);

            var respondeu = await sut.TentarAsync(Empresa, _conversa, oi, Agora);
            if (respondeu) enviadas++;
            respondidas.Add(respondeu);
        }

        respondidas.Should().Equal(new[] { true, true, true, true, false, false, false, false, false, false });
        enviadas.Should().Be(limite);
        await _canal.Received(limite).EnviarTextoAsync(Destino, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
