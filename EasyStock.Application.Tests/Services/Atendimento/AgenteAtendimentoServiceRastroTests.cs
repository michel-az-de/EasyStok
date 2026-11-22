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
    [InlineData("Seu estorno já caiu na conta.")]
    [InlineData("O frete já está acertado.")]
    [InlineData("Vou pedir para a dona verificar isso com você.")]
    [InlineData("Veja em https://exemplo.com/x")]
    [InlineData("   ")]
    public void Rastro_MotivoFixoDaGuardaTemCategoriaPropria(string resposta)
    {
        var motivo = GuardaRespostaAgente.Avaliar(resposta, [], ["escalar_para_dona"]);

        LogsDoAssistente.CategoriaDaGuarda(motivo!).Should().Be(motivo, "se o texto da guarda mudar, a lista do log acompanha");
    }

    [Fact]
    public void Rastro_MotivoDesconhecidoNaoVaiAoLog() =>
        LogsDoAssistente.CategoriaDaGuarda("cliente Maria pediu 59").Should().Be(LogsDoAssistente.CategoriaOutra);

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
