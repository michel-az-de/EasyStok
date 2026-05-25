using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

/// <summary>
/// Stub de desenvolvimento do MercadoPago — retorna URL fictícia imediatamente.
/// Registrado quando <c>MercadoPago:UseStub=true</c> (ambiente Development).
/// </summary>
public sealed class StubMercadoPagoClient(ILogger<StubMercadoPagoClient> logger) : IMercadoPagoClient
{
    public Task<PreferenceCriadaResult> CriarPreferenceAsync(
        CriarPreferenceCommand command,
        CancellationToken ct = default)
    {
        var preferenceId = $"stub-{command.PedidoId}";
        var initPoint = $"https://stub.mp/{command.PedidoId}";

        logger.LogInformation(
            "StubMercadoPago preference criada pedidoId={PedidoId} initPoint={InitPoint}",
            command.PedidoId, initPoint);

        return Task.FromResult(new PreferenceCriadaResult(preferenceId, initPoint));
    }

    /// <summary>
    /// Stub do <c>GetPaymentAsync</c> — status derivado do prefixo do <paramref name="paymentId"/>:
    /// <list type="bullet">
    ///   <item><c>approved-*</c> → <c>approved</c></item>
    ///   <item><c>rejected-*</c> → <c>rejected</c></item>
    ///   <item><c>pending-*</c> → <c>pending</c></item>
    ///   <item><c>orphan-*</c> → external_reference inválida</item>
    ///   <item>default → <c>approved</c></item>
    /// </list>
    /// </summary>
    public Task<MpPaymentDetailsDto> GetPaymentAsync(
        string paymentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentId))
            throw new ArgumentException("paymentId é obrigatório.", nameof(paymentId));

        var lower = paymentId.ToLowerInvariant();
        string status;
        string? statusDetail = null;
        string? externalRef = paymentId; // por convenção, stub retorna o paymentId como external_reference (testes setam Guid quando precisam)

        if (lower.StartsWith("rejected-", StringComparison.Ordinal))
        {
            status = "rejected";
            statusDetail = "cc_rejected_other_reason";
        }
        else if (lower.StartsWith("pending-", StringComparison.Ordinal))
        {
            status = "pending";
        }
        else if (lower.StartsWith("orphan-", StringComparison.Ordinal))
        {
            status = "approved";
            externalRef = "not-a-guid";
        }
        else
        {
            status = "approved";
        }

        logger.LogDebug(
            "StubMercadoPago GetPayment paymentId={PaymentId} → status={Status}",
            paymentId, status);

        return Task.FromResult(new MpPaymentDetailsDto(paymentId, status, statusDetail, externalRef, 100m));
    }
}
