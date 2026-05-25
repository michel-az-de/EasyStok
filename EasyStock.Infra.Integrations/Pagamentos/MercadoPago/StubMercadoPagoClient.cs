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
}
