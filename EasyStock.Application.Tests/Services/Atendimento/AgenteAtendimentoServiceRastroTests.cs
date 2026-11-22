using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Tests.Services.Auth;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3121: produção só grava de Warning para cima. Cada decisão do Assistente que tira o cliente do fluxo automático deixa
/// uma linha com evento estável, e a retomada avisa o console em tempo real.
/// </summary>
public partial class AgenteAtendimentoServiceTests
{
    private readonly LoggerQueGuarda<AgenteAtendimentoService> _log = new();
    private readonly IOperacaoEventPublisher _eventos = Substitute.For<IOperacaoEventPublisher>();

    private IEnumerable<int> Avisos => _log.Eventos.Where(e => e.Nivel == LogLevel.Warning).Select(e => e.Evento.Id);

    [Fact]
    public async Task Rastro_PortaoDaJanelaLogaAvisoComOCodigo()
    {
        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora.AddHours(25));

        Avisos.Should().Contain(LogsDoAssistente.EventoPortaoBarrou);
        _log.Linhas.Should().Contain(l => l.Contains(PortoesDoAssistente.Janela));
    }

    [Fact]
    public async Task Rastro_ClienteBloqueadoLogaAvisoComOCodigoDoPortao()
    {
        var cliente = Cliente.Criar(_empresaId, "Maria");
        cliente.Bloquear("golpe", Agora.AddHours(-1));
        _clienteRepository.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        _conversa.VincularCliente(cliente.Id);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        Avisos.Should().Contain(LogsDoAssistente.EventoPortaoBarrou);
        _log.Linhas.Should().Contain(l => l.Contains(PortoesDoAssistente.Bloqueado));
        _log.Linhas.Should().NotContain(l => l.Contains("Maria") || l.Contains("golpe"), "nome e motivo não vão ao log");
    }

    [Theory]
    [InlineData("Seu estorno já caiu na conta.", "afirma pagamento ou devolução sem a ferramenta confirmar")]
    [InlineData("O frete já está acertado.", "afirma frete combinado sem a ferramenta confirmar")]
    [InlineData("Vou pedir para a dona verificar isso com você.", "promete encaminhamento sem acionar a equipe")]
    [InlineData("Veja em https://exemplo.com/x", "link sem origem em ferramenta")]
    [InlineData("   ", "o agente não produziu resposta")]
    [InlineData("Vou chamar escalar_para_dona agora.", "texto com chamada de ferramenta no lugar da resposta")]
    [InlineData("Seu pedido já está registrado.", "afirma situação do pedido sem a ferramenta confirmar")]
    [InlineData("Sem problema, o frete é grátis para você.", "promete frete grátis ou desconto, que é decisão da equipe")]
    public void Rastro_MotivoFixoDaGuardaTemCategoriaPropria(string resposta, string esperado)
    {
        var motivo = GuardaRespostaAgente.Avaliar(resposta, [], ["escalar_para_dona"]);

        motivo.Should().Be(esperado, "cada resposta cai no seu motivo, não em qualquer um da lista");
        LogsDoAssistente.CategoriaDaGuarda(motivo!).Should().Be(esperado, "se o texto da guarda mudar, a lista do log acompanha");
    }

    [Fact]
    public void Rastro_FerramentaQueFalhouTemCategoriaPropria() =>
        LogsDoAssistente.CategoriaDaGuarda(LogsDoAssistente.CategoriaFerramentaFalhou)
            .Should().Be(LogsDoAssistente.CategoriaFerramentaFalhou).And.NotBe(LogsDoAssistente.CategoriaOutra);

    [Fact]
    public void Rastro_MotivoDesconhecidoNaoVaiAoLog() =>
        LogsDoAssistente.CategoriaDaGuarda("cliente Maria pediu 59").Should().Be(LogsDoAssistente.CategoriaOutra);

    [Fact]
    public async Task Rastro_ConversaComAEquipeNaoLogaAviso_RodaACadaMensagemDoCliente()
    {
        _conversa.Assumir(Agora.AddMinutes(-5), Guid.NewGuid());
        PortoesDoAssistente.DaConversa(_conversa, Agora).First(e => e.Barra).Codigo.Should().Be(PortoesDoAssistente.Situacao);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado, "o turno parou no portão, não antes nem depois");
        _requisicoes.Should().BeEmpty();
        Avisos.Should().NotContain(LogsDoAssistente.EventoPortaoBarrou);
    }

    [Fact]
    public async Task Rastro_VinculoEmRevisaoNaoLogaAviso_RodaACadaMensagemDoCliente()
    {
        _conversa.ExigirConferenciaCliente();
        PortoesDoAssistente.DaConversa(_conversa, Agora).First(e => e.Barra).Codigo.Should().Be(PortoesDoAssistente.Vinculo);

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado, "o turno parou no portão, não antes nem depois");
        _requisicoes.Should().BeEmpty();
        Avisos.Should().NotContain(LogsDoAssistente.EventoPortaoBarrou);
    }

    // Limite e cota tiram a conversa do automático como os outros portões: mesmo evento, mesmo código de busca.
    [Fact]
    public async Task Rastro_PortaoDoLimiteLogaAvisoComOCodigo()
    {
        RespostasDoAgenteNaUltimaHora(LimiteRespostasAgenteOptions.PadraoPorHora);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _log.Eventos.Where(e => e.Nivel == LogLevel.Warning).Select(e => e.Evento.Id)
            .Should().Equal(LogsDoAssistente.EventoPortaoBarrou);
        _log.Linhas.Should().Contain(l => l.Contains("Portao=" + PortoesDoAssistente.Limite));
    }

    [Fact]
    public async Task Rastro_PortaoDaCotaLogaAvisoComOCodigo()
    {
        LojaComCota(1_000);
        UsoDoMes(1_000);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        _log.Eventos.Where(e => e.Nivel == LogLevel.Warning).Select(e => e.Evento.Id)
            .Should().Equal(LogsDoAssistente.EventoPortaoBarrou);
        _log.Linhas.Should().Contain(l => l.Contains("Portao=" + PortoesDoAssistente.Cota));
    }

    [Fact]
    public async Task Rastro_RespostaBarradaLogaACategoriaSemOValor()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("A lasanha sai por R$ 59,00."));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        Avisos.Should().Contain(LogsDoAssistente.EventoGuardaBarrou);
        _log.Linhas.Should().Contain(l => l.Contains("valor sem origem em ferramenta"));
        // As linhas guardam também os ids do turno em hexadecimal: procurar "59" quebraria por acaso num Guid.
        _log.Linhas.Should().NotContain(l => l.Contains("R$") || l.Contains("lasanha"), "o log não leva trecho da resposta");
    }

    // #3149: dicionário gravado que a leitura descarta não some calado; o log leva o fato, não o conteúdo.
    [Fact]
    public async Task Rastro_DicionarioDescartadoLogaAvisoSemOConteudo()
    {
        LojaLigada().DefinirDicionario("""[{"termo":"segredo da casa","significado":"linha\nforjada"}]""");
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("Oi! Como posso ajudar?"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        Avisos.Should().ContainSingle(id => id == LogsDoAssistente.EventoDicionarioDescartado);
        _log.Linhas.Should().NotContain(l => l.Contains("segredo da casa") || l.Contains("forjada"));
    }

    [Fact]
    public async Task Rastro_DicionarioValidoOuAusenteNaoLogaAviso()
    {
        LojaLigada().DefinirDicionario(DicionarioDaLoja.Serializar([new("PF", "prato feito do dia")]));
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("Oi! Como posso ajudar?"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        Avisos.Should().NotContain(LogsDoAssistente.EventoDicionarioDescartado);
    }

    [Fact]
    public async Task Rastro_TurnoInterrompidoLogaAviso()
    {
        var aguardando = new TaskCompletionSource<RespostaLlm>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ => { chamou.SetResult(); return aguardando.Task; });
        var turno = CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        await chamou.Task;
        _conversa.VincularCliente(Guid.NewGuid());
        aguardando.SetResult(Texto("Resposta da pessoa anterior."));
        await turno;

        Avisos.Should().Contain(LogsDoAssistente.EventoTurnoInterrompido);
        _log.Linhas.Should().NotContain(l => l.Contains("Resposta da pessoa anterior"));
    }

    [Fact]
    public async Task Rastro_RetomadaDispensadaLogaAvisoComOCodigoDoPortao()
    {
        EscaladaPeloRoboHa(AlemDoPrazo);
        var desligada = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        desligada.Ativo = false;
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns(desligada);

        await Retomar();

        Avisos.Should().Contain(LogsDoAssistente.EventoRetomadaDispensada);
        await _eventos.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task Rastro_RetomadaAvisaOConsoleEmTempoReal()
    {
        EscaladaPeloRoboHa(AlemDoPrazo);
        var servico = CriarServicoComRetomada();
        var emTransacao = false;
        bool? avisouEmTransacao = null;
        _unitOfWork.ExecuteInTransactionSemRetryAsync(
                Arg.Any<Func<CancellationToken, Task<(ConversaComMensagens, Mensagem)?>>>(), Arg.Any<CancellationToken>())
            .Returns(async c =>
            {
                emTransacao = true;
                try { return await c.Arg<Func<CancellationToken, Task<(ConversaComMensagens, Mensagem)?>>>()(c.Arg<CancellationToken>()); }
                finally { emTransacao = false; }
            });
        _eventos.WhenForAnyArgs(e => e.PublicarAsync(default!, default, default!, default))
            .Do(_ => avisouEmTransacao = emTransacao); // o serviço engole falha do aviso: afirmar aqui dentro não prova nada

        await servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora, retomada: true);

        avisouEmTransacao.Should().BeFalse("dentro da transação o commit ainda não aconteceu e a conversa está travada");

        await _eventos.Received(1).PublicarAsync(EventosOperacao.ConversaAtualizada, _empresaId,
            new ConversaAtualizadaOperacao(_conversa.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rastro_FalhaAoAvisarOConsoleNaoDerrubaARetomada()
    {
        EscaladaPeloRoboHa(AlemDoPrazo);
        _eventos.PublicarAsync(default!, default, default!, default).ReturnsForAnyArgs<Task>(_ => throw new InvalidOperationException("broker"));

        var resultado = await Retomar();

        resultado.ChamouLlm.Should().BeTrue();
        _conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
    }
}
