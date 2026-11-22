using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.Cliente.Dossie;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Application.Tests.Services.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>
/// #1288: timeout (OperationCanceledException com o ct vivo) não pode derrubar o worker.
/// #2133: turno que falha devolve a conversa à equipe, em vez de só logar.
/// </summary>
public class ProcessarTurnoAgenteUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _conversaId = Guid.NewGuid();
    private readonly IConversaRepository _conversaRepository = Substitute.For<IConversaRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();
    private readonly IEscaladorConversa _escalador = Substitute.For<IEscaladorConversa>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private static readonly DateTime Agora = new(2026, 10, 28, 15, 0, 0, DateTimeKind.Utc);

    private ProcessarTurnoAgenteUseCase CriarUseCase()
    {
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(true); llm.DisponivelAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        var clientes = Substitute.For<IClienteRepository>();
        var agente = new AgenteAtendimentoService(
            llm, _conversaRepository, Substitute.For<IConfiguracaoAtendimentoRepository>(), clientes,
            [], _escalador, Substitute.For<IWhatsAppCloudClient>(),
            Substitute.For<IUsoIaRepository>(), _unitOfWork,
            new ObterDossieClienteUseCase(clientes, Substitute.For<IClienteCrmRepository>(),
                Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), _conversaRepository),
            Substitute.For<ICadernoRepository>(), NullLogger<AgenteAtendimentoService>.Instance);
        return new ProcessarTurnoAgenteUseCase(agente, _tenant, _conversaRepository, _escalador, _unitOfWork, _logger);
    }

    private readonly LoggerQueGuarda<ProcessarTurnoAgenteUseCase> _logger = new();

    // #3121: as linhas de um mesmo turno, inclusive a da falha, saem com a conversa e uma correlação própria.
    [Fact]
    public async Task FalhaDoTurnoLogaDentroDoEscopoDoTurno()
    {
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversaId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ConversaComMensagens?>(_ => throw new InvalidOperationException("banco"));

        var useCase = CriarUseCase();
        await useCase.ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId));
        await useCase.ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId));

        var falhas = _logger.Eventos.Where(e => e.Nivel == LogLevel.Error).Select(e => e.Escopo).ToList();
        falhas.Should().HaveCountGreaterThanOrEqualTo(2);
        falhas.Should().OnlyContain(e => Equals(e["ConversaId"], _conversaId) && Equals(e["EmpresaId"], _empresaId));
        falhas.Select(e => e["TurnoCorrelacao"]).Distinct().Should().HaveCountGreaterThanOrEqualTo(2, "cada turno tem a sua correlação");
        falhas.Should().OnlyContain(e => e.Count == 3, "o escopo não leva texto, nome nem telefone");
    }

    [Fact]
    public async Task TimeoutComCtVivoNaoDerrubaOWorker()
    {
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversaId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ConversaComMensagens?>(_ => throw new TaskCanceledException("timeout"));

        var resultado = await CriarUseCase().ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId));

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
    }

    [Fact]
    public async Task CancelamentoDoWorkerPropaga()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversaId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ConversaComMensagens?>(ci => throw new OperationCanceledException(ci.Arg<CancellationToken>()));

        var act = () => CriarUseCase().ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private Conversa ConversaAutomatica()
    {
        var conversa = Conversa.Abrir(_empresaId, "5511999998888", Agora, "Maria");
        conversa.RegistrarEntrada(Agora);
        conversa.Situacao.Should().Be(SituacaoConversa.Automatica);
        return conversa;
    }

    private void FazOTurnoFalhar() =>
        _conversaRepository.ObterComMensagensAsync(_empresaId, _conversaId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ConversaComMensagens?>(_ => throw new InvalidOperationException("LLM quebrou"));

    /// <summary>#2902: a conversa que a dona abriu pelo "Novo pedido" nasce Assumida e o turno do agente a ignora.</summary>
    [Fact]
    public async Task ConversaAssumidaPelaDona_TurnoEhIgnorado()
    {
        var conversa = ConversaAutomatica();
        conversa.Assumir(Agora, Guid.NewGuid());
        _conversaRepository.ObterPorIdAsync(_empresaId, _conversaId, Arg.Any<CancellationToken>()).Returns(conversa);
        FazOTurnoFalhar();

        var resultado = await CriarUseCase().ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId));

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default, default);
    }

    [Fact]
    public async Task FalhaDoTurnoDevolveAConversaParaAEquipe()
    {
        var conversa = ConversaAutomatica();
        _conversaRepository.ObterPorIdAsync(_empresaId, _conversaId, Arg.Any<CancellationToken>()).Returns(conversa);
        FazOTurnoFalhar();

        var resultado = await CriarUseCase().ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId));

        await _escalador.Received(1).EscalarAsync(
            _empresaId, conversa, ProcessarTurnoAgenteUseCase.MotivoFalhaDoTurno, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).CommitAsync();
        resultado.Should().Be(new ResultadoTurnoAgente(ChamouLlm: false, Respondeu: false, Escalou: true));
    }

    [Fact]
    public async Task FalhaDoTurnoDescartaOEstadoPendenteAntesDeEscalar()
    {
        var conversa = ConversaAutomatica();
        _conversaRepository.ObterPorIdAsync(_empresaId, _conversaId, Arg.Any<CancellationToken>()).Returns(conversa);
        FazOTurnoFalhar();

        await CriarUseCase().ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId));

        Received.InOrder(() =>
        {
            _unitOfWork.DescartarAlteracoesPendentes();
            _escalador.EscalarAsync(_empresaId, conversa, Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
            _unitOfWork.CommitAsync();
        });
    }

    [Fact]
    public async Task FalhaDoTurnoNaoEscalaConversaQueAEquipeJaAssumiu()
    {
        var conversa = ConversaAutomatica();
        conversa.Assumir(Agora);
        _conversaRepository.ObterPorIdAsync(_empresaId, _conversaId, Arg.Any<CancellationToken>()).Returns(conversa);
        FazOTurnoFalhar();

        var resultado = await CriarUseCase().ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId));

        await _escalador.DidNotReceiveWithAnyArgs().EscalarAsync(default, default!, default!, default);
        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
    }

    [Fact]
    public async Task FalhaAoEscalarNaoDerrubaOWorker()
    {
        var conversa = ConversaAutomatica();
        _conversaRepository.ObterPorIdAsync(_empresaId, _conversaId, Arg.Any<CancellationToken>()).Returns(conversa);
        FazOTurnoFalhar();
        _escalador.EscalarAsync(default, default!, default!, default).ReturnsForAnyArgs(_ => throw new InvalidOperationException("banco fora"));

        var resultado = await CriarUseCase().ExecuteAsync(new ProcessarTurnoAgenteJob(_empresaId, _conversaId));

        resultado.Should().Be(ResultadoTurnoAgente.Ignorado);
    }
}
