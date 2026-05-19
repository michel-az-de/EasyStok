using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Exceptions;

namespace EasyStock.Infra.Integrations.Fiscal;

/// <summary>
/// Implementacao de <see cref="IGatewayFiscalFactory"/> baseada em
/// resolucao por <c>IEnumerable&lt;IGatewayFiscal&gt;</c> do container DI. Cada
/// adapter registrado contribui com seu <see cref="IGatewayFiscal.Provedor"/>
/// como chave. Lookup e case-insensitive.
/// </summary>
public sealed class GatewayFiscalFactory : IGatewayFiscalFactory
{
    private readonly IReadOnlyDictionary<string, IGatewayFiscal> _porProvedor;

    public GatewayFiscalFactory(IEnumerable<IGatewayFiscal> gateways)
    {
        ArgumentNullException.ThrowIfNull(gateways);
        _porProvedor = gateways.ToDictionary(g => g.Provedor, StringComparer.OrdinalIgnoreCase);
    }

    public IGatewayFiscal ObterPara(string provedor)
    {
        if (string.IsNullOrWhiteSpace(provedor))
            throw new RegraDeDominioVioladaException("Provedor fiscal obrigatorio para resolucao do gateway.");

        if (_porProvedor.TryGetValue(provedor.Trim(), out var gateway))
            return gateway;

        var disponiveis = string.Join(", ", _porProvedor.Keys);
        throw new RegraDeDominioVioladaException(
            $"Provedor fiscal '{provedor}' nao registrado. Disponiveis: [{disponiveis}].");
    }
}
