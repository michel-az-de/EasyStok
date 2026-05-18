using EasyStock.Application.Ports.Output.Fiscal;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Infra.Integrations.Fiscal.Mock.DependencyInjection;

/// <summary>
/// Registra o adapter mock (<see cref="MockGatewayFiscal"/>) como adicional
/// implementacao de <see cref="IGatewayFiscal"/>. Convive lado-a-lado com
/// adapters reais (Focus NFe, eNotas) — selecao por tenant via
/// <see cref="IGatewayFiscalFactory"/>.
/// </summary>
public static class MockFiscalServiceCollectionExtensions
{
    public static IServiceCollection AddMockFiscalGateway(this IServiceCollection services)
    {
        // Multi-impl: adicionar SEM remover o gateway real. O factory resolve por
        // ConfigFiscalDto.Provedor; tenant com Provedor="mock" usa este adapter,
        // tenant com Provedor="focus" usa o FocusNFeAdapter.
        services.AddScoped<IGatewayFiscal, MockGatewayFiscal>();
        return services;
    }
}
