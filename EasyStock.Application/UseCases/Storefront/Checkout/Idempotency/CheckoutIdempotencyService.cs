using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;

/// <summary>
/// Camada de idempotência sobre o checkout (TASK-EZ-CHECKOUT-002).
///
/// <para>
/// <strong>TentarReservarAsync:</strong> Verifica se (Key, ContentHash) já foi processado.
/// Retorna o DTO cacheado em replay; lança <see cref="IdempotencyMismatchException"/>
/// se a key existe com hash diferente; retorna null se checkout novo (reserva feita).
/// </para>
///
/// <para>
/// <strong>RegistrarRespostaAsync:</strong> Vincula FaturaId + InitPoint ao registro
/// reservado. Chamado após Fase 3 (sucesso do MercadoPago).
/// </para>
/// </summary>
public sealed class CheckoutIdempotencyService(
    ICheckoutIdempotencyRepository repo,
    ILogger<CheckoutIdempotencyService> logger)
{
    private const int ExpiresInSeconds = 1800;

    /// <summary>
    /// Verifica replay, detecta mismatch ou reserva a chave atomicamente.
    /// </summary>
    /// <returns>
    /// DTO cacheado se replay (FaturaId já vinculada); null se checkout novo.
    /// </returns>
    /// <exception cref="IdempotencyMismatchException">
    /// Key existe com ContentHash diferente — cliente alterou o carrinho.
    /// </exception>
    public async Task<CheckoutCriadoDto?> TentarReservarAsync(
        Guid key,
        string contentHash,
        CancellationToken ct = default)
    {
        var existentes = await repo.GetByKeyAsync(key, ct);

        if (existentes.Count > 0)
        {
            var comMesmoHash = existentes.FirstOrDefault(e => e.Confere(key, contentHash));

            if (comMesmoHash is not null)
            {
                if (comMesmoHash.FaturaId.HasValue && comMesmoHash.InitPoint is not null)
                {
                    logger.LogInformation(
                        "Idempotency replay: key={Key} faturaId={FaturaId}",
                        key, comMesmoHash.FaturaId.Value);
                    return new CheckoutCriadoDto(comMesmoHash.FaturaId.Value, comMesmoHash.InitPoint, ExpiresInSeconds);
                }

                // Registro existe mas sem resposta (Fase 3 falhou anteriormente) → recria.
                logger.LogInformation(
                    "Idempotency in-flight sem resposta: key={Key}, prosseguindo com nova tentativa.", key);
                return null;
            }

            // Key existe com hash diferente → carrinho foi alterado.
            logger.LogWarning(
                "Idempotency mismatch: key={Key} hash recebido difere do registrado.", key);
            throw new IdempotencyMismatchException();
        }

        // Nenhum registro: INSERT atômico para travar a key.
        var proposta = CheckoutIdempotency.Criar(key, contentHash);
        var (reservado, existente) = await repo.TentarReservarAsync(proposta, ct);

        if (!reservado)
        {
            // Race condition: outra request inseriu simultaneamente.
            if (existente.FaturaId.HasValue && existente.InitPoint is not null)
            {
                logger.LogInformation(
                    "Idempotency race-replay: key={Key} faturaId={FaturaId}",
                    key, existente.FaturaId.Value);
                return new CheckoutCriadoDto(existente.FaturaId.Value, existente.InitPoint, ExpiresInSeconds);
            }

            // A concorrente reservou mas ainda não terminou → prossegue (melhor esforço).
            logger.LogInformation(
                "Idempotency race sem resposta ainda: key={Key}, prosseguindo.", key);
        }

        return null;
    }

    /// <summary>
    /// Vincula FaturaId + InitPoint ao registro reservado após Fase 3 concluída.
    /// No-op se o registro não for encontrado (Fase 3 falhou antes de reservar).
    /// </summary>
    public async Task RegistrarRespostaAsync(
        Guid key,
        string contentHash,
        Guid faturaId,
        string initPoint,
        CancellationToken ct = default)
    {
        var registro = await repo.GetByKeyHashAsync(key, contentHash, ct);
        if (registro is null)
        {
            logger.LogWarning(
                "RegistrarResposta: registro não encontrado key={Key} — idempotência parcial.", key);
            return;
        }

        registro.VincularFatura(faturaId, initPoint);
        await repo.UpdateAsync(registro, ct);

        logger.LogInformation(
            "Idempotency resposta registrada: key={Key} faturaId={FaturaId}", key, faturaId);
    }
}
