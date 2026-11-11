using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>
/// #2283: o job do turno estava na fila em memória quando a Api reiniciou. A fila nova começa vazia; a recuperação
/// do start devolve o turno à fila, e o agente responde o cliente.
/// </summary>
public class RecuperarTurnosPerdidosUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);
    private readonly ITurnosPendentesQuery _query = Substitute.For<ITurnosPendentesQuery>();
    private readonly IQueueService _fila = Substitute.For<IQueueService>();

    private RecuperarTurnosPerdidosUseCase CriarUseCase() =>
        new(_query, _fila, NullLogger<RecuperarTurnosPerdidosUseCase>.Instance);

    [Fact]
    public async Task TurnoPerdidoNoReinicioVoltaParaAFila()
    {
        var empresa = Guid.NewGuid();
        var perdida = Guid.NewGuid();
        var outra = Guid.NewGuid();
        _query.ListarAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new TurnoPendente(empresa, perdida), new TurnoPendente(empresa, outra)]);

        var recuperados = await CriarUseCase().ExecuteAsync(Agora, TimeSpan.FromMinutes(30));

        recuperados.Should().Be(2);
        await _fila.Received(1).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, new ProcessarTurnoAgenteJob(empresa, perdida));
        await _fila.Received(1).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, new ProcessarTurnoAgenteJob(empresa, outra));
    }

    [Fact]
    public async Task ProcuraSoDentroDaJanelaAteAgora()
    {
        _query.ListarAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);

        var recuperados = await CriarUseCase().ExecuteAsync(Agora, TimeSpan.FromMinutes(30));

        recuperados.Should().Be(0);
        await _query.Received(1).ListarAsync(Agora.AddMinutes(-30), Agora, Arg.Any<CancellationToken>());
        await _fila.DidNotReceiveWithAnyArgs().EnqueueAsync<ProcessarTurnoAgenteJob>(default!, default!);
    }

    [Fact]
    public async Task JanelaZeroDesligaARecuperacao()
    {
        var recuperados = await CriarUseCase().ExecuteAsync(Agora, TimeSpan.Zero);

        recuperados.Should().Be(0);
        await _query.DidNotReceiveWithAnyArgs().ListarAsync(default, default, default);
        await _fila.DidNotReceiveWithAnyArgs().EnqueueAsync<ProcessarTurnoAgenteJob>(default!, default!);
    }
}
