using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Infra.Async.DependencyInjection;
using FluentAssertions;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>
/// N3 (#1351): sem SMTP configurado nada sai, e o resultado diz isso: <see cref="DesfechoEnvio.Simulado"/> com provider
/// <c>console</c>, nunca <c>smtp</c>. O nome da classe segue sendo contrato do diagnóstico.
/// </summary>
public class ConsoleEmailServiceTests
{
    [Fact]
    public async Task EnviarAsyncDevolveSimuladoComProviderConsole()
    {
        IEmailService console = new ConsoleEmailService(new ColetorDeLogs<ConsoleEmailService>());

        var resultado = await console.EnviarAsync(new MensagemEmail("maria.souza@example.com", "Assunto", "Corpo secreto"));

        resultado.Desfecho.Should().Be(DesfechoEnvio.Simulado);
        resultado.ProviderUsado.Should().Be("console");
        resultado.Sucesso.Should().BeTrue("quem está fora do motor segue tratando o simulado como sucesso");
    }

    [Fact]
    public async Task EnviarAsyncNaoLogaDestinatarioNemCorpo()
    {
        var logs = new ColetorDeLogs<ConsoleEmailService>();
        var outboxId = Guid.NewGuid();

        await new ConsoleEmailService(logs).EnviarAsync(
            new MensagemEmail("maria.souza@example.com", "Assunto", "Corpo secreto", OutboxId: outboxId));

        logs.Linhas.Should().NotContain(l => l.Texto.Contains("maria.souza") || l.Texto.Contains("Corpo secreto"));
        logs.Linhas.Should().OnlyContain(l => l.Texto.Contains(outboxId.ToString()));
    }

    [Fact]
    public void ONomeDaClasseContinuaSendoContratoDoDiagnostico()
    {
        typeof(ConsoleEmailService).Name.Should().Be("ConsoleEmailService");
    }
}
