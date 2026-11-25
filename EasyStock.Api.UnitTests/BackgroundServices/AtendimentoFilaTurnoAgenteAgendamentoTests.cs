using EasyStock.Api.BackgroundServices;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// #3291 (R3): trava a ligação da fila de turnos com o <see cref="ExecutorPorChave"/>: a chave é a conversa e o grupo é a
/// loja. Trocar os dois (ou usar a conversa nos dois) deixa o resto verde e a fila volta a serializar por loja.
/// </summary>
public class AtendimentoFilaTurnoAgenteAgendamentoTests
{
    private static readonly TimeSpan Teto = TimeSpan.FromSeconds(10);

    private static TaskCompletionSource Sinal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ExecutorPorChave NovoExecutor() => new(paralelismo: 4, aoFalhar: (_, _) => { }, tetoPorGrupo: 2);

    private static void Agendar(ExecutorPorChave executor, Guid loja, TaskCompletionSource iniciou, Task liberar) =>
        AtendimentoFilaTurnoAgenteBackgroundService.AgendarTurno(
            executor, new ProcessarTurnoAgenteJob(loja, Guid.NewGuid()),
            async _ =>
            {
                iniciou.SetResult();
                await liberar;
            }, CancellationToken.None);

    [Fact]
    public async Task DuasConversasDaMesmaLojaRodamJuntas()
    {
        var executor = NovoExecutor();
        var loja = Guid.NewGuid();
        var primeira = Sinal();
        var segunda = Sinal();
        var liberar = Sinal();

        Agendar(executor, loja, primeira, liberar.Task);
        Agendar(executor, loja, segunda, liberar.Task);

        // Nenhuma foi liberada: as duas só começam juntas se a chave for a conversa e não a loja.
        await Task.WhenAll(primeira.Task, segunda.Task).WaitAsync(Teto);
        liberar.SetResult();
        await executor.AguardarTudoAsync().WaitAsync(Teto);
    }

    [Fact]
    public async Task ATerceiraConversaDaMesmaLojaEsperaOTetoEDepoisRoda()
    {
        var executor = NovoExecutor();
        var loja = Guid.NewGuid();
        var primeira = Sinal();
        var segunda = Sinal();
        var terceira = Sinal();
        var liberar = Sinal();

        Agendar(executor, loja, primeira, liberar.Task);
        Agendar(executor, loja, segunda, liberar.Task);
        await Task.WhenAll(primeira.Task, segunda.Task).WaitAsync(Teto);

        // A terceira anda até o teto da loja na thread do teste e para ali: quando Esvaziar volta, ela já esperou ou já rodou.
        var fila = new FilaManual();
        var contexto = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(fila);
        try
        {
            Agendar(executor, loja, terceira, liberar.Task);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(contexto);
        }

        fila.Esvaziar().Should().Be(1, "a volta do Task.Yield da terceira passa pela fila");
        terceira.Task.IsCompleted.Should().BeFalse("o teto da loja é 2 e as duas primeiras seguem rodando");

        liberar.SetResult();
        await terceira.Task.WaitAsync(Teto);
        await executor.AguardarTudoAsync().WaitAsync(Teto);
    }
}
