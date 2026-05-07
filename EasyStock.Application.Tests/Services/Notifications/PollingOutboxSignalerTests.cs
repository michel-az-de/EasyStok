using EasyStock.Application.Services.Notifications;
using FluentAssertions;

namespace EasyStock.Application.Tests.Services.Notifications;

public class PollingOutboxSignalerTests
{
    [Fact]
    public void Construtor_rejeita_intervalo_zero_ou_negativo()
    {
        var act = () => new PollingOutboxSignaler(TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();

        var act2 = () => new PollingOutboxSignaler(TimeSpan.FromSeconds(-1));
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Signal_eh_no_op_polling_nao_suporta_wakeup_imediato()
    {
        using var sut = new PollingOutboxSignaler(TimeSpan.FromSeconds(10));

        // Signal não deve afetar o timer — chamar várias vezes não acelera o tick
        sut.Signal();
        sut.Signal();
        sut.Signal();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = async () => await sut.WaitAsync(cts.Token);

        // Como tick é a cada 10s e cancelamos em 100ms, deve cancelar
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WaitAsync_propaga_OperationCanceledException()
    {
        using var sut = new PollingOutboxSignaler(TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await sut.WaitAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task WaitAsync_completa_quando_intervalo_passa()
    {
        using var sut = new PollingOutboxSignaler(TimeSpan.FromMilliseconds(50));

        // Primeiro tick após ~50ms
        var task = sut.WaitAsync(CancellationToken.None);
        await Task.Delay(150);

        task.IsCompleted.Should().BeTrue();
    }
}
