using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Logging.Abstractions;
using EasyStock.Application.UseCases.Cliente.Dossie;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3068: a equipe não respondeu no prazo e o turno chega marcado como retomada. O próprio turno decide: confere a
/// espera, os portões e a guarda por mensagem, devolve a conversa ao Assistente e responde.
/// </summary>
public partial class AgenteAtendimentoServiceTests
{
    private static readonly TimeSpan AlemDoPrazo = TimeSpan.FromSeconds(61);
    private static readonly TimeSpan AntesDoPrazo = TimeSpan.FromSeconds(30);

    private const string Encaminhamento = "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.";

    private AgenteAtendimentoService CriarServicoComRetomada(int segundos = 60, params IFerramentaAgente[] adicionais)
    {
        // A retomada decide dentro de uma transação, com a conversa travada.
        _unitOfWork.ExecuteInTransactionSemRetryAsync(
                Arg.Any<Func<CancellationToken, Task<(ConversaComMensagens, Mensagem)?>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<(ConversaComMensagens, Mensagem)?>>>()(c.Arg<CancellationToken>()));
        return new(
            _llm, _conversaRepository, _configuracaoRepository, _clienteRepository,
            [_consultarPedido, .. adicionais], _escalador, _cloudClient, _usoIaRepository, _unitOfWork,
            new ObterDossieClienteUseCase(_clienteRepository, _crm, _historicoPedidos, _domicilio, _conversaRepository),
            _caderno, NullLogger<AgenteAtendimentoService>.Instance, _pedidos,
            retomadaOpcoes: Microsoft.Extensions.Options.Options.Create(new RetomadaAssistenteOptions { Segundos = segundos }));
    }

    private Task<ResultadoTurnoAgente> Retomar(int segundos = 60) =>
        CriarServicoComRetomada(segundos).ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora, retomada: true);

    /// <summary>O robô respondeu a entrada com o encaminhamento e passou a conversa para a fila da equipe.</summary>
    private Mensagem EscaladaPeloRoboHa(TimeSpan espera)
    {
        var quando = Agora - espera;
        _historico.Clear();
        var entrada = Mensagem.Entrada(_empresaId, _conversa.Id, quando.AddSeconds(-5), TipoConteudoMensagem.Texto,
            "quero falar com uma pessoa", "wamid.in9");
        entrada.MarcarProcessada(quando);
        _historico.Add(entrada);
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Sistema, quando, TipoConteudoMensagem.Texto,
            EscalarConversaUseCase.PrefixoMotivo + "cliente pediu uma pessoa"));
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, quando, TipoConteudoMensagem.Texto,
            "Vou encaminhar sua mensagem para a equipe conferir e responder por aqui.", "wamid.out9"));
        _conversa.Escalar(quando, "cliente pediu uma pessoa");
        return entrada;
    }

    /// <summary>A atendente assumiu, o cliente escreveu e ninguém respondeu.</summary>
    private Mensagem ClienteEscreveuComAEquipeHa(TimeSpan espera)
    {
        _historico.Clear();
        _conversa.Assumir(Agora.AddMinutes(-10), Guid.NewGuid());
        var entrada = Mensagem.Entrada(_empresaId, _conversa.Id, Agora - espera, TipoConteudoMensagem.Texto,
            "alguém aí?", "wamid.in8");
        _historico.Add(entrada);
        return entrada;
    }

    private IEnumerable<Mensagem> NotasGravadas() => _conversaRepository.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(_conversaRepository.AddMensagemAsync))
        .Select(c => (Mensagem)c.GetArguments()[0]!)
        .Where(m => m.Autor == AutorMensagem.Sistema);

    private static string TextoDe(MensagemLlm mensagem) =>
        string.Join("\n", mensagem.Conteudo.OfType<BlocoTextoLlm>().Select(b => b.Texto));

    [Fact]
    public async Task Retomada_EquipeNaoRespondeuNoPrazo_AssistenteAssumeRespondeEAnota()
    {
        var entrada = EscaladaPeloRoboHa(AlemDoPrazo);

        var resultado = await Retomar();

        resultado.Should().Be(new ResultadoTurnoAgente(ChamouLlm: true, Respondeu: true, Escalou: false));
        _conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        _conversa.RetomadaPorTempoDecidida(entrada.Id).Should().BeTrue();
        NotasGravadas().Should().ContainSingle(m => m.Texto!.StartsWith(RetomadaDoAssistente.PrefixoNota))
            .Which.Texto.Should().Contain("60 s");
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task Retomada_ReabreAEntradaDoClienteEAvisaOModeloParaNaoEscalarDeNovo()
    {
        EscaladaPeloRoboHa(AlemDoPrazo);

        await Retomar();

        var requisicao = _requisicoes.Should().ContainSingle().Subject;
        requisicao.Mensagens[^1].Papel.Should().Be(MensagemLlm.Usuario);
        TextoDe(requisicao.Mensagens[^1]).Should().Contain("[entrada nova").And.Contain("quero falar com uma pessoa");
        requisicao.SystemVolatil.Should().Contain(RetomadaDoAssistente.MarcaNoPrompt)
            .And.Contain("escalar_para_dona");
    }

    [Fact]
    public async Task Retomada_ClienteEscreveuComAEquipeESemResposta_AssistenteAssume()
    {
        var entrada = ClienteEscreveuComAEquipeHa(AlemDoPrazo);

        var resultado = await Retomar();

        resultado.Respondeu.Should().BeTrue();
        _conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        _conversa.AssumidaPorUsuarioId.Should().BeNull();
        _conversa.RetomadaPorTempoDecidida(entrada.Id).Should().BeTrue();
    }

    [Fact]
    public async Task Retomada_AntesDoPrazo_NaoMexeNaConversa()
    {
        EscaladaPeloRoboHa(AntesDoPrazo);

        var resultado = await Retomar();

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _requisicoes.Should().BeEmpty();
        NotasGravadas().Should().BeEmpty();
    }

    [Fact]
    public async Task Retomada_EquipeRespondeuDepoisDaEntrada_NaoRetoma()
    {
        EscaladaPeloRoboHa(TimeSpan.FromMinutes(10));
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Atendente, Agora.AddMinutes(-5),
            TipoConteudoMensagem.Texto, "Oi, aqui é a Thati. Já vejo isso.", "wamid.out10"));

        var resultado = await Retomar();

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _requisicoes.Should().BeEmpty();
    }

    [Fact]
    public async Task Retomada_RoboJaTinhaRespondidoEEquipeSoAssumiu_NaoRetoma()
    {
        _historico.Clear();
        var entrada = Mensagem.Entrada(_empresaId, _conversa.Id, Agora.AddMinutes(-10), TipoConteudoMensagem.Texto,
            "vocês abrem domingo?", "wamid.in7");
        entrada.MarcarProcessada(Agora.AddMinutes(-10));
        _historico.Add(entrada);
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Agente, Agora.AddMinutes(-10),
            TipoConteudoMensagem.Texto, "Abrimos sim, das 10h às 15h.", "wamid.out7"));
        _conversa.Assumir(Agora.AddMinutes(-9), Guid.NewGuid());

        var resultado = await Retomar();

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado, "ninguém está esperando resposta");
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _requisicoes.Should().BeEmpty();
    }

    [Fact]
    public async Task Retomada_MesmaMensagemDoClienteSoUmaVez()
    {
        EscaladaPeloRoboHa(AlemDoPrazo);
        await Retomar();
        // O robô passou de novo para a equipe sem o cliente escrever nada.
        _conversa.Escalar(Agora, "cliente insiste em falar com uma pessoa");

        var segunda = await CriarServicoComRetomada().ProcessarTurnoAsync(
            _empresaId, _conversa.Id, Agora.AddMinutes(5), retomada: true);

        segunda.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _requisicoes.Should().ContainSingle("a segunda retomada da mesma mensagem não chama o modelo");
    }

    [Fact]
    public async Task Retomada_PortaoQueBarra_NaoRetomaNemEscalaDeNovo()
    {
        var entrada = EscaladaPeloRoboHa(AlemDoPrazo);
        var desligada = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        desligada.Ativo = false;
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns(desligada);

        var resultado = await Retomar();

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _conversa.MotivoEscalada.Should().Be("cliente pediu uma pessoa", "a conversa fica como a equipe deixou");
        _conversa.RetomadaPorTempoDecidida(entrada.Id).Should().BeTrue("a varredura não reavalia a mesma mensagem a cada passada");
        _requisicoes.Should().BeEmpty();
        NotasGravadas().Should().BeEmpty();
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    /// <summary>O modelo insiste em passar para a equipe no turno retomado.</summary>
    private Task<ResultadoTurnoAgente> RetomarComOModeloEscalando()
    {
        _situacaoNoBanco = SituacaoConversa.Automatica;
        _escalador.When(e => e.EscalarAsync(_empresaId, _conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>()))
            .Do(_ => _conversa.Escalar(Agora, "cliente insiste em uma pessoa"));
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("escala", "escalar_para_dona", JsonSerializer.SerializeToElement(new { motivo = "cliente insiste em uma pessoa" }))], 1, 1));
        return CriarServicoComRetomada(60, new EscalarParaDonaFerramenta(_escalador))
            .ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora, retomada: true);
    }

    [Fact]
    public async Task Retomada_RoboEscalaDeNovo_NaoRepeteOEncaminhamentoQueOClienteJaRecebeu()
    {
        EscaladaPeloRoboHa(AlemDoPrazo);

        var resultado = await RetomarComOModeloEscalando();

        resultado.Escalou.Should().BeTrue();
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        await _cloudClient.DidNotReceive().EnviarTextoAsync(WaId, Encaminhamento, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retomada_RoboEscalaPelaPrimeiraVez_AvisaOClienteDoEncaminhamento()
    {
        ClienteEscreveuComAEquipeHa(AlemDoPrazo);

        var resultado = await RetomarComOModeloEscalando();

        resultado.Escalou.Should().BeTrue();
        await _cloudClient.Received(1).EnviarTextoAsync(WaId, Encaminhamento, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retomada_PrazoZeroDesliga()
    {
        EscaladaPeloRoboHa(TimeSpan.FromHours(1));

        var resultado = await Retomar(segundos: 0);

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
    }

    [Fact]
    public async Task SemRetomada_ConversaComAEquipeContinuaSemResposta()
    {
        EscaladaPeloRoboHa(AlemDoPrazo);

        var resultado = await CriarServicoComRetomada().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado, "só a varredura da retomada devolve a conversa");
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
    }

    [Fact]
    public async Task Retomada_ClienteSoReagiuDepoisDaRespostaDaEquipe_NaoRetoma()
    {
        ClienteEscreveuComAEquipeHa(TimeSpan.FromMinutes(5));
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Atendente, Agora.AddMinutes(-4),
            TipoConteudoMensagem.Texto, "Sai às 18h.", "wamid.out12"));
        // Reação, figurinha, vídeo sem legenda e mensagem sem suporte chegam como Outro: o fluxo normal não as leva ao robô.
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora - AlemDoPrazo, TipoConteudoMensagem.Outro, null, "wamid.in12"));

        var resultado = await Retomar();

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _requisicoes.Should().BeEmpty();
    }

    [Fact]
    public async Task Retomada_SoFigurinhaNaConversa_NaoRetoma()
    {
        _historico.Clear();
        _conversa.Assumir(Agora.AddMinutes(-10), Guid.NewGuid());
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora - AlemDoPrazo, TipoConteudoMensagem.Outro, null, "wamid.in13"));

        (await Retomar()).Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
    }

    [Fact]
    public async Task Retomada_ClienteSoTocouBotaoDeAcaoDepoisDaRespostaDaEquipe_NaoRetoma()
    {
        ClienteEscreveuComAEquipeHa(TimeSpan.FromMinutes(5));
        _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Atendente, Agora.AddMinutes(-4),
            TipoConteudoMensagem.Texto, "Sai às 18h.", "wamid.out14"));
        // Botão "acao:" é resolvido sem o robô no fluxo normal: o toque não é pergunta esperando resposta.
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora - AlemDoPrazo, TipoConteudoMensagem.Botao,
            "Avaliar", "wamid.in14", "acao:avaliar:5"));

        var resultado = await Retomar();

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _requisicoes.Should().BeEmpty();
    }

    [Fact]
    public async Task Retomada_ClienteRespondeuPorBotaoComum_Retoma()
    {
        _historico.Clear();
        _conversa.Assumir(Agora.AddMinutes(-10), Guid.NewGuid());
        _historico.Add(Mensagem.Entrada(_empresaId, _conversa.Id, Agora - AlemDoPrazo, TipoConteudoMensagem.Botao,
            "Quero falar do pedido", "wamid.in15", "menu:pedido"));

        (await Retomar()).Should().NotBe(ResultadoTurnoAgente.Ignorado);
    }

    [Fact]
    public async Task Retomada_AtendenteAcabouDeAssumir_OPrazoComecaAli()
    {
        ClienteEscreveuComAEquipeHa(AlemDoPrazo);
        var atendente = Guid.NewGuid();
        _conversa.Assumir(Agora.AddSeconds(-5), atendente);

        (await Retomar()).Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.AssumidaPorUsuarioId.Should().Be(atendente);
        _requisicoes.Should().BeEmpty();
    }

    [Fact]
    public async Task Retomada_ConversaAcabouDeSerTransferida_OPrazoComecaAli()
    {
        ClienteEscreveuComAEquipeHa(AlemDoPrazo);
        var destino = Guid.NewGuid();
        _conversa.Transferir(destino, Agora.AddSeconds(-5));

        (await Retomar()).Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.AssumidaPorUsuarioId.Should().Be(destino);
    }

    [Fact]
    public async Task Retomada_EquipeRespondeEnquantoOTurnoDecide_NaoRetomaNemResponde()
    {
        var entrada = ClienteEscreveuComAEquipeHa(AlemDoPrazo);
        // Cada leitura devolve uma foto do histórico: só quem relê depois do lock enxerga a resposta da atendente,
        // que entra no banco depois da primeira leitura do turno.
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversa.Id, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => new ConversaComMensagens(_conversa, _historico.ToList()));
        var respondeu = false;
        _conversaRepository.When(r => r.ObterPorIdComLockAsync(_empresaId, _conversa.Id, Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                if (respondeu) return;
                respondeu = true;
                _historico.Add(Mensagem.Saida(_empresaId, _conversa.Id, AutorMensagem.Atendente, Agora,
                    TipoConteudoMensagem.Texto, "Oi, já te respondo.", "wamid.out11"));
            });

        var resultado = await Retomar();

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida);
        _conversa.RetomadaPorTempoDecidida(entrada.Id).Should().BeFalse();
        _requisicoes.Should().BeEmpty();
        NotasGravadas().Should().BeEmpty();
    }

    [Fact]
    public async Task Retomada_FalhaAoGravar_DescartaOQueFicouPendente()
    {
        ClienteEscreveuComAEquipeHa(AlemDoPrazo);
        var servico = CriarServicoComRetomada();
        // A falha vem no commit, com a retomada já aplicada na entidade rastreada.
        var situacaoNoCommit = (SituacaoConversa?)null;
        _unitOfWork.CommitAsync().Returns<Task<int>>(_ =>
        {
            situacaoNoCommit = _conversa.Situacao;
            throw new InvalidOperationException("banco fora");
        });

        var acao = () => servico.ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora, retomada: true);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        situacaoNoCommit.Should().Be(SituacaoConversa.Automatica, "a retomada estava pendente quando o banco falhou");
        _unitOfWork.Received().DescartarAlteracoesPendentes();
        _requisicoes.Should().BeEmpty();
    }
}
