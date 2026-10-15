using System.Collections.Concurrent;
using System.Text.Json;

namespace EasyStock.Infra.Integrations.Meta;

/// <summary>Sem provider <c>meta</c>: não sai para a rede, guarda o payload (JSON) para inspeção.</summary>
public sealed class StubMetaMensageriaTransporte : IMetaMensageriaTransporte
{
    private readonly ConcurrentQueue<string> _enviados = new();

    public IReadOnlyCollection<string> Enviados => _enviados.ToArray();

    public Task<string> EnviarAsync(object payload, CancellationToken ct = default)
    {
        _enviados.Enqueue(JsonSerializer.Serialize(payload));
        return Task.FromResult("stub-mid-" + Guid.NewGuid().ToString("N"));
    }
}