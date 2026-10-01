using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>O LLM é um fake (<see cref="IAgenteLlmClient"/> substituído): nenhuma chamada de rede.</summary>
public class AgenteAtendimentoServiceTests
{
    private const string WaId = "5511999998888";
    private static readonly DateTime Agora = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IAgenteLlmClient _llm = Substitute.For<IAgenteLlmClient>();
    private readonly IConversaRepository _conversaRepository = Substitute.For<IConversaRepository>();
    private readonly IConfiguracaoAtendimentoRepository _configuracaoRepository = Substitute.For<IConfiguracaoAtendimentoRepository>();
    private readonly IClienteRepository _clienteRepository = Substitute.For<IClienteRepository>();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly IHistoricoPedidosClienteQueries _historicoPedidos = Substitute.For<IHistoricoPedidosClienteQueries>();
    private readonly IDomicilioQueries _domicilio = Substitute.For<IDomicilioQueries>();
    private readonly IEscaladorConversa _escalador = Substitute.For<IEscaladorConversa>();
    private readonly IWhatsAppCloudClient _cloudClient = Substitute.For<IWhatsAppCloudClient>();
    private readonly IUsoIaRepository _usoIaRepository = Substitute.For<IUsoIaRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IFerramentaAgente _consultarPedido = Substitute.For<IFerramentaAgente>();
    private readonly ICadernoRepository _caderno = Substitute.For<ICadernoRepository>();
    private readonly List<RequisicaoLlm> _requisicoes = [];
    private readonly Conversa _conversa;
    private readonly List<Mensagem> _historico = [];

    /// <summary>Situação "no banco", relida antes do envio; nulo = a mesma da conversa carregada.</summary>
    private SituacaoConversa? _situacaoNoBanco;

    public AgenteAtendimentoServiceTests()
    {
        _llm.Disponivel.Returns(true);
        _llm.When(l => l.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()))
            .Do(ci => _requisicoes.Add(ci.Arg<RequisicaoLlm>()));
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("ok"));

        _conversa = Conversa.Abrir(_empresaId, WaId, Agora.AddMinutes(-1), "Maria");
        _conversa.RegistrarEntrada(Agora.AddMinutes(-1));
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "quanto tempo falta?", "wamid.in1"));
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ConversaComMensagens(_conversa, _historico));
        _conversaRepository.ObterSituacaoAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>())
            .Returns(_ => _situacaoNoBanco ?? _conversa.Situacao);

        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns((ConfiguracaoAtendimento?)null);
        _cloudClient.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnvioWhatsAppResult("wamid.out1"));

        _consultarPedido.Nome.Returns("consultar_pedido");
        _consultarPedido.Descricao.Returns("Consulta o pedido");
        _consultarPedido.SchemaJson.Returns("""{"type":"object","properties":{}}""");
        _consultarPedido.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns("""{"status":"EmPreparo","previsao":"15:40"}""");
    }

    private AgenteAtendimentoService CriarServico() => new(
        _llm, _conversaRepository, _configuracaoRepository, _clienteRepository,
        [_consultarPedido], _escalador, _cloudClient, _usoIaRepository, _unitOfWork,
        new ObterDossieClienteUseCase(_clienteRepository, _crm, _historicoPedidos, _domicilio, _conversaRepository),
        _caderno, NullLogger<AgenteAtendimentoService>.Instance);

    private static RespostaLlm Texto(string texto) =>
        new("end_turn", [new BlocoTextoLlm(texto)], 100, 20);

    private static RespostaLlm UsoFerramenta(string nome, string id = "toolu_1") =>
        new(RespostaLlm.StopToolUse,
            [new BlocoTextoLlm("Vou verificar."), new BlocoUsoFerramentaLlm(id, nome, JsonDocument.Parse("{}").RootElement)],
            80, 10);

    [Fact]
    public async Task ExecutaFerramentaEResponde()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(UsoFerramenta("consultar_pedido"), Texto("Seu pedido fica pronto por volta das 15h40."));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeTrue();
        _requisicoes.Should().HaveCount(2);
        _requisicoes[0].Ferramentas.Select(f => f.Nome).Should().Contain("consultar_pedido");
        _requisicoes[0].Mensagens.Should().ContainSingle()
            .Which.Conteudo.OfType<BlocoTextoLlm>().Single().Texto.Should().Contain("quanto tempo falta?");

        // Continuação: assistant com o tool_use + user com o tool_result da ferramenta.
        var continuacao = _requisicoes[1].Mensagens;
        continuacao.Should().HaveCount(3);
        continuacao[1].Papel.Should().Be(MensagemLlm.Assistente);
        continuacao[1].Conteudo.OfType<BlocoUsoFerramentaLlm>().Single().Nome.Should().Be("consultar_pedido");
        continuacao[2].Papel.Should().Be(MensagemLlm.Usuario);
        var resultadoFerramenta = continuacao[2].Conteudo.OfType<BlocoResultadoFerramentaLlm>().Single();
        resultadoFerramenta.UsoFerramentaId.Should().Be("toolu_1");
        resultadoFerramenta.Conteudo.Should().Contain("15:40");

        await _cloudClient.Received(1).EnviarTextoAsync(WaId, "Seu pedido fica pronto por volta das 15h40.",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Agente && m.ExternoId == "wamid.out1"),
            Arg.Any<CancellationToken>());
        await _usoIaRepository.Received(1).AddAsync(Arg.Is<UsoIa>(u =>
            u.EmpresaId == _empresaId && u.TotalGeracoes == 1 && u.TotalTokens == 210));
        await _unitOfWork.Received().CommitAsync();
    }

    [Fact]
    public async Task CadernoEntraNoPromptAntesDoDossie()
    {
        var horario = TrechoCaderno.Criar(_empresaId, "Horário", "Abrimos de terça a sábado.", null, nucleo: true, Agora);
        var troca = TrechoCaderno.Criar(_empresaId, "Troca", "Trocamos em até 24 h.", "troca", nucleo: false, Agora);
        _caderno.ListarAsync(_empresaId, false, Arg.Any<CancellationToken>()).Returns([horario, troca]);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var system = _requisicoes[0].System;
        system.Should().Contain("Abrimos de terça a sábado.").And.Contain($"[{troca.Codigo}] Troca");
        system.IndexOf("Abrimos de terça a sábado.", StringComparison.Ordinal)
            .Should().BeLessThan(system.IndexOf("Dossiê desta conversa", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SemCadernoOPromptNaoMuda()
    {
        _caderno.ListarAsync(_empresaId, false, Arg.Any<CancellationToken>()).Returns([]);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _requisicoes[0].System.Should().NotContain("Caderno da loja")
            .And.StartWith(PromptAtendimento.Montar(ConfiguracaoAtendimento.CriarPadrao(_empresaId)) + "\n\nDossiê desta conversa");
    }

    [Fact]
    public async Task NaoRespondeQuandoAssumida()
    {
        _conversa.Assumir(Agora);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeFalse();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task ClienteBloqueadoNoMeioDaConversaEscalaSemChamarLlm()
    {
        var cliente = Cliente.Criar(_empresaId, "Maria");
        cliente.Bloquear("golpe", Agora.AddHours(-1));
        _clienteRepository.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        _conversa.VincularCliente(cliente.Id);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeFalse();
        resultado.Escalou.Should().BeTrue();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa, "cliente bloqueado: golpe", Agora, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task NaoChamaLlmQuandoDesligadoEEscalaParaADona()
    {
        _llm.Disponivel.Returns(false);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeFalse();
        resultado.Escalou.Should().BeTrue();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        // #1288: "fica para a dona" precisa assumir de fato, senão a conversa fica sem ninguém e fora do lembrete.
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa,
            Arg.Is<string>(m => m.Contains("desligado")), Agora, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task TimeoutDoLlmComCtVivoEscala()
    {
        // #1288: o timeout do HttpClient chega como TaskCanceledException com o ct do turno vivo.
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new TaskCanceledException("timeout do HttpClient"));
        _escalador.When(e => e.EscalarAsync(Arg.Any<Guid>(), Arg.Any<Conversa>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(ci => ci.Arg<Conversa>().Assumir(ci.Arg<DateTime>()));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeTrue();
        resultado.Escalou.Should().BeTrue();
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa,
            Arg.Is<string>(m => m.Contains("falha ao chamar o LLM")), Agora, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task CancelamentoDoTurnoPropaga()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(ci => throw new OperationCanceledException(ci.Arg<CancellationToken>()));

        var act = () => CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task TimeoutNaFerramentaViraResultadoDeErro()
    {
        _consultarPedido.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TaskCanceledException("timeout"));
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(UsoFerramenta("consultar_pedido"), Texto("Vou confirmar com a cozinha."));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeTrue();
        _requisicoes[1].Mensagens[^1].Conteudo.OfType<BlocoResultadoFerramentaLlm>().Single().EhErro.Should().BeTrue();
    }

    [Fact]
    public async Task TimeoutNoEnvioGravaMensagemComFalha()
    {
        _cloudClient.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<EnvioWhatsAppResult>(_ => throw new TaskCanceledException("timeout"));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeFalse();
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Agente && m.Status == StatusMensagem.Falhou),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task MensagemEmRajadaNaoProcessadaChamaLlm()
    {
        // #1288: a 2ª mensagem chegou com o timestamp da Meta (truncado em segundos) enquanto o turno 1
        // rodava; a resposta do turno 1 ficou com EnviadaEm do início do turno, depois dela na ordenação.
        var t = Agora.AddMinutes(-1);
        _historico.Clear();
        var primeira = Mensagem.Entrada(_empresaId, _conversa.Id, t, TipoConteudoMensagem.Texto, "oi", "wamid.in1");
        primeira.MarcarProcessada(t.AddSeconds(1));
        var segunda = Mensagem.Entrada(_empresaId, _conversa.Id, t, TipoConteudoMensagem.Texto, "quero um bolo de cenoura", "wamid.in2");
        _historico.Add(primeira);
        _historico.Add(segunda);
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, t.AddSeconds(1),
            TipoConteudoMensagem.Texto, "Olá! Como posso ajudar?", "wamid.out0"));
        _conversaRepository.ObterMensagemPorExternoIdAsync(_empresaId, "wamid.in2", Arg.Any<CancellationToken>()).Returns(segunda);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeTrue();
        var mensagens = _requisicoes.Should().ContainSingle().Subject.Mensagens;
        mensagens[^1].Papel.Should().Be(MensagemLlm.Usuario);
        mensagens[^1].Conteudo.OfType<BlocoTextoLlm>().Single().Texto.Should().Be("quero um bolo de cenoura");
        segunda.ProcessadaEm.Should().Be(Agora);
    }

    [Fact]
    public async Task EntradasJaProcessadasNaoChamamLlm()
    {
        // Job repetido depois de o turno marcar a entrada: não responde de novo.
        _historico[0].MarcarProcessada(Agora.AddSeconds(-50));
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora.AddSeconds(-50),
            TipoConteudoMensagem.Texto, "Já respondi", "wamid.out0"));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeFalse();
    }

    [Fact]
    public async Task EntradaRespondidaPelaDonaNaoEPendente()
    {
        _historico[0].MarcarProcessada(Agora.AddSeconds(-50));
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddSeconds(-40), TipoConteudoMensagem.Texto,
            "tem de chocolate?", "wamid.in2"));
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Dona, Agora.AddSeconds(-30),
            TipoConteudoMensagem.Texto, "Tem sim!", "wamid.dona1"));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeFalse();
    }

    [Fact]
    public async Task DonaAssumeDuranteOTurnoNaoEnvia()
    {
        // #1288: a dona escreveu pelo console (Assumir em outro escopo) enquanto o LLM respondia.
        _llm.When(l => l.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()))
            .Do(_ => _situacaoNoBanco = SituacaoConversa.Assumida);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeTrue();
        resultado.Respondeu.Should().BeFalse();
        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await _conversaRepository.DidNotReceive().AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Agente), Arg.Any<CancellationToken>());
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
        await _usoIaRepository.Received(1).AddAsync(Arg.Any<UsoIa>());
        await _unitOfWork.Received(1).CommitAsync();
    }

    [Fact]
    public async Task DonaAssumeDuranteOTurnoNaoEnviaFraseDeEsperaNemEscala()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(_ => UsoFerramenta("consultar_pedido", "toolu_" + _requisicoes.Count));
        _situacaoNoBanco = SituacaoConversa.Assumida;

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeFalse();
        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    [Theory]
    [InlineData(CanalConversa.Instagram, "IGSID-9")]
    [InlineData(CanalConversa.Messenger, "PSID-3")]
    [InlineData(CanalConversa.ChatSite, "sessao-1")]
    public async Task AgenteNaoRespondeForaDoWhatsApp(CanalConversa canal, string contato)
    {
        // S35: o agente envia pelo cliente do WhatsApp; em outro canal sairia pelo canal errado.
        var conversa = Conversa.Abrir(_empresaId, contato, Agora.AddMinutes(-1), canal: canal);
        conversa.RegistrarEntrada(Agora.AddMinutes(-1));
        _conversaRepository.ObterComMensagensAsync(_empresaId, conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ConversaComMensagens(conversa, [Mensagem.Entrada(_empresaId, conversa.Id, Agora.AddMinutes(-1),
                TipoConteudoMensagem.Texto, "oi", "mid-1")]));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeFalse();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        // #1288: sem agente no canal, a conversa passa de fato para a dona.
        resultado.Escalou.Should().BeTrue();
        await _escalador.Received(1).EscalarAsync(_empresaId, conversa, Arg.Is<string>(m => m.Contains(canal.ToString())),
            Agora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NaoRespondeForaDaJanela24h()
    {
        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora.AddHours(25));

        resultado.ChamouLlm.Should().BeFalse();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
    }

    [Fact]
    public async Task LimiteDeIteracoesEscala()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(_ => UsoFerramenta("consultar_pedido", "toolu_" + _requisicoes.Count));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Escalou.Should().BeTrue();
        _requisicoes.Should().HaveCount(AgenteAtendimentoService.MaximoIteracoes);
        var espera = ConfiguracaoAtendimento.CriarPadrao(_empresaId).FraseEspera;
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, espera, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotaInternaMarcadaComoInterno()
    {
        _historico.Insert(0, Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Sistema, Agora.AddMinutes(-2),
            TipoConteudoMensagem.Texto, "cliente pediu desconto na última compra"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var requisicao = _requisicoes.Should().ContainSingle().Subject;
        requisicao.System.Should().Contain("[interno] cliente pediu desconto na última compra");
        requisicao.Mensagens.SelectMany(m => m.Conteudo).OfType<BlocoTextoLlm>()
            .Should().NotContain(b => b.Texto.Contains("desconto"));
    }

    [Fact]
    public async Task DossieDoClienteEntraNoSystemComNotasInterno()
    {
        var cliente = Cliente.Criar(_empresaId, "Maria");
        cliente.AdicionarTag("vegano", OrigemClienteTag.Dona, Agora);
        _clienteRepository.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        _clienteRepository.GetByIdWithDetailsAsync(_empresaId, cliente.Id).Returns(cliente);
        _crm.ObterComTagsAsync(_empresaId, cliente.Id, Arg.Any<CancellationToken>()).Returns(cliente);
        _crm.ListarNotasAsync(_empresaId, cliente.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([ClienteNota.Criar(_empresaId, cliente.Id, "não gosta de coco", "Baba", Agora)]);
        _conversa.VincularCliente(cliente.Id);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var requisicao = _requisicoes.Should().ContainSingle().Subject;
        requisicao.System.Should().Contain("vegano");
        requisicao.System.Should().Contain("[interno] ").And.Contain("não gosta de coco");
        requisicao.System.Split('\n').Where(l => l.Contains("coco"))
            .Should().OnlyContain(l => l.StartsWith("- [interno]"));
    }

    [Fact]
    public async Task UltimaMensagemNaoEDoClienteNaoResponde()
    {
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora.AddSeconds(-30),
            TipoConteudoMensagem.Texto, "Já respondi", "wamid.out0"));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeFalse();
    }
}
