using EasyStock.Application.Ports.Output;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async;

/// <summary>
/// Implementação em memória do serviço de fila.
/// Usa ConcurrentQueue para thread-safety e processamento sequencial.
/// Ideal para desenvolvimento/testes; para produção usar Redis Queue ou similar.
/// </summary>
/// <remarks>
/// <c>ProcessQueueAsync</c> drena todas as mensagens presentes no momento da chamada e retorna
/// quando a fila fica vazia. Para processamento contínuo em produção, utilize um
/// <c>BackgroundService</c> que invoque este método em loop com um intervalo de polling.
/// </remarks>
public sealed class BackgroundQueueService(ILogger<BackgroundQueueService>? logger = null) : IQueueService, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _queues = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Task EnqueueAsync<T>(string queueName, T message)
    {
        var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<string>());
        var json = JsonSerializer.Serialize(message, JsonOptions);
        queue.Enqueue(json);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Processa todas as mensagens atualmente enfileiradas e retorna quando a fila fica vazia
    /// ou quando o <paramref name="cancellationToken"/> é solicitado.
    /// </summary>
    public async Task ProcessQueueAsync<T>(string queueName, Func<T, Task> processor, CancellationToken cancellationToken)
    {
        var queue = _queues.GetOrAdd(queueName, _ => new ConcurrentQueue<string>());

        while (!cancellationToken.IsCancellationRequested && queue.TryDequeue(out var json))
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
                // #2285: segue com o resto da fila, mas com a exceção inteira no log (Console.Error perdia a stack).
                logger?.LogError(ex, "Erro processando mensagem da fila {Fila}.", queueName);
            }
        }
    }

    public void Dispose()
    {
        _queues.Clear();
    }
}