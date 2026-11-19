namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Executa trabalhos em paralelo entre chaves e em ordem dentro de cada chave. Trabalhos da mesma chave
/// formam uma cadeia (cada um espera o anterior); chaves diferentes concorrem, limitadas por um
/// <see cref="SemaphoreSlim"/>. Exceção de um trabalho vai ao callback e não quebra a cadeia.
/// A entrada da chave sai do dicionário quando a última tarefa da cadeia termina, sob o mesmo lock do agendamento,
/// então não há corrida entre remover e agendar.
/// </summary>
public sealed class ExecutorPorChave
{
    private readonly SemaphoreSlim _semaforo;
    private readonly Action<Guid, Exception> _aoFalhar;
    private readonly Dictionary<Guid, Cadeia> _cadeias = new();
    private readonly object _sync = new();
    private int _trabalhosPendentes;

    public ExecutorPorChave(int paralelismo, Action<Guid, Exception> aoFalhar)
    {
        if (paralelismo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(paralelismo), "O paralelismo deve ser pelo menos 1.");
        }

        ArgumentNullException.ThrowIfNull(aoFalhar);
        _semaforo = new SemaphoreSlim(paralelismo);
        _aoFalhar = aoFalhar;
    }

    /// <summary>Chaves com trabalho agendado ou em curso.</summary>
    public int ChavesAtivas
    {
        get { lock (_sync) return _cadeias.Count; }
    }

    /// <summary>Trabalhos agendados que ainda não terminaram.</summary>
    public int TrabalhosPendentes
    {
        get { lock (_sync) return _trabalhosPendentes; }
    }

    public void Agendar(Guid chave, Func<CancellationToken, Task> trabalho, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trabalho);

        lock (_sync)
        {
            if (!_cadeias.TryGetValue(chave, out var cadeia))
            {
                cadeia = new Cadeia();
                _cadeias[chave] = cadeia;
            }

            cadeia.Contador++;
            _trabalhosPendentes++;
            cadeia.Ultima = ExecutarAsync(chave, cadeia, cadeia.Ultima, trabalho, ct);
        }
    }

    /// <summary>Espera as cadeias que existem agora. Trabalhos agendados depois não são aguardados.</summary>
    public Task AguardarTudoAsync()
    {
        Task[] pendentes;
        lock (_sync)
        {
            pendentes = _cadeias.Values.Select(c => c.Ultima).ToArray();
        }

        return Task.WhenAll(pendentes);
    }

    private async Task ExecutarAsync(
        Guid chave, Cadeia cadeia, Task anterior, Func<CancellationToken, Task> trabalho, CancellationToken ct)
    {
        // Sai do lock de quem agendou antes de qualquer trabalho síncrono.
        await Task.Yield();
        try
        {
            // A tarefa anterior nunca falha: todas as exceções são capturadas abaixo.
            await anterior.ConfigureAwait(false);

            await _semaforo.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await trabalho(ct).ConfigureAwait(false);
            }
            finally
            {
                _semaforo.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Desligamento: o trabalho não rodou ou foi interrompido. Não é falha.
        }
        catch (Exception ex)
        {
            try
            {
                _aoFalhar(chave, ex);
            }
            catch
            {
                // O callback de erro não pode derrubar a cadeia nem o processo.
            }
        }
        finally
        {
            Concluir(chave, cadeia);
        }
    }

    private void Concluir(Guid chave, Cadeia cadeia)
    {
        lock (_sync)
        {
            cadeia.Contador--;
            _trabalhosPendentes--;

            if (cadeia.Contador == 0
                && _cadeias.TryGetValue(chave, out var atual)
                && ReferenceEquals(atual, cadeia))
            {
                _cadeias.Remove(chave);
            }
        }
    }

    private sealed class Cadeia
    {
        public Task Ultima { get; set; } = Task.CompletedTask;

        public int Contador { get; set; }
    }
}
