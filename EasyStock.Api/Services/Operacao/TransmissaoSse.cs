namespace EasyStock.Api.Services.Operacao;

/// <summary>
/// Laço de escrita do SSE de operação (S18): um único escritor na resposta, que manda os eventos da fila
/// assim que chegam e um comentário <c>: heartbeat</c> quando a fila fica parada pelo intervalo informado
/// (proxies derrubam conexão ociosa acima de 30 s).
/// </summary>
public static class TransmissaoSse
{
    public const string Heartbeat = ": heartbeat\n\n";

    public static async Task TransmitirAsync(
        HttpResponse response, OperacaoEventBroker.ListenerSlot slot, TimeSpan intervaloHeartbeat, CancellationToken ct)
    {
        try
        {
            // Confirmação inicial para o cliente tratar a conexão como aberta.
            await response.WriteAsync("event: ready\ndata: {}\n\n", ct);
            await response.Body.FlushAsync(ct);

            while (!ct.IsCancellationRequested && !slot.Cancelled)
            {
                if (!await slot.Signal.WaitAsync(intervaloHeartbeat, ct))
                {
                    await response.WriteAsync(Heartbeat, ct);
                    await response.Body.FlushAsync(ct);
                    continue;
                }

                while (slot.Queue.TryDequeue(out var mensagem))
                    await response.WriteAsync(mensagem.ParaFrame(), ct);
                await response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Cliente desconectou: fim normal do stream.
        }
    }
}
