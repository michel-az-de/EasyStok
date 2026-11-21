using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;

namespace EasyStock.Application.Tests.UseCases.Atendimento;

/// <summary>
/// #3068: a varredura acha a conversa parada com a equipe além do prazo e enfileira o turno marcado como retomada. Quem
/// decide e retoma é o turno; aqui só entra a candidata.
/// </summary>
public class EnfileirarRetomadasDoAssistenteUseCaseTests
{
    private static readonly DateTime Agora = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private readonly IRetomadasPendentesQuery _query = Substitute.For<IRetomadasPendentesQuery>();
    private readonly IQueueService _fila = Substitute.For<IQueueService>();

    private EnfileirarRetomadasDoAssistenteUseCase CriarUseCase(int segundos = 60, int janelaMinutos = 30) => new(
        _query, _fila,
        Microsoft.Extensions.Options.Options.Create(new RetomadaAssistenteOptions { Segundos = segundos, JanelaMinutos = janelaMinutos }));

    [Fact]
    public async Task ConversaParadaComAEquipeEntraNaFilaComoRetomada()
    {
        var empresa = Guid.NewGuid();
        var parada = Guid.NewGuid();
        _query.ListarAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([new TurnoPendente(empresa, parada)]);

        var enfileiradas = await CriarUseCase().ExecuteAsync(Agora);

        enfileiradas.Should().Be(1);
        await _fila.Received(1).EnqueueAsync(FilaAtendimentoNomes.TurnoAgente, new ProcessarTurnoAgenteJob(empresa, parada, Retomada: true));
    }

    [Fact]
    public async Task ProcuraQuemEsperaEntreOPrazoEAJanela()
    {
        _query.ListarAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);

        await CriarUseCase(segundos: 60, janelaMinutos: 30).ExecuteAsync(Agora);

        // Espera menor que o prazo ainda é da equipe; maior que a janela é conversa velha, que não se ressuscita.
        await _query.Received(1).ListarAsync(Agora.AddMinutes(-30), Agora.AddSeconds(-60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrazoZeroDesligaAVarredura()
    {
        var enfileiradas = await CriarUseCase(segundos: 0).ExecuteAsync(Agora);

        enfileiradas.Should().Be(0);
        await _query.DidNotReceiveWithAnyArgs().ListarAsync(default, default, default);
        await _fila.DidNotReceiveWithAnyArgs().EnqueueAsync<ProcessarTurnoAgenteJob>(default!, default!);
    }
}
