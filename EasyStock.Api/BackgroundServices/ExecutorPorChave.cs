namespace EasyStock.Api.BackgroundServices;

/// <summary>
/// Executa trabalhos em paralelo entre chaves e em ordem dentro de cada chave. Trabalhos da mesma chave
/// formam uma cadeia (cada um espera o anterior); chaves diferentes concorrem, limitadas por um
/// <see cref="SemaphoreSlim"/> global. #3291: cada trabalho pode pertencer a um grupo (a loja) com teto próprio, para
/// uma loja com muitas conversas não ocupar todas as vagas. O teto do grupo é pedido antes da vaga global, então quem
/// espera a própria loja não segura vaga que outra loja poderia usar. Exceção de um trabalho vai ao callback e não
/// quebra a cadeia. A entrada da chave e a do grupo saem dos dicionários quando o último trabalho termina, sob o mesmo
/// lock do agendamento, então não há corrida entre remover e agendar.
/// </summary>
public sealed class ExecutorPorChave
{
    private readonly SemaphoreSlim _semaforo;
    private readonly int _tetoPorGrupo;
    private readonly Action<Guid, Exception> _aoFalhar;
    private readonly Dictionary<Guid, Cadeia> _cadeias = new();
    private readonly Dictionary<Guid, Grupo> _grupos = new();
    private readonly object _sync = new();
    private int _trabalhosPendentes;

    /// <param name="paralelismo">Vagas globais: quantos trabalhos rodam ao mesmo tempo, somando todos os grupos.</param>
    /// <param name="aoFalhar">Recebe a chave e a exceção do trabalho que falhou.</param>
    /// <param name="tetoPorGrupo">Quantas chaves do mesmo grupo rodam ao mesmo tempo; sem limite por padrão.</param>
    public ExecutorPorChave(int paralelismo, Action<Guid, Exception> aoFalhar, int tetoPorGrupo = int.MaxValue)
    {
        if (paralelismo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(paralelismo), "O paralelismo deve ser pelo menos 1.");
        }

        if (tetoPorGrupo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tetoPorGrupo), "O teto por grupo deve ser pelo menos 1.");
        }

        ArgumentNullException.ThrowIfNull(aoFalhar);
        _semaforo = new SemaphoreSlim(paralelismo);
        _tetoPorGrupo = tetoPorGrupo;
        _aoFalhar = aoFalhar;
    }

    /// <summary>Chaves com trabalho agendado ou em curso.</summary>
    public int ChavesAtivas
    {
        get { lock (_sync) return _cadeias.Count; }
    }

    /// <summary>Grupos com trabalho agendado ou em curso.</summary>
    public int GruposAtivos
    {
        get { lock (_sync) return _grupos.Count; }
    }

    /// <summary>Trabalhos agendados que ainda não terminaram.</summary>
    public int TrabalhosPendentes
    {
        get { lock (_sync) return _trabalhosPendentes; }
    }

    /// <summary>Agenda sem grupo: a própria chave é o grupo, então o teto por grupo não limita nada além da vaga global.</summary>
    public void Agendar(Guid chave, Func<CancellationToken, Task> trabalho, CancellationToken ct = default) =>
        Agendar(chave, chave, trabalho, ct);

    /// <summary>
    /// Agenda o trabalho na cadeia da <paramref name="chave"/> (a conversa) e sob o teto do <paramref name="grupo"/> (a loja).
    /// O <paramref name="ct"/> vale para a espera pelas vagas e é o que o trabalho recebe.
    /// </summary>
    public void Agendar(Guid chave, Guid grupo, Func<CancellationToken, Task> trabalho, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trabalho);

        lock (_sync)
        {
            if (!_cadeias.TryGetValue(chave, out var cadeia))
            {
                cadeia = new Cadeia();
                _cadeias[chave] = cadeia;
            }

            if (!_grupos.TryGetValue(grupo, out var vagas))
            {
                vagas = new Grupo(grupo, _tetoPorGrupo);
                _grupos[grupo] = vagas;
            }

            cadeia.Contador++;
            vagas.Contador++;
            _trabalhosPendentes++;
            cadeia.Ultima = ExecutarAsync(chave, cadeia, vagas, cadeia.Ultima, trabalho, ct);
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
        Guid chave, Cadeia cadeia, Grupo grupo, Task anterior, Func<CancellationToken, Task> trabalho, CancellationToken ct)
    {
        // Sai do lock de quem agendou antes de qualquer trabalho síncrono.
        await Task.Yield();
        try
        {
            // A tarefa anterior nunca falha: todas as exceções são capturadas abaixo.
            await anterior.ConfigureAwait(false);

            // Só se solta o que foi pedido: cancelar na espera de uma vaga não devolve vaga que não se tinha.
            await grupo.Vagas.WaitAsync(ct).ConfigureAwait(false);
            try
            {
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
            finally
            {
                grupo.Vagas.Release();
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
            Concluir(chave, cadeia, grupo);
        }
    }

    private void Concluir(Guid chave, Cadeia cadeia, Grupo grupo)
    {
        lock (_sync)
        {
            cadeia.Contador--;
            grupo.Contador--;
            _trabalhosPendentes--;

            if (cadeia.Contador == 0
                && _cadeias.TryGetValue(chave, out var atual)
                && ReferenceEquals(atual, cadeia))
            {
                _cadeias.Remove(chave);
            }

            if (grupo.Contador == 0
                && _grupos.TryGetValue(grupo.Id, out var guardado)
                && ReferenceEquals(guardado, grupo))
            {
                _grupos.Remove(grupo.Id);
            }
        }
    }

    private sealed class Cadeia
    {
        public Task Ultima { get; set; } = Task.CompletedTask;

        public int Contador { get; set; }
    }

    private sealed class Grupo(Guid id, int teto)
    {
        public Guid Id { get; } = id;

        public SemaphoreSlim Vagas { get; } = new(teto);

        public int Contador { get; set; }
    }
}
