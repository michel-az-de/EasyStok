using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Atendimento.Ferramentas;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3175: cada turno gravado diz por que terminou assim, em código estável. Os portões que escalam antes do modelo gravam
/// turno; os silenciosos não. O código nunca leva texto do modelo, valor nem trecho de mensagem.
/// </summary>
public partial class AgenteAtendimentoServiceTests
{
    private void ClienteVinculado(Action<Cliente> ajustar)
    {
        var cliente = Cliente.Criar(_empresaId, "Maria");
        ajustar(cliente);
        _clienteRepository.GetByIdAsync(_empresaId, cliente.Id).Returns(cliente);
        _conversa.VincularCliente(cliente.Id);
    }

    [Fact]
    public async Task Motivo_PortaoDaLojaGravaTurnoQueEscalouSemTokens()
    {
        _llm.Disponivel.Returns(false);
        _llm.DisponivelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Motivo.Should().Be("portao:" + PortoesDoAssistente.Modelo);
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Escalou);
        turno.TotalTokens.Should().Be(0);
        turno.MensagemClienteId.Should().Be(_historico[0].Id);
    }

    [Fact]
    public async Task Motivo_PortaoDaPessoaGravaTurnoQueEscalou()
    {
        ClienteVinculado(c => c.Bloquear("golpe", Agora.AddHours(-1)));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Motivo.Should().Be("portao:" + PortoesDoAssistente.Bloqueado);
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Escalou);
        turno.Motivo.Should().NotContain("golpe");
    }

    [Fact]
    public async Task Motivo_PortaoDoLimiteGravaTurnoQueEscalou()
    {
        RespostasDoAgenteNaUltimaHora(LimiteRespostasAgenteOptions.PadraoPorHora);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Motivo.Should().Be("portao:" + PortoesDoAssistente.Limite);
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Escalou);
    }

    [Fact]
    public async Task Motivo_PortaoDaCotaMantemDesfechoCota()
    {
        LojaComCota(1_000);
        UsoDoMes(1_000);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Motivo.Should().Be("portao:" + PortoesDoAssistente.Cota);
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Cota);
    }

    [Fact]
    public async Task Motivo_PortaoDaSituacaoNaoGravaTurno()
    {
        _conversa.Assumir(Agora.AddMinutes(-5), Guid.NewGuid());

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        await _turnos.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task Motivo_RespostaNormalGravaNulo()
    {
        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().Motivo.Should().BeNull();
    }

    [Fact]
    public async Task Motivo_GuardaDeValorGravaCodigoSemOValorNemATrechoDaResposta()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("A lasanha sai por R$ 59,00."));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Motivo.Should().Be(MotivoDoTurno.GuardaValor);
        turno.Motivo.Should().NotContain("59").And.NotContain("R$").And.NotContain("lasanha");
    }

    [Fact]
    public async Task Motivo_GuardaDeCategoriaFixaGravaOSlugDela()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("Seu estorno já caiu na conta."));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().Motivo.Should().Be("guarda:pagamento_devolucao");
    }

    [Fact]
    public async Task Motivo_RespostaVaziaGravaSlugDaGuardaSemOStopReason()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("   "));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().Motivo.Should().Be("guarda:sem_resposta");
    }

    [Fact]
    public async Task Motivo_LimiteDeIteracoesGravaIteracoes()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(_ => UsoFerramenta("consultar_pedido", "toolu_" + _requisicoes.Count));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().Motivo.Should().Be(MotivoDoTurno.Iteracoes);
    }

    [Fact]
    public async Task Motivo_MenuQueNaoSaiGravaMenu()
    {
        var janelas = Substitute.For<IFerramentaAgente>();
        janelas.Nome.Returns("listar_janelas");
        janelas.Descricao.Returns("Lista as janelas");
        janelas.SchemaJson.Returns("""{"type":"object","properties":{}}""");
        janelas.ExecutarAsync(Arg.Any<ContextoTurnoAgente>(), Arg.Any<JsonElement>(), Arg.Any<CancellationToken>())
            .Returns(c =>
            {
                c.Arg<ContextoTurnoAgente>().AdiarEnvio(_ => Task.FromResult(false));
                return """{"opcoesNoMenu":2}""";
            });
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(UsoFerramenta("listar_janelas"), Texto("Escolha uma das janelas abaixo."));

        await CriarServico(janelas).ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().Motivo.Should().Be(MotivoDoTurno.Menu);
    }

    [Fact]
    public async Task Motivo_FalhaDaGeracaoGravaFalhaLlm()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns<RespostaLlm>(_ => throw new HttpRequestException("fora do ar"));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Erro);
        turno.Motivo.Should().Be(MotivoDoTurno.FalhaLlm);
    }

    [Fact]
    public async Task Motivo_TetoDeSaidaEstouradoEscalaSemEnviarTextoCortado()
    {
        // #3273 (R1): max_tokens com texto cortado nunca chega ao cliente; a equipe recebe a conversa, com motivo próprio.
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm(RespostaLlm.StopMaxTokens, [new BlocoTextoLlm("A entrega de amanhã fica combi")], 10, 4096));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Escalou.Should().BeTrue();
        await _cloudClient.DidNotReceive().EnviarTextoAsync(Arg.Any<string>(), Arg.Is<string>(t => t.Contains("combi")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        var turno = TurnoGravado();
        turno.Motivo.Should().Be(MotivoDoTurno.TetoSaida);
    }

    [Fact]
    public async Task Motivo_RecusaDoModeloEscalaSemEnviarOTextoDaRecusaEChamaOModeloUmaVezSo()
    {
        // #3321 (D4): refusal é desfecho próprio: o texto dela nunca vai ao cliente e o pedido não se repete.
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>())
            .Returns(new RespostaLlm(RespostaLlm.StopRefusal, [new BlocoTextoLlm("Não posso ajudar com isso.")], 10, 20));

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Escalou.Should().BeTrue();
        await _llm.Received(1).EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>());
        await _cloudClient.DidNotReceive().EnviarTextoAsync(Arg.Any<string>(), Arg.Is<string>(t => t.Contains("Não posso ajudar")),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
        TurnoGravado().Motivo.Should().Be(MotivoDoTurno.RecusaModelo);
    }

    private AgenteAtendimentoService CriarServicoComPrazoDeUmSegundo() => new(
        _llm, _conversaRepository, _configuracaoRepository, _clienteRepository, [_consultarPedido], _escalador,
        _cloudClient, _unitOfWork,
        new ObterDossieClienteUseCase(_clienteRepository, _crm, _historicoPedidos, _domicilio, _conversaRepository),
        _caderno, _log, _turnos, Microsoft.Extensions.Options.Options.Create(new LimiteRespostasAgenteOptions()),
        Microsoft.Extensions.Options.Options.Create(_precos), _pedidos,
        prazoTurno: Microsoft.Extensions.Options.Options.Create(new PrazoDoTurnoAgenteOptions { Segundos = 1 }));

    [Fact]
    public async Task Motivo_PrazoDoTurnoEstouradoEscalaGravaMotivoProprioENaoChamaOModeloDeNovo()
    {
        // #3291 (R3): modelo que não responde não prende a conversa: no prazo do turno ela vai para a equipe, uma chamada só.
        var chamadas = 0;
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(async c =>
        {
            chamadas++;
            await Task.Delay(Timeout.Infinite, c.Arg<CancellationToken>());
            return Texto("nunca");
        });

        var resultado = await CriarServicoComPrazoDeUmSegundo()
            .ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora).WaitAsync(TimeSpan.FromSeconds(15));

        resultado.Escalou.Should().BeTrue();
        chamadas.Should().Be(1);
        var turno = TurnoGravado();
        turno.Motivo.Should().Be(MotivoDoTurno.PrazoTurno);
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Escalou);
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa,
            Arg.Is<string>(m => m.StartsWith(GeracaoAgenteAtendimento.MotivoPrazoDoTurno)), Agora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Motivo_PrazoEstouradoDepoisDeUmaFerramentaGravaOsTokensJaGastosEUmaSoEscalada()
    {
        // #3291 (R3): a primeira volta custou tokens e rodou a ferramenta; a segunda passa do prazo. O turno guarda o
        // que foi gasto, escala uma vez e não manda texto nenhum ao cliente (a ferramenta não envia).
        var chamadas = 0;
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(async c =>
        {
            if (++chamadas == 1) return UsoFerramenta("consultar_pedido");
            await Task.Delay(Timeout.Infinite, c.Arg<CancellationToken>());
            return Texto("nunca");
        });

        var resultado = await CriarServicoComPrazoDeUmSegundo()
            .ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora).WaitAsync(TimeSpan.FromSeconds(15));

        resultado.Escalou.Should().BeTrue();
        var turno = TurnoGravado();
        turno.Motivo.Should().Be(MotivoDoTurno.PrazoTurno);
        turno.TotalTokens.Should().BeGreaterThan(0, "a volta que terminou foi paga");
        turno.Ferramentas.Should().Equal(new FerramentaDoTurno("consultar_pedido", true));
        await _escalador.Received(1).EscalarAsync(_empresaId, _conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>());
        // Sem resposta duplicada: sai uma mensagem só, o encaminhamento da escalada.
        await _cloudClient.Received(1).EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Motivo_EnvioQueDemoraMaisQueOPrazoNaoEhCortado()
    {
        // #3291 (R3): o prazo é só das chamadas ao modelo. O modelo responde na hora, o envio ao cliente passa de 1 s e
        // termina: o prazo (1 s) já não existe quando o envio roda.
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("Oi! Já te respondo."));
        var tokenDoEnvio = default(CancellationToken);
        _cloudClient.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async c =>
            {
                tokenDoEnvio = c.Arg<CancellationToken>();
                await Task.Delay(1300, c.Arg<CancellationToken>());
                return new EnvioWhatsAppResult("wamid.lento");
            });

        var resultado = await CriarServicoComPrazoDeUmSegundo()
            .ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora).WaitAsync(TimeSpan.FromSeconds(15));

        resultado.Respondeu.Should().BeTrue();
        resultado.Escalou.Should().BeFalse();
        tokenDoEnvio.IsCancellationRequested.Should().BeFalse();
        TurnoGravado().Desfecho.Should().Be(DesfechoTurnoAgente.Respondeu);
    }

    [Fact]
    public async Task Motivo_TurnoInterrompidoGravaInterrompido()
    {
        var aguardando = new TaskCompletionSource<RespostaLlm>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ => { chamou.SetResult(); return aguardando.Task; });
        var turno = CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        await chamou.Task;
        _conversa.VincularCliente(Guid.NewGuid());
        aguardando.SetResult(Texto("Resposta da pessoa anterior."));
        await turno;

        TurnoGravado().Motivo.Should().Be(MotivoDoTurno.Interrompido);
    }

    // #3298 (R11): a conversa mudou de mãos e o modelo também tinha falhado: o histórico guarda o erro do modelo, não só a interrupção.
    [Fact]
    public async Task Motivo_TurnoInterrompidoComFalhaDoModeloGravaErroComFalhaLlm()
    {
        var aguardando = new TaskCompletionSource<RespostaLlm>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(_ => { chamou.SetResult(); return aguardando.Task; });
        var turno = CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);
        await chamou.Task;
        _conversa.VincularCliente(Guid.NewGuid());
        aguardando.SetException(new HttpRequestException("fora do ar"));
        await turno;

        var gravado = TurnoGravado();
        gravado.Desfecho.Should().Be(DesfechoTurnoAgente.Erro);
        gravado.Motivo.Should().Be(MotivoDoTurno.FalhaLlm);
    }

    [Fact]
    public async Task Motivo_EscaladaPedidaPeloModeloGravaCodigoENuncaOTextoQueEleEscreveu()
    {
        _situacaoNoBanco = SituacaoConversa.Automatica;
        _escalador.When(e => e.EscalarAsync(_empresaId, _conversa, Arg.Any<string>(), Agora, Arg.Any<CancellationToken>()))
            .Do(_ => _conversa.Escalar(Agora, "x"));
        var entrada = JsonSerializer.SerializeToElement(new { motivo = "cliente Maria quer devolver R$ 59 da lasanha" });
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(new RespostaLlm(RespostaLlm.StopToolUse,
            [new BlocoUsoFerramentaLlm("esc", "escalar_para_dona", entrada)], 1, 1));

        await CriarServico(new EscalarParaDonaFerramenta(_escalador)).ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Escalou);
        turno.Motivo.Should().Be(MotivoDoTurno.EscaladaModelo);
        turno.Motivo.Should().NotContain("Maria").And.NotContain("59").And.NotContain("lasanha");
    }
}
