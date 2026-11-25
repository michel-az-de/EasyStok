using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Services.Notifications;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Application.Tests.Services.Atendimento;

/// <summary>
/// #3356 (R11): a ordem dos portões medida por fora, no turno e na retomada. O turno e a retomada leem cada grupo (conversa,
/// loja, pessoa, limite, cota) só se o anterior passou, e o primeiro portão que barra decide. Estes testes valem antes e
/// depois de a retomada passar a pedir a ordem a <c>PortoesDoAssistente.EtapasAsync</c>.
/// </summary>
public partial class AgenteAtendimentoServiceTests
{
    private void OrdemLojaDesligada()
    {
        var desligada = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        desligada.Ativo = false;
        _configuracaoRepository.GetByEmpresaIdAsync(_empresaId).Returns(desligada);
    }

    private AgenteAtendimentoService OrdemServicoDeRetomada(IContatoDaEquipeAvisada equipe)
    {
        // A retomada decide dentro de uma transação, com a conversa travada.
        _unitOfWork.ExecuteInTransactionSemRetryAsync(
                Arg.Any<Func<CancellationToken, Task<(ConversaComMensagens, Mensagem)?>>>(), Arg.Any<CancellationToken>())
            .Returns(c => c.Arg<Func<CancellationToken, Task<(ConversaComMensagens, Mensagem)?>>>()(c.Arg<CancellationToken>()));
        return new(
            _llm, _conversaRepository, _configuracaoRepository, _clienteRepository,
            [_consultarPedido], _escalador, _cloudClient, _unitOfWork,
            new ObterDossieClienteUseCase(_clienteRepository, _crm, _historicoPedidos, _domicilio, _conversaRepository),
            _caderno, _log, _turnos, Microsoft.Extensions.Options.Options.Create(new LimiteRespostasAgenteOptions()),
            Microsoft.Extensions.Options.Options.Create(_precos), _pedidos, equipe,
            eventos: _eventos,
            retomadaOpcoes: Microsoft.Extensions.Options.Options.Create(new RetomadaAssistenteOptions { Segundos = 60 }));
    }

    private Task<ResultadoTurnoAgente> OrdemRetomar(IContatoDaEquipeAvisada equipe, DateTime? agora = null) =>
        OrdemServicoDeRetomada(equipe).ProcessarTurnoAsync(_empresaId, _conversa.Id, agora ?? Agora, retomada: true);

    /// <summary>A retomada foi dispensada pelo portão: a conversa fica com a equipe, o modelo não roda e a mensagem não volta.</summary>
    private async Task OrdemRetomadaDispensadaPor(ResultadoTurnoAgente resultado, Mensagem entrada, string portao)
    {
        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        _conversa.Situacao.Should().Be(SituacaoConversa.Assumida, "a conversa fica como a equipe deixou");
        _conversa.RetomadaPorTempoDecidida(entrada.Id).Should().BeTrue("a varredura não reavalia a mesma mensagem a cada passada");
        _log.Linhas.Should().ContainSingle(l => l.Contains("retomada dispensada") && l.Contains("Portao=" + portao));
        _requisicoes.Should().BeEmpty();
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    // ---- Turno: a ordem que o turno já tem e que esta fatia não muda -------------------------------------------------

    [Fact]
    public async Task Ordem_Turno_ConversaQueBarra_NaoLeAConfiguracaoNemAEquipeNemOCliente()
    {
        _conversa.Assumir(Agora.AddMinutes(-1), Guid.NewGuid());

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        await _configuracaoRepository.DidNotReceiveWithAnyArgs().GetByEmpresaIdAsync(default);
        await _llm.DidNotReceiveWithAnyArgs().DisponivelAsync(default, default);
        await _clienteRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task Ordem_Turno_SemEntradaPendente_IgnoraAntesDeLerALoja()
    {
        _historico[0].MarcarProcessada(Agora);
        OrdemLojaDesligada();

        var resultado = await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        await _configuracaoRepository.DidNotReceiveWithAnyArgs().GetByEmpresaIdAsync(default);
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task Ordem_Turno_LojaDesligadaEClienteBloqueado_BarraNaLojaSemLerOCliente()
    {
        OrdemLojaDesligada();
        ClienteVinculado(c => c.Bloquear("golpe", Agora.AddHours(-1)));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().Motivo.Should().Be("portao:" + PortoesDoAssistente.Chave);
        await _clienteRepository.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task Ordem_Turno_ClienteBloqueadoELimiteEstourado_BarraNoClienteSemContarAsRespostas()
    {
        ClienteVinculado(c => c.Bloquear("golpe", Agora.AddHours(-1)));
        RespostasDoAgenteNaUltimaHora(LimiteRespostasAgenteOptions.PadraoPorHora);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        TurnoGravado().Motivo.Should().Be("portao:" + PortoesDoAssistente.Bloqueado);
        await _conversaRepository.DidNotReceiveWithAnyArgs().ContarRespostasDoAgenteDesdeAsync(default, default, default, default);
    }

    [Fact]
    public async Task Ordem_Turno_LimiteEstouradoECotaEsgotada_BarraNoLimiteSemSomarOMes()
    {
        LojaComCota(1_000);
        UsoDoMes(1_000);
        RespostasDoAgenteNaUltimaHora(LimiteRespostasAgenteOptions.PadraoPorHora);

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        var turno = TurnoGravado();
        turno.Motivo.Should().Be("portao:" + PortoesDoAssistente.Limite);
        turno.Desfecho.Should().Be(DesfechoTurnoAgente.Escalou, "só a cota tem desfecho próprio");
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    // ---- Retomada: todos os portões menos "quem atende", na ordem do turno, até o primeiro que barra -----------------

    [Fact]
    public async Task Ordem_Retomada_VinculoEmRevisao_Dispensa_SemLerALojaNemOCusto()
    {
        var entrada = EscaladaPeloRoboHa(AlemDoPrazo);
        _conversa.ExigirConferenciaCliente();
        var equipe = Substitute.For<IContatoDaEquipeAvisada>();

        var resultado = await OrdemRetomar(equipe);

        await OrdemRetomadaDispensadaPor(resultado, entrada, PortoesDoAssistente.Vinculo);
        await _llm.DidNotReceiveWithAnyArgs().DisponivelAsync(default, default);
        await equipe.DidNotReceiveWithAnyArgs().EhInscritoAsync(default, default, default);
        await _conversaRepository.DidNotReceiveWithAnyArgs().ContarRespostasDoAgenteDesdeAsync(default, default, default, default);
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task Ordem_Retomada_JanelaVencida_Dispensa_SemLerALojaNemOCusto()
    {
        var entrada = EscaladaPeloRoboHa(AlemDoPrazo);
        var equipe = Substitute.For<IContatoDaEquipeAvisada>();

        var resultado = await OrdemRetomar(equipe, Agora.AddHours(25));

        await OrdemRetomadaDispensadaPor(resultado, entrada, PortoesDoAssistente.Janela);
        await _llm.DidNotReceiveWithAnyArgs().DisponivelAsync(default, default);
        await equipe.DidNotReceiveWithAnyArgs().EhInscritoAsync(default, default, default);
        await _conversaRepository.DidNotReceiveWithAnyArgs().ContarRespostasDoAgenteDesdeAsync(default, default, default, default);
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task Ordem_Retomada_ChaveDesligadaELimiteEstourado_BarraNaChave_LendoALojaInteiraENadaDepois()
    {
        var entrada = EscaladaPeloRoboHa(AlemDoPrazo);
        OrdemLojaDesligada();
        RespostasDoAgenteNaUltimaHora(LimiteRespostasAgenteOptions.PadraoPorHora);
        var equipe = Substitute.For<IContatoDaEquipeAvisada>();

        var resultado = await OrdemRetomar(equipe);

        await OrdemRetomadaDispensadaPor(resultado, entrada, PortoesDoAssistente.Chave);
        // O grupo da loja é lido inteiro (modelo, canal e contato da equipe), mesmo com a chave desligada; o seguinte não.
        await _llm.Received(1).DisponivelAsync(_empresaId, Arg.Any<CancellationToken>());
        await equipe.Received(1).EhInscritoAsync(_empresaId, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _conversaRepository.DidNotReceiveWithAnyArgs().ContarRespostasDoAgenteDesdeAsync(default, default, default, default);
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task Ordem_Retomada_ClienteBloqueado_BarraAntesDoLimiteEDaCota()
    {
        var entrada = EscaladaPeloRoboHa(AlemDoPrazo);
        ClienteVinculado(c => c.Bloquear("golpe", Agora.AddHours(-1)));
        LojaComCota(1_000);
        UsoDoMes(1_000);
        RespostasDoAgenteNaUltimaHora(LimiteRespostasAgenteOptions.PadraoPorHora);

        var resultado = await OrdemRetomar(Substitute.For<IContatoDaEquipeAvisada>());

        await OrdemRetomadaDispensadaPor(resultado, entrada, PortoesDoAssistente.Bloqueado);
        await _conversaRepository.DidNotReceiveWithAnyArgs().ContarRespostasDoAgenteDesdeAsync(default, default, default, default);
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task Ordem_Retomada_LimiteEstouradoECotaEsgotada_BarraNoLimiteSemSomarOMes()
    {
        var entrada = EscaladaPeloRoboHa(AlemDoPrazo);
        LojaComCota(1_000);
        UsoDoMes(1_000);
        RespostasDoAgenteNaUltimaHora(LimiteRespostasAgenteOptions.PadraoPorHora);

        var resultado = await OrdemRetomar(Substitute.For<IContatoDaEquipeAvisada>());

        await OrdemRetomadaDispensadaPor(resultado, entrada, PortoesDoAssistente.Limite);
        await _turnos.DidNotReceiveWithAnyArgs().SomarAsync(default, default, default, default);
    }

    [Fact]
    public async Task Ordem_Retomada_SoACotaEsgotada_Dispensa()
    {
        var entrada = EscaladaPeloRoboHa(AlemDoPrazo);
        LojaComCota(1_000);
        UsoDoMes(1_000);

        var resultado = await OrdemRetomar(Substitute.For<IContatoDaEquipeAvisada>());

        await OrdemRetomadaDispensadaPor(resultado, entrada, PortoesDoAssistente.Cota);
    }
}
