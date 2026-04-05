using EasyStock.Application.Ports.Output;
using System.Collections.Concurrent;
using System.Text.Json;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementação em memória do serviço de fila.
/// Usa ConcurrentQueue para thread-safety e processamento sequencial.
/// Ideal para desenvolvimento/testes; para produção usar Redis Queue ou similar.
/// </summary>
public sealed class BackgroundQueueService : IQueueService, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _queues = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _processingTokens = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task EnqueueAsync<T>(string queueName, T message)
    {
        var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<string>());
        var json = JsonSerializer.Serialize(message, JsonOptions);
        queue.Enqueue(json);
        return Task.CompletedTask;
    }

    public async Task ProcessQueueAsync<T>(string queueName, Func<T, Task> processor, CancellationToken cancellationToken)
    {
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTokens[queueName] = tokenSource;

        try
        {
            var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<string>());

            while (!tokenSource.Token.IsCancellationRequested)
            {
                if (queue.TryDequeue(out var json))
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<T>(json, JsonOptions);
                        if (message is not null)
                        {
                            await processor(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log error and continue processing
                        Console.Error.WriteLine($"Erro processando mensagem da fila {queueName}: {ex.Message}");
                    }
                }
                else
                {
                    // Wait a bit before checking again
                    await Task.Delay(1000, tokenSource.Token);
                }
            }
        }
        finally
        {
            _processingTokens.TryRemove(queueName, out _);
        }
    }

    public Task<int> GetQueueLengthAsync(string queueName)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            return Task.FromResult(queue.Count);
        }
        return Task.FromResult(0);
    }

    public Task ClearQueueAsync(string queueName)
    {
        if (_queues.TryGetValue(queueName, out var queue))
        {
            while (queue.TryDequeue(out _)) { }
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var tokenSource in _processingTokens.Values)
        {
            tokenSource.Cancel();
            tokenSource.Dispose();
        }
        _processingTokens.Clear();
        _queues.Clear();
    }
}