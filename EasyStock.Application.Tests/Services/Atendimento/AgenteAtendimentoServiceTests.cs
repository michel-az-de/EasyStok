using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Sales;
using Microsoft.Extensions.Configuration;
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
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
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

        // #1889: o padrão nasce desligado; estes cenários exercitam o agente ligado pela dona.
        var ligada = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        ligada.Ativo = true;
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns(ligada);
        _cloudClient.EnviarTextoAsync(WaId, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EnvioWhatsAppResult("wamid.out1"));

        _consultarPedido.Nome.Returns("consultar_pedido");
        _consultarPedido.Descricao.Returns("Consulta o pedido");
        _consultarPedido.SchemaJson.Returns("""{"type":"object","properties":{}}""");
        _consultarPedido.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns("""{"status":"EmPreparo","previsao":"15:40"}""");
    }

    private AgenteAtendimentoService CriarServico(params IFerramentaAgente[] adicionais) => new(
        _llm, _conversaRepository, _configuracaoRepository, _clienteRepository,
        [_consultarPedido, .. adicionais], _escalador, _cloudClient, _usoIaRepository, _unitOfWork,
        new ObterDossieClienteUseCase(_clienteRepository, _crm, _historicoPedidos, _domicilio, _conversaRepository),
        _caderno, NullLogger<AgenteAtendimentoService>.Instance, _pedidos);

    private static RespostaLlm Texto(string texto) =>
        new("end_turn", [new BlocoTextoLlm(texto)], 100, 20);

    private static RespostaLlm UsoFerramenta(string nome, string id = "toolu_1") =>
        new(RespostaLlm.StopToolUse,
            [new BlocoTextoLlm("Vou verificar."), new BlocoUsoFerramentaLlm(id, nome, JsonDocument.Parse("{}").RootElement)],
            80, 10);

    [Fact]
    public async Task ExecutaFerramentaEResponde()
    {
        VerificarConsulta("Seu pedido fica pronto por volta das 15h40.");
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
    public async Task FotoAntigaNaoViraPedidoNovoEDespedidaFechaUmaVez()
    {
        _situacaoNoBanco = SituacaoConversa.Automatica;
        _historico.Clear();
        var foto = Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddDays(-2), TipoConteudoMensagem.Imagem,
            "foto do teste anterior", "wamid.foto-antiga");
        foto.MarcarProcessada(Agora.AddDays(-2).AddSeconds(1));
        _historico.Add(foto);
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora.AddDays(-2).AddSeconds(2),
            TipoConteudoMensagem.Texto, "Foto recebida.", "wamid.resposta-antiga"));
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "Obrigado, era só isso. Até mais!", "wamid.despedida"));
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(UsoFerramenta("encerrar_conversa"));
        var servico = CriarServico(new EncerrarConversaFerramenta());

        var resultado = await servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeTrue();
        _conversa.Situacao.Should().Be(SituacaoConversa.Encerrada);
        var textos = _requisicoes.Single().Mensagens.SelectMany(m => m.Conteudo.OfType<BlocoTextoLlm>()).Select(b => b.Texto).ToList();
        textos.Should().Contain(t => t.StartsWith("[histórico de ") && t.Contains("foto do teste anterior"));
        textos[^1].Should().StartWith("[entrada nova de ").And.Contain("Obrigado, era só isso. Até mais!");
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, "Combinado. Até a próxima!", Arg.Any<string?>(), Arg.Any<CancellationToken>());
        (await servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora)).ChamouLlm.Should().BeFalse();
    }
    [Fact]
    public async Task AudioTranscritoChegaComoTextoAoAgente()
    {
        // #1398: com transcrição, o agente lê o que o cliente falou no lugar do marcador genérico.
        _historico.Clear();
        var audio = Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Audio,
            externoId: "wamid.audio1");
        audio.RegistrarTranscricao("quero dois bolos de cenoura");
        _historico.Add(audio);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _requisicoes[0].Mensagens.Single().Conteudo.OfType<BlocoTextoLlm>().Single().Texto
            .Should().Contain("[áudio] quero dois bolos de cenoura");
    }

    [Fact]
    public async Task RespostaDoAgenteSaiSemTravessao()
    {
        // #1330: travessão vira pontuação simples antes de ir ao WhatsApp e ao histórico.
        VerificarConsulta("Temos ravioli de carne — R$ 22,50.");
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(UsoFerramenta("consultar_pedido"), Texto("Texto livre ignorado."));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        await _cloudClient.Received(1).EnviarTextoAsync(WaId, "Temos ravioli de carne, R$ 22,50.",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _conversaRepository.Received(1).AddMensagemAsync(
            Arg.Is<Mensagem>(m => m.Autor == AutorMensagem.Agente && m.Texto == "Temos ravioli de carne, R$ 22,50."),
            Arg.Any<CancellationToken>());
    }

    private void VerificarConsulta(string texto) =>
        _consultarPedido.When(f => f.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>()))
            .Do(c => c.Arg<ContextoTurnoAgente>().RegistrarRespostaVerificada(texto));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TextoLivreOuJsonDeFerramentaSemRespostaVerificadaEncaminhaParaEquipe(bool consultar)
    {
        var falso = Texto("Seu estorno já caiu na conta.");
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(consultar ? UsoFerramenta("consultar_pedido") : falso, falso);
        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        resultado.Escalou.Should().BeTrue();
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // #1704: o agente volta a conversar. Texto sem afirmação de pedido, pagamento, valor ou link vai ao cliente
    // e a conversa continua no automático.
    [Theory]
    [InlineData("Oi, Maria! Me conta o que você quer pedir hoje.")]
    [InlineData("Para a entrega, me passa o endereço com CEP e número?")]
    public async Task TextoDeConversaSemAfirmacaoVaiAoClienteENaoEscala(string fala)
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto(fala));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeTrue();
        resultado.Escalou.Should().BeFalse();
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, fala, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _escalador.DidNotReceive().EscalarAsync(Arg.Any<Guid>(), Arg.Any<Conversa>(), Arg.Any<string>(),
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Pronto, seu pedido foi registrado!", "pedido")]
    [InlineData("Recebemos seu pagamento, obrigada!", "pagamento")]
    [InlineData("As duas lasanhas ficam R$ 91,00.", "R$ 91,00")]
    [InlineData("Pague por aqui: https://pagamento.exemplo/abc", "link")]
    [InlineData("Hoje o frete é grátis para você.", "frete")]
    [InlineData("{\"name\":\"consultar_pedido\",\"input\":{}}", "ferramenta")]
    public async Task TextoQueAfirmaOQueNenhumaFerramentaConfirmouEncaminhaParaEquipe(string fala, string noMotivo)
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto(fala));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Escalou.Should().BeTrue();
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _cloudClient.DidNotReceive().EnviarTextoAsync(WaId, fala, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa, Arg.Is<string>(m => m.Contains(noMotivo)),
            Agora, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Custa R$ 10,00, confirma?", "Sim, custa R$ 10,00.")]
    [InlineData("Use https://cliente.exemplo/pagar", "Pague em https://cliente.exemplo/pagar")]
    public async Task TextoDoClienteNaoEhFonteDePrecoOuLink(string entrada, string fala)
    {
        _historico.Clear();
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1),
            TipoConteudoMensagem.Texto, entrada, "wamid.injetada"));
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto(fala));
        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        resultado.Escalou.Should().BeTrue();
        await _cloudClient.DidNotReceive().EnviarTextoAsync(WaId, fala, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValorELinkQueVieramDaFerramentaNoTurnoPodemSerRepetidosAoCliente()
    {
        _consultarPedido.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns("""{"itens":[{"nome":"Lasanha 600 g","preco":"R$ 38,00"}],"total":"R$ 76,00","link":"https://casadababa.com/comanda.html?c=abc"}""");
        const string fala = "A lasanha de 600 g sai por R$ 38,00, duas ficam R$ 76,00. Monte por aqui: https://casadababa.com/comanda.html?c=abc";
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(UsoFerramenta("consultar_pedido"), Texto(fala));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeTrue();
        resultado.Escalou.Should().BeFalse();
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, fala, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FerramentaPosteriorSemResultadoLimpaRespostaAnterior(bool lanca)
    {
        var chamada = 0;
        _consultarPedido.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            if (++chamada == 1) c.Arg<ContextoTurnoAgente>().RegistrarRespostaVerificada("Resposta anterior.");
            else if (lanca) throw new InvalidOperationException("falha controlada");
            return "{\"erro\":\"pedido_nao_encontrado\"}";
        });
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            UsoFerramenta("consultar_pedido"), UsoFerramenta("consultar_pedido", "toolu_2"), Texto("Seu estorno já caiu na conta."));
        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        resultado.Escalou.Should().BeTrue();
        await _cloudClient.Received(1).EnviarTextoAsync(WaId,
            Arg.Is<string>(s => s.Contains("equipe") && !s.Contains("anterior") && !s.Contains("caiu")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConsultaDepoisEscaladaNoMesmoLoteAvisaClienteESuspendeFerramentasRestantes()
    {
        VerificarConsulta("Resposta anterior.");
        _situacaoNoBanco = SituacaoConversa.Automatica;
        _escalador.When(e => e.EscalarAsync(_empresaId, _conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>()))
            .Do(_ => _conversa.Escalar(Agora, "conferência"));
        var entrada = JsonSerializer.SerializeToElement(new { motivo = "conferência" });
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(RespostaLlm.StopToolUse,
        [
            new BlocoUsoFerramentaLlm("consulta", "consultar_pedido", entrada),
            new BlocoUsoFerramentaLlm("escala", "escalar_para_dona", entrada),
            new BlocoUsoFerramentaLlm("nao_executar", "consultar_pedido", entrada)
        ], 1, 1));
        var resultado = await CriarServico(new EscalarParaDonaFerramenta(_escalador)).ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        resultado.Escalou.Should().BeTrue(); resultado.Respondeu.Should().BeTrue();
        _requisicoes.Should().ContainSingle();
        await _consultarPedido.Received(1).ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>());
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EncerramentoPelaFerramentaEnviaDespedidaFixaENaoExecutaRestanteDoLoteOuReplay()
    {
        _situacaoNoBanco = SituacaoConversa.Automatica;
        var entrada = JsonSerializer.SerializeToElement(new { });
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(RespostaLlm.StopToolUse,
        [new BlocoUsoFerramentaLlm("fechar", "encerrar_conversa", entrada), new BlocoUsoFerramentaLlm("ignorar", "consultar_pedido", entrada)], 1, 1));
        var servico = CriarServico(new EncerrarConversaFerramenta());
        var resultado = await servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        resultado.Respondeu.Should().BeTrue(); resultado.Escalou.Should().BeFalse();
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, "Combinado. Até a próxima!",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _consultarPedido.DidNotReceiveWithAnyArgs().ExecutarAsync(default!, default, default);
        (await servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora)).ChamouLlm.Should().BeFalse();
        _requisicoes.Should().ContainSingle();
    }

    [Fact]
    public async Task EncerramentoComPedidoVinculadoEncaminhaParaEquipeSemDespedidaOuRespostaLivre()
    {
        _situacaoNoBanco = SituacaoConversa.Automatica;
        _escalador.When(e => e.EscalarAsync(_empresaId, _conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>()))
            .Do(_ => _conversa.Escalar(Agora, "pedido vinculado"));
        var pedidoId = Guid.NewGuid();
        _conversa.DefinirPedidoEmAndamento(pedidoId);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(
            UsoFerramenta("encerrar_conversa"),
            new RespostaLlm("end_turn", [new BlocoTextoLlm("Pedido concluído e conversa encerrada.")], 1, 1));
        var servico = CriarServico(new EncerrarConversaFerramenta());

        var resultado = await servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeTrue();
        resultado.Escalou.Should().BeTrue();
        _conversa.Situacao.Should().NotBe(SituacaoConversa.Encerrada);
        _conversa.PedidoEmAndamentoId.Should().Be(pedidoId);
        await _cloudClient.Received(1).EnviarTextoAsync(WaId,
            "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        (await servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora)).ChamouLlm.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PedidoJaFinalizadoSoltaDaConversaAntesDoTurno(bool entregue)
    {
        // #2126: "quero uma lasanha" numa conversa ainda presa à comanda cancelada devolvia a comanda antiga.
        var pedido = Pedido.Criar(_empresaId, origem: "whatsapp");
        if (entregue)
        {
            pedido.MudarStatus(StatusPedido.Preparando);
            pedido.MudarStatus(StatusPedido.Pronto);
            pedido.MudarStatus(StatusPedido.Entregue);
        }
        else pedido.Cancelar();
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        _conversa.DefinirPedidoEmAndamento(pedido.Id);
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddSeconds(-30), TipoConteudoMensagem.Texto,
            "Eu quero uma lasanha", "wamid.in2"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _conversa.PedidoEmAndamentoId.Should().BeNull();
        _requisicoes[0].System.Should().Contain("Pedido em andamento: nenhum")
            .And.NotContain(pedido.Id.ToString());
    }

    [Fact]
    public async Task PedidoEmAndamentoContinuaVinculado()
    {
        var pedido = Pedido.Criar(_empresaId, origem: "whatsapp");
        pedido.MudarStatus(StatusPedido.Preparando);
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        _conversa.DefinirPedidoEmAndamento(pedido.Id);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _conversa.PedidoEmAndamentoId.Should().Be(pedido.Id);
        _requisicoes[0].System.Should().Contain(pedido.Id.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnvioEfetivoNaoDuplicaRespostaENaoPerdeHistoricoQuandoFerramentaPosteriorFalha(bool falhaPosterior)
    {
        _consultarPedido.When(f => f.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>()))
            .Do(c => c.Arg<ContextoTurnoAgente>().RegistrarEnvioVerificado());
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(UsoFerramenta("consultar_pedido"),
            falhaPosterior ? UsoFerramenta("inexistente", "toolu_2") : Texto("Texto livre ignorado."), Texto("Texto livre ignorado."));
        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        resultado.Respondeu.Should().BeTrue(); resultado.Escalou.Should().Be(falhaPosterior);
        if (falhaPosterior)
            await _cloudClient.Received(1).EnviarTextoAsync(WaId, Arg.Is<string>(s => s.Contains("equipe")), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        else
            await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
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
    public async Task AtendimentoAutomaticoDesligadoNaConfiguracaoPassaParaADonaSemChamarOLlm()
    {
        // #1475: o interruptor "Atendimento automático" do console grava Ativo; desligado, o agente não responde.
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        configuracao.Atualizar(null, null, null, null, null, null, null, null, ativo: false);
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns(configuracao);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeFalse();
        resultado.Respondeu.Should().BeFalse();
        resultado.Escalou.Should().BeTrue();
        await _llm.DidNotReceiveWithAnyArgs().EnviarAsync(default!, default);
        await _cloudClient.DidNotReceiveWithAnyArgs().EnviarTextoAsync(default!, default!, default, default);
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa,
            AgenteAtendimentoService.MotivoAtendimentoAutomaticoDesligado, Agora, Arg.Any<CancellationToken>());
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
        // #1791: a resposta é gravada logo depois do envio; o fim do turno faz o segundo commit.
        await _unitOfWork.Received(2).CommitAsync();
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
        // #1791: a resposta é gravada logo depois do envio; o fim do turno faz o segundo commit.
        await _unitOfWork.Received(2).CommitAsync();
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
        mensagens[^1].Conteudo.OfType<BlocoTextoLlm>().Single().Texto.Should().StartWith("[entrada nova de ").And.EndWith("quero um bolo de cenoura");
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

    // ── #2004 atendimento-03: mensagem que falhou ao enviar não é mensagem entregue ──────────────────────

    private Mensagem SaidaQueFalhou(AutorMensagem autor, DateTime em, string texto, TipoFalhaEnvio tipo = TipoFalhaEnvio.Permanente)
    {
        var saida = Mensagem.Saida(_empresaId, _conversa.Id, autor, em, TipoConteudoMensagem.Texto, texto);
        saida.RegistrarFalhaEnvio("Falha no canal (código Meta 131026).", tipo, em);
        return saida;
    }

    [Fact]
    public async Task EntradaCujaRespostaDaDonaFalhouAoEnviarAindaEPendente()
    {
        // A resposta da dona nunca chegou ao cliente: a entrada dele segue sem resposta e o agente precisa atender.
        _historico[0].MarcarProcessada(Agora.AddSeconds(-50));
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddSeconds(-40), TipoConteudoMensagem.Texto,
            "tem de chocolate?", "wamid.in2"));
        _historico.Add(SaidaQueFalhou(AutorMensagem.Dona, Agora.AddSeconds(-30), "Tem sim!"));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeTrue();
    }

    [Theory]
    [InlineData(AutorMensagem.Agente, TipoFalhaEnvio.Permanente)]
    [InlineData(AutorMensagem.Dona, TipoFalhaEnvio.Permanente)]
    public async Task SaidaQueFalhouDeVezNaoEntraNaConversaDoLlm(AutorMensagem autor, TipoFalhaEnvio tipo)
    {
        // Senão o agente responde "já te mandei o link acima" sobre algo que o cliente nunca recebeu.
        _historico[0].MarcarProcessada(Agora.AddMinutes(-3));
        _historico.Add(SaidaQueFalhou(autor, Agora.AddMinutes(-2), "Aqui está o link do cardápio: https://x.test/cardapio", tipo));
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "e o link?", "wamid.in2"));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.ChamouLlm.Should().BeTrue();
        var textos = _requisicoes.Should().ContainSingle().Subject.Mensagens
            .SelectMany(m => m.Conteudo.OfType<BlocoTextoLlm>()).Select(b => b.Texto).ToList();
        textos.Should().NotContain(t => t.Contains("link do cardápio"));
        textos.Should().Contain(t => t.Contains("e o link?"));
    }

    [Theory]
    [InlineData(TipoFalhaEnvio.Incerta, "(sem resposta do canal)")]
    [InlineData(TipoFalhaEnvio.Temporaria, "reenvio agendado")]
    public async Task SaidaIncertaOuComReenvioAgendadoEntraNoLlmMarcadaComoNaoConfirmada(TipoFalhaEnvio tipo, string _)
    {
        // Pode ter chegado (incerta) ou vai chegar sozinha (reenvio agendado): some do histórico só a falha definitiva.
        _historico[0].MarcarProcessada(Agora.AddMinutes(-3));
        _historico.Add(SaidaQueFalhou(AutorMensagem.Agente, Agora.AddMinutes(-2), "Seu pedido sai às 15h.", tipo));
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "e agora?", "wamid.in2"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _requisicoes.Single().Mensagens.SelectMany(m => m.Conteudo.OfType<BlocoTextoLlm>()).Select(b => b.Texto)
            .Should().Contain(t => t.Contains("[não confirmada]") && t.Contains("Seu pedido sai às 15h."));
    }

    [Fact]
    public async Task SaidaTemporariaSemReenvioAgendadoEFalhaDefinitivaNaoEntraNoLlm()
    {
        _historico[0].MarcarProcessada(Agora.AddMinutes(-3));
        var semReenvio = Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora.AddMinutes(-2),
            TipoConteudoMensagem.Texto, "Seu pedido sai às 15h.");
        semReenvio.RegistrarFalhaEnvio("Falha no canal.", TipoFalhaEnvio.Temporaria, Agora.AddMinutes(-2), reenvioAutomatico: false);
        _historico.Add(semReenvio);
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "e agora?", "wamid.in2"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _requisicoes.Single().Mensagens.SelectMany(m => m.Conteudo.OfType<BlocoTextoLlm>()).Select(b => b.Texto)
            .Should().NotContain(t => t.Contains("Seu pedido sai às 15h."));
    }

    [Fact]
    public async Task SaidaReenviadaComSucessoVoltaParaAConversaDoLlm()
    {
        // Controle: a falha que o reenvio resolveu (Status Enviada) é mensagem entregue como as outras.
        _historico[0].MarcarProcessada(Agora.AddMinutes(-3));
        var reenviada = SaidaQueFalhou(AutorMensagem.Agente, Agora.AddMinutes(-2), "Seu pedido sai às 15h.", TipoFalhaEnvio.Temporaria);
        reenviada.RegistrarReenviada("wamid.reenvio");
        _historico.Add(reenviada);
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "obrigada!", "wamid.in2"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _requisicoes.Single().Mensagens.SelectMany(m => m.Conteudo.OfType<BlocoTextoLlm>()).Select(b => b.Texto)
            .Should().Contain(t => t.Contains("Seu pedido sai às 15h."));
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
        var espera = "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.";
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

    // #1791: banco simulado. Só o que passou por um commit bem-sucedido fica visível para o webhook
    // (status e eco procuram a mensagem pelo wamid); commit que falha não grava nada.
    private readonly List<Mensagem> _pendentes = [];
    private readonly List<Mensagem> _gravadas = [];
    private readonly HashSet<string> _entradasProcessadasGravadas = [];
    private Func<bool> _commitFalha = () => false;

    private void SimularBanco()
    {
        _conversaRepository.When(r => r.AddMensagemAsync(Arg.Any<Mensagem>(), Arg.Any<CancellationToken>()))
            .Do(c => _pendentes.Add(c.Arg<Mensagem>()));
        _conversaRepository.ObterMensagemPorExternoIdAsync(_empresaId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(c => _historico.FirstOrDefault(m => m.ExternoId == c.ArgAt<string>(1)));
        _unitOfWork.CommitAsync().Returns(_ =>
        {
            if (_commitFalha()) throw new InvalidOperationException("banco fora do ar");
            _gravadas.AddRange(_pendentes);
            _pendentes.Clear();
            foreach (var entrada in _historico.Where(m => m.Autor == AutorMensagem.Cliente && m.ProcessadaEm is not null))
                _entradasProcessadasGravadas.Add(entrada.ExternoId!);
            return 1;
        });
    }

    private EnviarCardapioImagemFerramenta CriarFerramentaCardapio()
    {
        var storefront = Substitute.For<IStorefrontRepository>();
        _cloudClient.EnviarTextoAsync(WaId, Arg.Is<string>(t => t.Contains("comanda", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new EnvioWhatsAppResult("wamid.cardapio"));
        var saudacao = new SaudacaoAtendimento(storefront, new ConfigurationBuilder().Build());
        return new EnviarCardapioImagemFerramenta(storefront,
            new LinkCardapioConversaService(new LinkCardapioConversaServiceTests.RepositorioEmMemoria(), saudacao),
            _cloudClient, _conversaRepository);
    }

    [Fact]
    public async Task SaidaDaFerramentaFicaGravadaAntesDeOLlmContinuar()
    {
        // #1791: o status (sent/failed) e o eco da comanda chegam pelo webhook enquanto o LLM ainda
        // responde. Se a mensagem só fosse gravada no fim do turno, o status cairia em "wamid
        // desconhecido" (sem reenvio S57 nem SMS S60) e o eco viraria Mensagem(Dona) assumindo a conversa.
        SimularBanco();
        var ferramenta = CriarFerramentaCardapio();
        string?[] gravadasNaContinuacao = [];
        var entradaProcessadaNaContinuacao = false;
        var chamada = 0;
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++chamada == 1) return UsoFerramenta("enviar_cardapio_imagem");
            gravadasNaContinuacao = _gravadas.Select(m => m.ExternoId).ToArray();
            entradaProcessadaNaContinuacao = _entradasProcessadasGravadas.Contains("wamid.in1");
            return Texto("Texto livre ignorado.");
        });

        var resultado = await CriarServico(ferramenta).ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Respondeu.Should().BeTrue();
        gravadasNaContinuacao.Should().Contain("wamid.cardapio");
        entradaProcessadaNaContinuacao.Should().BeTrue();
    }

    [Fact]
    public async Task CommitFinalQueFalhaNaoFazOProximoTurnoResponderDeNovo()
    {
        // #1791: a comanda já saiu pela Meta; se o commit do fim do turno falhar, as entradas
        // precisam continuar processadas, senão o próximo job responde de novo ao cliente.
        SimularBanco();
        var ferramenta = CriarFerramentaCardapio();
        var chamadas = 0;
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ =>
            ++chamadas == 1 ? UsoFerramenta("enviar_cardapio_imagem") : Texto("Texto livre ignorado."));
        // Falha o commit do fim do turno, depois de o LLM concluir.
        _commitFalha = () => chamadas >= 2;
        var servico = CriarServico(ferramenta);

        var primeiro = () => servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        await primeiro.Should().ThrowAsync<InvalidOperationException>();

        // O próximo job relê o banco: só o que foi gravado.
        var entrada = Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-1), TipoConteudoMensagem.Texto,
            "quanto tempo falta?", "wamid.in1");
        if (_entradasProcessadasGravadas.Contains("wamid.in1")) entrada.MarcarProcessada(Agora);
        _historico.Clear();
        _historico.Add(entrada);
        _historico.AddRange(_gravadas);
        _requisicoes.Clear();
        _commitFalha = () => false;

        var segundo = await servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora.AddSeconds(5));

        segundo.ChamouLlm.Should().BeFalse();
        _gravadas.Select(m => m.ExternoId).Should().Contain("wamid.cardapio");
    }
}
