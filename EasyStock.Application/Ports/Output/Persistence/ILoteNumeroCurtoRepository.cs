namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>Número curto do lote (#2199, D4), o do código de barras Code128 C.</summary>
public interface ILoteNumeroCurtoRepository
{
    /// <summary>
    /// Devolve o número do lote, dando um ao lote antigo que ainda não tem. Idempotente: repetir, mesmo em paralelo,
    /// devolve o mesmo número. <c>null</c> quando o lote não existe ou não é da empresa.
    /// </summary>
    Task<string?> GarantirAsync(Guid empresaId, Guid loteId, CancellationToken ct = default);
}
