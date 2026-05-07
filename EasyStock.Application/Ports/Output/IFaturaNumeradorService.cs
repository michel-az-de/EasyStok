using System;
using System.Threading;
using System.Threading.Tasks;

namespace EasyStock.Application.Ports.Output;

/// <summary>
/// Geracao atomica de numero sequencial fiscal-friendly por empresa+ano para
/// <see cref="Domain.Entities.Fatura"/>. Implementacao usa
/// <c>INSERT ... ON CONFLICT DO UPDATE RETURNING</c> em transacao curta para
/// evitar race conditions em multi-pod.
/// </summary>
public interface IFaturaNumeradorService
{
    /// <summary>
    /// Reserva e retorna o proximo numero formatado <c>YYYY-NNNNNN</c> para a
    /// empresa+ano da <paramref name="dataEmissao"/>. Operacao atomica.
    /// </summary>
    /// <remarks>
    /// O numero e CONSUMIDO ao chamar este metodo — se a transacao da Fatura
    /// abortar depois, aquele numero e perdido (gap aceitavel para nao-fiscal).
    /// </remarks>
    Task<string> GerarAsync(Guid empresaId, DateTime dataEmissao, CancellationToken ct = default);
}
