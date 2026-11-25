using System.Collections.Concurrent;

namespace EasyStock.Api.UnitTests.BackgroundServices;

/// <summary>
/// Guarda o que o executor posta (a volta do <c>Task.Yield</c>) e só roda quando o teste esvazia a fila, na
/// thread do teste. Quando <see cref="Esvaziar"/> volta, o trabalho já parou no semáforo ou já rodou.
/// </summary>
internal sealed class FilaManual : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Acao, object? Estado)> _fila = new();

    public override void Post(SendOrPostCallback d, object? state) => _fila.Enqueue((d, state));

    /// <summary>Roda o que está na fila e devolve quantos callbacks rodou.</summary>
    public int Esvaziar()
    {
        var rodados = 0;
        while (_fila.TryDequeue(out var item))
        {
            item.Acao(item.Estado);
            rodados++;
        }

        return rodados;
    }
}
