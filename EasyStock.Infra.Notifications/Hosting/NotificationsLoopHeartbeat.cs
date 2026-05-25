using System.Collections.Concurrent;
using EasyStock.Application.Services.Notifications;

namespace EasyStock.Infra.Notifications.Hosting;

/// <summary>
/// Implementacao thread-safe do heartbeat. Singleton — compartilhado entre os 3
/// wrappers e o health check.
/// </summary>
public sealed class NotificationsLoopHeartbeat : INotificationsLoopHeartbeat
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _beats =
        new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public NotificationsLoopHeartbeat() : this(TimeProvider.System) { }

    public NotificationsLoopHeartbeat(TimeProvider time) => _time = time;

    public void Heartbeat(string loopName) => _beats[loopName] = _time.GetUtcNow();

    public DateTimeOffset? LastBeat(string loopName)
        => _beats.TryGetValue(loopName, out var b) ? b : null;

    public IReadOnlyDictionary<string, DateTimeOffset> Snapshot()
        => new Dictionary<string, DateTimeOffset>(_beats);
}
