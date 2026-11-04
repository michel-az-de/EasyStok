namespace EasyStock.Application.UseCases.Etiquetas.Folha;

/// <summary>
/// Quantos pedidos de folha em PDF a API atende ao mesmo tempo (#2200). A vaga cobre os QR e o render, os dois CPU
/// síncrona; quem não consegue vaga dentro da espera recebe <see cref="FolhaPdfOcupadaException"/> (503). Singleton:
/// o teto vale para o processo.
/// </summary>
public sealed class VagasDeFolhaPdf
{
    public const int VagasPadrao = 2;

    /// <summary>Folga para a fila andar: o pior pedido aceito leva cerca de 13 s entre QR e render.</summary>
    public static readonly TimeSpan EsperaPadrao = TimeSpan.FromSeconds(15);

    private readonly SemaphoreSlim _vagas;
    private readonly TimeSpan _espera;

    public VagasDeFolhaPdf(int vagas = VagasPadrao, TimeSpan? espera = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(vagas, 1);
        _vagas = new SemaphoreSlim(vagas, vagas);
        _espera = espera ?? EsperaPadrao;
    }

    /// <summary>Espera uma vaga. Devolva com <see cref="IDisposable.Dispose"/>; devolver de novo não conta.</summary>
    /// <exception cref="FolhaPdfOcupadaException">Nenhuma vaga dentro da espera.</exception>
    public async Task<IDisposable> OcuparAsync(CancellationToken ct = default)
    {
        if (!await _vagas.WaitAsync(_espera, ct)) throw new FolhaPdfOcupadaException();
        return new Vaga(_vagas);
    }

    private sealed class Vaga(SemaphoreSlim vagas) : IDisposable
    {
        private int _devolvida;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _devolvida, 1) == 0) vagas.Release();
        }
    }
}
