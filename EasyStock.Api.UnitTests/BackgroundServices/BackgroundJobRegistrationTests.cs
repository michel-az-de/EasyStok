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
    public void AddEasyStockBackgroundJobs_RegistraDrenoDaFilaDeMidiaDoAtendimento_NoProcessoDaApi()
    {
        // A fila é em memória (BackgroundQueueService singleton): quem drena precisa estar no mesmo
        // processo que o webhook enfileira. No Worker ela nunca recebia nada.
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddEasyStockBackgroundJobs(new ConfigurationBuilder().Build());

        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .Should().Contain(typeof(AtendimentoFilaMidiaBackgroundService));
    }

    [Fact]
    public void AddEasyStockBackgroundJobs_RegistraDisparadorDeMensagensProgramadas_EDesligaPorFlag()
    {
        var ligado = new ServiceCollection().AddLogging();
        ligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder().Build());
        var desligado = new ServiceCollection().AddLogging();
        desligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{BackgroundJobOptions.SectionName}:EnableMensagensProgramadas"] = "false" })
            .Build());

        ligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().Contain(typeof(MensagensProgramadasBackgroundService));
        desligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().NotContain(typeof(MensagensProgramadasBackgroundService));
    }

    [Fact]
    public void AddEasyStockBackgroundJobs_RegistraReenvioDeMensagens_EDesligaPorFlag()
    {
        var ligado = new ServiceCollection().AddLogging();
        ligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder().Build());
        var desligado = new ServiceCollection().AddLogging();
        desligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{BackgroundJobOptions.SectionName}:EnableReenvioMensagens"] = "false" })
            .Build());

        ligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().Contain(typeof(ReenvioMensagensBackgroundService));
        desligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().NotContain(typeof(ReenvioMensagensBackgroundService));
    }

    [Fact]
    public void AddEasyStockBackgroundJobs_RegistraAvaliadorDeLembretes_EDesligaPorFlag()
    {
        var ligado = new ServiceCollection().AddLogging();
        ligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder().Build());
        var desligado = new ServiceCollection().AddLogging();
        desligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{BackgroundJobOptions.SectionName}:EnableAvaliadorLembretes"] = "false" })
            .Build());

        ligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().Contain(typeof(AvaliadorLembretesBackgroundService));
        desligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().NotContain(typeof(AvaliadorLembretesBackgroundService), "desligar o avaliador é o rollback do S43");
    }

    [Fact]
    public void AddEasyStockBackgroundJobs_RegistraPedidoAtrasoJob_EDesligaPorFlag()
    {
        var ligado = new ServiceCollection().AddLogging();
        ligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder().Build());
        var desligado = new ServiceCollection().AddLogging();
        desligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{BackgroundJobOptions.SectionName}:EnablePedidoAtraso"] = "false" })
            .Build());

        ligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().Contain(typeof(PedidoAtrasoJob));
        desligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().NotContain(typeof(PedidoAtrasoJob), "desligar o job é o rollback da S21");
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
