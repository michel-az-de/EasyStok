namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Implementação fallback — apenas aguarda um <see cref="PeriodicTimer"/>.
/// Sem capacidade de wakeup imediato (Signal é no-op). Usado quando LISTEN/NOTIFY
/// não está disponível (SQLite, ambientes restritos, modo cron-only).
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
        try { await _timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { /* shutdown — ok */ }
    }

    public void Signal() { /* no-op — polling não suporta wakeup imediato */ }

    public void Dispose() => _timer.Dispose();
}
