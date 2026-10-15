using System.Collections.Concurrent;

namespace EasyStock.Application.Services.Notifications;

/// <summary>
/// Teto de disparos de teste por superadmin (N13): 10 por hora, em memória. É teto de segurança com uma instância de API
/// (reiniciar zera e uma segunda instância dobra o teto), não cota contábil. Recusa não consome cota.
/// </summary>
public sealed class LimitadorDisparoTeste(TimeProvider relogio)
{
    public const int LimitePorJanela = 10;
    public static readonly TimeSpan Janela = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _disparos = new();

    /// <summary>Tenta reservar um disparo. Recusado, <paramref name="esperar"/> é o tempo até a cota liberar.</summary>
    public bool TentarAdquirir(Guid superadminId, out TimeSpan esperar)
    {
        var agora = relogio.GetUtcNow();
        var fila = _disparos.GetOrAdd(superadminId, _ => new Queue<DateTimeOffset>());
        lock (fila)
        {
            while (fila.Count > 0 && agora - fila.Peek() >= Janela) fila.Dequeue();

            if (fila.Count >= LimitePorJanela)
            {
                esperar = fila.Peek() + Janela - agora;
                return false;
            }

            fila.Enqueue(agora);
            esperar = TimeSpan.Zero;
            return true;
        }
    }
}
