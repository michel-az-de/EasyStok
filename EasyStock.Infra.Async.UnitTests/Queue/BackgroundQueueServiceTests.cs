using FluentAssertions;
using NSubstitute;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.UnitTests.Queue;

public class BackgroundQueueServiceTests
{
    private sealed record Job(int N);

    [Fact]
    public async Task JobQueFalha_LogaComAExcecaoEContinuaNaFila()
    {
        // #2285: Console.Error perdia a stack; o log leva a exceção e o job seguinte ainda roda.
        var logger = Substitute.For<ILogger<BackgroundQueueService>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var fila = new BackgroundQueueService(logger);
        await fila.EnqueueAsync("q", new Job(1));
        await fila.EnqueueAsync("q", new Job(2));
        var processados = new List<int>();

        await fila.ProcessQueueAsync<Job>("q", job =>
        {
            if (job.N == 1) throw new InvalidOperationException("quebrou");
            processados.Add(job.N);
            return Task.CompletedTask;
        }, CancellationToken.None);

        processados.Should().Equal(2);
        logger.ReceivedCalls().Should().Contain(c =>
            c.GetMethodInfo().Name == nameof(ILogger.Log)
            && (LogLevel)c.GetArguments()[0]! == LogLevel.Error
            && c.GetArguments()[3] is InvalidOperationException);
    }
}
