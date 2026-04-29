using System.Collections.Concurrent;
using System.Text.Json;

namespace EasyStock.Api.Mobile.Services;

/// <summary>
/// Onda 5 — broker de eventos in-memory pra Server-Sent Events.
///
/// PWA conecta em <c>GET /api/mobile/operation/stream?apiKey=...</c> que
/// mantém connection HTTP aberta com <c>Content-Type: text/event-stream</c>.
/// Quando uma mutation chega no servidor (Push do SyncController), service
/// publica evento <c>mutations-applied</c> pros listeners da mesma loja.
///
/// Decisões:
///   - In-memory: serve 1 instância de API. Em multi-instance, evoluir
///     pra Redis pubsub. Casa da Baba é 1 instância — tá bom.
///   - SSE em vez de WebSocket: server→client é tudo que preciso, e SSE
///     reconecta automaticamente sem código no client (usa EventSource
///     browser API). WebSocket exigiria lib client + handshake manual.
///   - Fila bounded por listener (max 50 events) — descarta antigos se
///     listener pendurar. Sem leak.
///
/// FAIL-SAFE: NÃO é fonte da verdade. Se broker cair, polling 30s do PWA
/// continua resolvendo. Eventos perdidos são recuperados pelo próximo pull.
/// </summary>
public class MobileEventBroker(ILogger<MobileEventBroker> log)
{
    private readonly ILogger<MobileEventBroker> _log = log;

    private readonly ConcurrentDictionary<string, ListenerSlot> _listeners = new();

    /// <summary>Tudo que o broker propaga.</summary>
    public class Subscription : IDisposable
    {
        private readonly MobileEventBroker _broker;
        private readonly string _key;
        public Subscription(MobileEventBroker broker, string key, ListenerSlot slot)
        {
            _broker = broker;
            _key = key;
            Slot = slot;
        }
        public ListenerSlot Slot { get; }
        public void Dispose()
        {
            if (_broker._listeners.TryRemove(_key, out var s)) s.Cancel();
        }
    }

    public class ListenerSlot
    {
        public Guid? EmpresaId { get; init; }
        public Guid? LojaId { get; init; }
        public string? DeviceId { get; init; }
        public ConcurrentQueue<string> Queue { get; } = new();
        public SemaphoreSlim Signal { get; } = new(0);
        public bool Cancelled { get; private set; }
        public void Cancel()
        {
            Cancelled = true;
            try { Signal.Release(); } catch { }
        }
    }

    /// <summary>
    /// Registra um listener pra empresa/loja. Chave é uma string única
    /// (connectionId). Retorna <see cref="Subscription"/> que ao Dispose
    /// remove o listener.
    /// </summary>
    public Subscription Subscribe(string key, Guid? empresaId, Guid? lojaId, string? deviceId)
    {
        var slot = new ListenerSlot { EmpresaId = empresaId, LojaId = lojaId, DeviceId = deviceId };
        _listeners[key] = slot;
        _log.LogDebug("SSE listener inscrito: key={Key} loja={LojaId} device={DeviceId} total={Total}",
            key, lojaId, deviceId, _listeners.Count);
        return new Subscription(this, key, slot);
    }

    /// <summary>
    /// Publica evento <c>mutations-applied</c> pra todos listeners da loja
    /// (excluindo o device origem que disparou).
    /// </summary>
    public Task NotifyMutationsAppliedAsync(
        Guid? empresaId,
        Guid? lojaId,
        string originDeviceId,
        int mutationCount)
    {
        if (!lojaId.HasValue) return Task.CompletedTask;

        var data = JsonSerializer.Serialize(new
        {
            type = "mutations-applied",
            originDeviceId,
            mutationCount,
            serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Broadcast(slot =>
            slot.LojaId == lojaId &&
            slot.DeviceId != originDeviceId, data);
        return Task.CompletedTask;
    }

    /// <summary>Notifica device específico de comando pendente.</summary>
    public Task NotifyCommandQueuedAsync(string deviceId, string commandType)
    {
        var data = JsonSerializer.Serialize(new
        {
            type = "command-queued",
            deviceId,
            commandType,
            serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        Broadcast(slot => slot.DeviceId == deviceId, data);
        return Task.CompletedTask;
    }

    private void Broadcast(Func<ListenerSlot, bool> predicate, string data)
    {
        var sent = 0;
        foreach (var (key, slot) in _listeners)
        {
            if (slot.Cancelled) continue;
            if (!predicate(slot)) continue;
            // Cap fila pra evitar memory leak se cliente travar
            if (slot.Queue.Count > 50) slot.Queue.TryDequeue(out _);
            slot.Queue.Enqueue(data);
            try { slot.Signal.Release(); } catch { }
            sent++;
        }
        if (sent > 0) _log.LogDebug("Broker publicou pra {Sent} listeners", sent);
    }
}
