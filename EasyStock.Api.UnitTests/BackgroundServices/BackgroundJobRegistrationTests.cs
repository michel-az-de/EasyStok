using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EasyStock.Api.UnitTests.BackgroundServices;

public class BackgroundJobRegistrationTests
{
    [Fact]
    public void AddEasyStockBackgroundJobs_DeveRegistrarApenasJobPadrao_QuandoFlagsNaoConfiguradas()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddLogging();

        services.AddEasyStockBackgroundJobs(configuration);

        var hostedServices = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        hostedServices.Should().Contain(typeof(AnalisadorEstoqueBackgroundService));
        hostedServices.Should().NotContain(typeof(AlertasEstoqueJob));
        hostedServices.Should().NotContain(typeof(ProcessarRecebimentoJob));
        hostedServices.Should().NotContain(typeof(RecalcularVelocidadesJob));
        hostedServices.Should().NotContain(typeof(RelatorioMensalJob));
    }

    [Fact]
    public void AddEasyStockBackgroundJobs_DeveRegistrarJobsLegados_QuandoFlagsEstiveremHabilitadas()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{BackgroundJobOptions.SectionName}:EnableAlertasEstoqueJob"] = "true",
                [$"{BackgroundJobOptions.SectionName}:EnableProcessarRecebimentoJob"] = "true",
                [$"{BackgroundJobOptions.SectionName}:EnableRecalcularVelocidadesJob"] = "true",
                [$"{BackgroundJobOptions.SectionName}:EnableRelatorioMensalJob"] = "true"
            })
            .Build();

        services.AddLogging();

        services.AddEasyStockBackgroundJobs(configuration);

        var hostedServices = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        hostedServices.Should().Contain(typeof(AnalisadorEstoqueBackgroundService));
        hostedServices.Should().Contain(typeof(AlertasEstoqueJob));
        hostedServices.Should().Contain(typeof(ProcessarRecebimentoJob));
        hostedServices.Should().Contain(typeof(RecalcularVelocidadesJob));
        hostedServices.Should().Contain(typeof(RelatorioMensalJob));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IPedidoFornecedorRecebimentoProcessor) &&
            descriptor.ImplementationType == typeof(NoOpPedidoFornecedorRecebimentoProcessor));
    }
}
