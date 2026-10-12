using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// I3.1 (#1667): o webhook do Mercado Pago é registrado com ou sem token. Antes ele só existia com
/// <c>MercadoPago:AccessToken</c>; sem token a rota respondia 404 e a falta de configuração ficava muda.
/// </summary>
public class MercadoPagoWebhookRegistroTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("APP_USR-abc")]
    public void Validador_e_processor_registrados_com_ou_sem_token(string? token)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["MercadoPago:AccessToken"] = token,
        }).Build();
        var services = new ServiceCollection();

        services.AddEasyStockAsyncInfrastructure(configuration);

        services.Where(d => d.ServiceType == typeof(IWebhookSignatureValidator))
            .Should().ContainSingle(d => d.ImplementationType == typeof(MercadoPagoSignatureValidator));
        services.Where(d => d.ServiceType == typeof(IGatewayWebhookProcessor))
            .Should().ContainSingle(d => d.ImplementationType == typeof(MercadoPagoWebhookProcessor));
    }
}