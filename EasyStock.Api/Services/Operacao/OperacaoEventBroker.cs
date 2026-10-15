using System.Collections.Concurrent;
using System.Text.Json;

namespace EasyStock.Api.Services.Operacao;

/// <summary>
/// Broker de eventos in-memory para Server-Sent Events. Atende dois canais sobre a mesma instância:
///
/// <list type="bullet">
///   <item><b>Mobile</b> (Onda 5): o PWA conecta em <c>GET /api/mobile/operation/stream</c> e recebe
///     <c>mutations-applied</c>, <c>command-queued</c> e <c>order.ready</c> da própria loja, como frame só com
///     <c>data:</c> (o PWA escuta <c>onmessage</c>). Sai em P05.</item>
///   <item><b>Operação</b> (S18): o console conecta em <c>GET api/operacao/eventos</c> com JWT e recebe
///     eventos nomeados (<c>event: pedido.pago</c>) da empresa da claim.</item>
/// </list>
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
/// FAIL-SAFE: NÃO é fonte da verdade. Se broker cair, polling do cliente
/// continua resolvendo. Eventos perdidos são recuperados pelo próximo pull.
/// </summary>
public class OperacaoEventBroker(ILogger<OperacaoEventBroker> log)
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private readonly ILogger<OperacaoEventBroker> _log = log;

    private readonly ConcurrentDictionary<string, ListenerSlot> _listeners = new();

    /// <summary>Quantidade de ouvintes conectados (os dois canais).</summary>
    public int Ouvintes => _listeners.Count;

    /// <summary>Tudo que o broker propaga.</summary>
    public class Subscription : IDisposable
    {
        private readonly OperacaoEventBroker _broker;
        private readonly string _key;
        public Subscription(OperacaoEventBroker broker, string key, ListenerSlot slot)
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

    public enum CanalSse
    {
        Mobile = 1,
        Operacao = 2,
    }

    /// <summary>Um frame SSE. Sem <see cref="Evento"/>, sai só com <c>data:</c> (formato do canal mobile).</summary>
    public sealed record MensagemSse(string? Evento, string Dados)
    {
        public string ParaFrame() => Evento is null
            ? $"data: {Dados}\n\n"
            : $"event: {Evento}\ndata: {Dados}\n\n";
    }

    public class ListenerSlot
    {
        public CanalSse Canal { get; init; } = CanalSse.Mobile;
        public Guid? EmpresaId { get; init; }
        public Guid? LojaId { get; init; }
        public string? DeviceId { get; init; }
        public Func<string, bool>? AceitaEvento { get; init; }
        public ConcurrentQueue<MensagemSse> Queue { get; } = new();
        public SemaphoreSlim Signal { get; } = new(0);
        public bool Cancelled { get; private set; }
        public void Cancel()
        {
            Cancelled = true;
            try { Signal.Release(); } catch { }
        }
    }

    /// <summary>
    /// Registra um listener mobile pra empresa/loja. Chave é uma string única
    /// (connectionId). Retorna <see cref="Subscription"/> que ao Dispose
    /// remove o listener.
    /// </summary>
    public Subscription Subscribe(string key, Guid? empresaId, Guid? lojaId, string? deviceId)
    {
        var slot = new ListenerSlot { Canal = CanalSse.Mobile, EmpresaId = empresaId, LojaId = lojaId, DeviceId = deviceId };
        _listeners[key] = slot;
        _log.LogDebug("SSE listener inscrito: key={Key} loja={LojaId} device={DeviceId} total={Total}",
            key, lojaId, deviceId, _listeners.Count);
        return new Subscription(this, key, slot);
    }

    /// <summary>Registra um ouvinte do console (S18) que recebe os eventos de operação da empresa.</summary>
    public Subscription SubscribeOperacao(string key, Guid empresaId, Func<string, bool>? aceitaEvento = null)
    {
        var slot = new ListenerSlot { Canal = CanalSse.Operacao, EmpresaId = empresaId, AceitaEvento = aceitaEvento };
        _listeners[key] = slot;
        _log.LogDebug("SSE operacao inscrito: key={Key} empresa={EmpresaId} total={Total}",
            key, empresaId, _listeners.Count);
        return new Subscription(this, key, slot);
    }

    /// <summary>Publica um evento nomeado para os ouvintes do console da empresa (S18).</summary>
    public void PublicarOperacao(Guid empresaId, string evento, object payload)
    {
        var mensagem = new MensagemSse(evento, JsonSerializer.Serialize(payload, JsonWeb));
        Broadcast(slot => slot.Canal == CanalSse.Operacao && slot.EmpresaId == empresaId
            && (slot.AceitaEvento?.Invoke(evento) ?? true), mensagem);
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
            slot.Canal == CanalSse.Mobile &&
            slot.LojaId == lojaId &&
            slot.DeviceId != originDeviceId, new MensagemSse(null, data));
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
        Broadcast(slot => slot.Canal == CanalSse.Mobile && slot.DeviceId == deviceId, new MensagemSse(null, data));
        return Task.CompletedTask;
    }

    /// <summary>
    /// C4 — Notifica todos devices da mesma loja (exceto o device origem) que
    /// um pedido transicionou pro status "pronto". PWA escuta e dispara
    /// notification API (web/native) pra alertar o garcom imediatamente,
    /// sem esperar a tela de pedidos refrescar via polling.
    /// </summary>
    public Task NotifyOrderReadyAsync(
        Guid? empresaId,
        Guid? lojaId,
        string originDeviceId,
        string orderId,
        string? clientName,
        decimal total,
        int itemCount)
    {
        if (!lojaId.HasValue) return Task.CompletedTask;
        var data = JsonSerializer.Serialize(new
        {
            type = "order.ready",
            orderId,
            clientName,
            total,
            itemCount,
            originDeviceId,
            serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        Broadcast(slot =>
            slot.Canal == CanalSse.Mobile &&
            slot.LojaId == lojaId &&
            slot.DeviceId != originDeviceId, new MensagemSse(null, data));
        return Task.CompletedTask;
    }

    private void Broadcast(Func<ListenerSlot, bool> predicate, MensagemSse mensagem)
    {
        var sent = 0;
        foreach (var (key, slot) in _listeners)
        {
            if (slot.Cancelled) continue;
            if (!predicate(slot)) continue;
            // Cap fila pra evitar memory leak se cliente travar
            if (slot.Queue.Count > 50) slot.Queue.TryDequeue(out _);
            slot.Queue.Enqueue(mensagem);
            try { slot.Signal.Release(); } catch { }
            sent++;
        }
        if (sent > 0) _log.LogDebug("Broker publicou pra {Sent} listeners", sent);
    }
}
