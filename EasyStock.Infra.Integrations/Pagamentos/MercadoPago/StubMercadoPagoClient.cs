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
        // S11: um pedido pode ter mais de uma cobrança (reemissão, troca de forma); a chave de
        // idempotência distingue cada uma e mantém o índice único (Provedor, ReferenciaExterna).
        var chave = command.IdempotencyKey ?? command.PedidoId.ToString();
        var preferenceId = $"stub-{chave}";
        var initPoint = $"https://stub.mp/{chave}";

        logger.LogInformation(
            "StubMercadoPago preference criada pedidoId={PedidoId} initPoint={InitPoint}",
            command.PedidoId, initPoint);

        return Task.FromResult(new PreferenceCriadaResult(preferenceId, initPoint));
    }
}
