namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Implementação fallback — apenas aguarda um <see cref="PeriodicTimer"/>.
/// Sem capacidade de wakeup imediato (Signal é no-op). Usado quando LISTEN/NOTIFY
/// não está disponível (ambientes restritos, modo cron-only).
/// Singleton.
/// </summary>
public sealed class PollingOutboxSignaler : IOutboxSignaler, IDisposable
{
    private readonly PeriodicTimer _timer;

    public PollingOutboxSignaler(TimeSpan intervalo)
    {
        if (intervalo <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(intervalo), "Intervalo deve ser positivo.");
        _timer = new PeriodicTimer(intervalo);
    }

    public async Task WaitAsync(CancellationToken ct)
    {
        // Propaga OperationCanceledException ao invés de engolir — alinha com
        // PostgresOutboxSignaler. Loops dos wrappers já tratam OCE.
        await _timer.WaitForNextTickAsync(ct);
    }

    public void Signal() { /* no-op — polling não suporta wakeup imediato */ }

    public void Dispose() => _timer.Dispose();
}
