using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.Tests.Services.Auth;
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
    public async Task Rastro_ConversaComAEquipeNaoLogaAviso_RodaACadaMensagemDoCliente()
    {
        _conversa.Assumir(Agora.AddMinutes(-5), Guid.NewGuid());

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        Avisos.Should().NotContain(LogsDoAssistente.EventoPortaoBarrou);
    }

    [Fact]
    public async Task Rastro_RespostaBarradaLogaACategoriaSemOValor()
    {
        _llm.EnviarAsync(Arg.Any<RequisicaoLlm>(), Arg.Any<CancellationToken>()).Returns(Texto("A lasanha sai por R$ 59,00."));

        await CriarServico().ProcessarTurnoAsync(_empresaId, _conversa.Id, Agora);

        Avisos.Should().Contain(LogsDoAssistente.EventoGuardaBarrou);
        _log.Linhas.Should().Contain(l => l.Contains("valor sem origem em ferramenta"));
        _log.Linhas.Should().NotContain(l => l.Contains("59"), "o log não leva trecho da resposta");
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

        await Retomar();

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
