namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>Número curto do lote (#2199, D4), o do código de barras Code128 C.</summary>
public interface ILoteNumeroCurtoRepository
{
    /// <summary>
    /// Devolve o número do lote, dando um ao lote antigo que ainda não tem. Idempotente: repetir, mesmo em paralelo,
    /// devolve o mesmo número. <c>null</c> quando o lote não existe ou não é da empresa.
    /// </summary>
    Task<string?> GarantirAsync(Guid empresaId, Guid loteId, CancellationToken ct = default);

    /// <summary>
    /// Os números já dados, numa leitura só (#2209, sync do app). Lote sem número, inexistente ou de outra empresa
    /// fica fora do resultado; o lote antigo ganha o dele por <see cref="GarantirAsync"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> NumerosDadosAsync(
        Guid empresaId, IReadOnlyCollection<Guid> loteIds, CancellationToken ct = default);
}
