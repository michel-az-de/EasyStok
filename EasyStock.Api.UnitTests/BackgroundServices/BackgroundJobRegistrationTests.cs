using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Notifications.Collectors;
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
    public void AddEasyStockBackgroundJobs_RegistraCaixaDeEmail_EDesligaPorFlag()
    {
        var ligado = new ServiceCollection().AddLogging();
        ligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder().Build());
        var desligado = new ServiceCollection().AddLogging();
        desligado.AddEasyStockBackgroundJobs(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{BackgroundJobOptions.SectionName}:EnableAtendimentoCaixaEmail"] = "false" })
            .Build());

        ligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().Contain(typeof(AtendimentoCaixaEmailBackgroundService));
        desligado.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType)
            .Should().NotContain(typeof(AtendimentoCaixaEmailBackgroundService));
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

    [Fact]
    public void ColetorDeProdutosVencendoNaoNasceRegistrado()
    {
        // N12: o coletor tem três defeitos conhecidos (data UTC, CorrelationId de 65 caracteres e payload que não casa com o
        // template). Sem a flag ele falharia a cada 5 min e poluiria o log; só liga quem aceitar os defeitos.
        var desligado = new ServiceCollection();
        desligado.AddEasyStockNotificationsRepositories(new ConfigurationBuilder().Build());
        var ligado = new ServiceCollection();
        ligado.AddEasyStockNotificationsRepositories(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Notifications:Coletores:ProdutosVencendo:Habilitado"] = "true" })
            .Build());

        ColetoresRegistrados(desligado).Should().NotContain(typeof(ColetorProdutosVencendo));
        ColetoresRegistrados(desligado).Should().Contain(typeof(ColetorRotinasAgendadas));
        ColetoresRegistrados(ligado).Should().Contain(typeof(ColetorProdutosVencendo));
    }

    private static IEnumerable<Type?> ColetoresRegistrados(IServiceCollection services) =>
        services.Where(d => d.ServiceType == typeof(IColetorEventoNotificacao)).Select(d => d.ImplementationType);
}
