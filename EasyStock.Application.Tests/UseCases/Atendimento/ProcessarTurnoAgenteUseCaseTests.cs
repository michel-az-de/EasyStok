using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using EasyStock.Application.UseCases.Cliente.Dossie;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>#1288: timeout (OperationCanceledException com o ct vivo) não pode derrubar o worker.</summary>
public class ProcessarTurnoAgenteUseCaseTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _conversaId = Guid.NewGuid();
    private readonly IConversaRepository _conversaRepository = Substitute.For<IConversaRepository>();
    private readonly ITenantContextAccessor _tenant = Substitute.For<ITenantContextAccessor>();

    private ProcessarTurnoAgenteUseCase CriarUseCase()
    {
        var llm = Substitute.For<IAgenteLlmClient>();
        llm.Disponivel.Returns(true);
        var clientes = Substitute.For<IClienteRepository>();
        var agente = new AgenteAtendimentoService(
            llm, _conversaRepository, Substitute.For<IConfiguracaoAtendimentoRepository>(), clientes,
            [], Substitute.For<IEscaladorConversa>(), Substitute.For<IWhatsAppCloudClient>(),
            Substitute.For<IUsoIaRepository>(), Substitute.For<IUnitOfWork>(),
            new ObterDossieClienteUseCase(clientes, Substitute.For<IClienteCrmRepository>(),
                Substitute.For<IHistoricoPedidosClienteQueries>(), Substitute.For<IDomicilioQueries>(), _conversaRepository),
            Substitute.For<ICadernoRepository>(), NullLogger<AgenteAtendimentoService>.Instance);
        return new ProcessarTurnoAgenteUseCase(agente, _tenant, NullLogger<ProcessarTurnoAgenteUseCase>.Instance);
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
}
