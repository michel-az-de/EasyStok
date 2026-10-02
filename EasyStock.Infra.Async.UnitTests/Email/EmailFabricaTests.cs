using EasyStock.Application.Ports.Output;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Async.Email;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async.UnitTests.Email;

/// <summary>
/// N3 (#1351): uma só fábrica de <see cref="IEmailService"/> para a API e o Worker. Mesma configuração, mesmo provider e
/// mesmas opções; nenhum remetente inventado; chave inválida ou faltando é nomeada.
/// </summary>
public class EmailFabricaTests
{
    private static readonly Dictionary<string, string?> SmtpCompleto = new()
    {
        ["Smtp:Host"] = "smtp.hostinger.com",
        ["Smtp:Port"] = "465",
        ["Smtp:Username"] = "avisos@easystok.online",
        ["Smtp:Password"] = "segredo",
        ["Smtp:FromEmail"] = "avisos@easystok.online",
        ["Smtp:Seguranca:Username"] = "seguranca@easystok.online",
        ["Smtp:Seguranca:Password"] = "outro-segredo",
        ["Smtp:Seguranca:FromEmail"] = "seguranca@easystok.online",
        ["ASPNETCORE_ENVIRONMENT"] = "Production",
    };

    private static Dictionary<string, string?> Sem(Dictionary<string, string?> origem, params string[] chaves) =>
        origem.Where(p => !chaves.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);

    private static Dictionary<string, string?> Com(Dictionary<string, string?> origem, params (string Chave, string? Valor)[] extras)
    {
        var copia = new Dictionary<string, string?>(origem);
        foreach (var (chave, valor) in extras)
            copia[chave] = valor;
        return copia;
    }

    private static (ServiceProvider Provedor, ColetaDeLogs Logs) Construir(Dictionary<string, string?> configuracao)
    {
        var raiz = new ConfigurationBuilder().AddInMemoryCollection(configuracao).Build();
        var logs = new ColetaDeLogs();
        var servicos = new ServiceCollection();
        servicos.AddLogging(b => b.AddProvider(logs));
        servicos.AddEasyStockEmail(raiz);
        return (servicos.BuildServiceProvider(), logs);
    }

    private static async Task IniciarHostedServicesAsync(ServiceProvider provedor)
    {
        foreach (var hosted in provedor.GetServices<IHostedService>())
            await hosted.StartAsync(CancellationToken.None);
    }

    [Fact]
    public void ProviderSmtpExplicitoComTudoResolveOSmtpEmailService()
    {
        var (provedor, _) = Construir(Com(SmtpCompleto, ("Email:Provider", "smtp")));

        provedor.GetRequiredService<IEmailService>().Should().BeOfType<SmtpEmailService>();
    }

    [Fact]
    public void SemProviderComHostPortaERemetenteEscolheSmtp()
    {
        var (provedor, _) = Construir(SmtpCompleto);

        provedor.GetRequiredService<IEmailService>().Should().BeOfType<SmtpEmailService>();
    }

    [Fact]
    public async Task SemFromEmailNemUsernameComArrobaCaiNoConsoleComAvisoQueNomeiaAChave()
    {
        var (provedor, logs) = Construir(Sem(SmtpCompleto, "Smtp:Username", "Smtp:FromEmail",
            "Smtp:Seguranca:Username", "Smtp:Seguranca:FromEmail"));

        var servico = provedor.GetRequiredService<IEmailService>();
        servico.GetType().Name.Should().Be("ConsoleEmailService", "o nome da classe é contrato do diagnóstico");

        await IniciarHostedServicesAsync(provedor);
        logs.Linhas.Should().Contain(l => l.Nivel == LogLevel.Warning && l.Texto.Contains("Smtp:FromEmail"));
        logs.Linhas.Should().NotContain(l => l.Texto.Contains("noreply@easystock.com"));
    }

    [Fact]
    public async Task SemHostCaiNoConsoleComAvisoQueNomeiaAChave()
    {
        var (provedor, logs) = Construir(Sem(SmtpCompleto, "Smtp:Host"));

        provedor.GetRequiredService<IEmailService>().GetType().Name.Should().Be("ConsoleEmailService");
        await IniciarHostedServicesAsync(provedor);
        logs.Linhas.Should().Contain(l => l.Nivel == LogLevel.Warning && l.Texto.Contains("Smtp:Host"));
    }

    [Fact]
    public void ProviderSmtpExplicitoSemHostRecusaSubirNomeandoAChave()
    {
        var act = () => Construir(Com(Sem(SmtpCompleto, "Smtp:Host"), ("Email:Provider", "smtp")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Host*");
    }

    [Fact]
    public void ProviderConsoleExplicitoIgnoraOSmtpConfigurado()
    {
        var (provedor, _) = Construir(Com(SmtpCompleto, ("Email:Provider", "console")));

        provedor.GetRequiredService<IEmailService>().GetType().Name.Should().Be("ConsoleEmailService");
    }

    [Fact]
    public async Task ProviderDesconhecidoCaiNoConsoleComAvisoQueNomeiaAChave()
    {
        var (provedor, logs) = Construir(Com(SmtpCompleto, ("Email:Provider", "pombo-correio")));

        provedor.GetRequiredService<IEmailService>().GetType().Name.Should().Be("ConsoleEmailService");
        await IniciarHostedServicesAsync(provedor);
        logs.Linhas.Should().Contain(l => l.Nivel == LogLevel.Warning && l.Texto.Contains("Email:Provider"));
    }

    [Fact]
    public void SendGridExplicitoSemRemetenteRecusaSubirNomeandoAChaveENaoInventaNoreply()
    {
        var act = () => Construir(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "sendgrid",
            ["SendGrid:ApiKey"] = "SG.chave",
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*SendGrid:FromEmail*");
    }

    [Fact]
    public void SendGridExplicitoSemApiKeyRecusaSubirNomeandoAChave()
    {
        var act = () => Construir(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "sendgrid",
            ["SendGrid:FromEmail"] = "avisos@easystok.online",
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*SendGrid:ApiKey*");
    }

    [Fact]
    public void SendGridExplicitoCompletoResolveOSendGrid()
    {
        var (provedor, _) = Construir(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "sendgrid",
            ["SendGrid:ApiKey"] = "SG.chave",
            ["SendGrid:FromEmail"] = "avisos@easystok.online",
        });

        provedor.GetRequiredService<IEmailService>().Should().BeOfType<SendGridEmailService>();
    }

    [Fact]
    public void ApiEWorkerComAMesmaConfiguracaoResolvemOMesmoProviderEAsMesmasOpcoes()
    {
        var (api, _) = Construir(SmtpCompleto);
        var (worker, _) = Construir(SmtpCompleto);

        api.GetRequiredService<IEmailService>().GetType().Should().Be(worker.GetRequiredService<IEmailService>().GetType());
        api.GetRequiredService<SmtpConfiguracao>().Should().Be(worker.GetRequiredService<SmtpConfiguracao>());
        api.GetRequiredService<SmtpConfiguracao>().Modo.Should().Be(SmtpModo.SslImplicito);
    }

    [Fact]
    public void ModoNenhumEmProductionRecusaSubirNomeandoAChave()
    {
        var act = () => Construir(Com(SmtpCompleto, ("Smtp:Modo", "Nenhum")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Modo*");
    }

    [Fact]
    public void ModoNenhumValeNoDesenvolvimentoComOMailpit()
    {
        var (provedor, _) = Construir(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "mailpit",
            ["Smtp:Port"] = "1025",
            ["Smtp:Modo"] = "Nenhum",
            ["Smtp:FromEmail"] = "avisos@easystok.local",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
        });

        provedor.GetRequiredService<IEmailService>().Should().BeOfType<SmtpEmailService>();
        provedor.GetRequiredService<SmtpConfiguracao>().Modo.Should().Be(SmtpModo.Nenhum);
    }

    [Fact]
    public void PortaInvalidaNaFabricaNomeiaAChaveEmVezDeFormatException()
    {
        var act = () => Construir(Com(SmtpCompleto, ("Smtp:Port", "quinhentos")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Smtp:Port*");
    }

    [Fact]
    public async Task SegurancaSemCaixaPropriaAvisaNaSubidaQueSaiDoRemetenteDeAvisos()
    {
        var (provedor, logs) = Construir(Sem(SmtpCompleto, "Smtp:Seguranca:Username", "Smtp:Seguranca:Password", "Smtp:Seguranca:FromEmail"));

        await IniciarHostedServicesAsync(provedor);

        logs.Linhas.Should().Contain(l => l.Nivel == LogLevel.Warning && l.Texto.Contains("Smtp:Seguranca"));
        provedor.GetRequiredService<SmtpConfiguracao>().SegurancaUsaAvisos.Should().BeTrue();
    }

    [Fact]
    public async Task SegurancaSoComFromEmailAvisaQueAutenticaComACredencialDeAvisos()
    {
        // Provedores como Gmail e SES recusam um From diferente da conta autenticada: quem configurou so o FromEmail
        // precisa saber, na subida, que a credencial e a de avisos.
        var (provedor, logs) = Construir(Sem(SmtpCompleto, "Smtp:Seguranca:Username", "Smtp:Seguranca:Password"));

        await IniciarHostedServicesAsync(provedor);

        logs.Linhas.Should().Contain(l => l.Nivel == LogLevel.Warning
            && l.Texto.Contains("Smtp:Seguranca:Username") && l.Texto.Contains("credencial de avisos"));
        logs.Linhas.Should().NotContain(l => l.Texto.Contains("propria"), "a caixa nao e propria: a credencial e a de avisos");
    }

    [Fact]
    public async Task SegurancaComCaixaPropriaNaoAvisa()
    {
        var (provedor, logs) = Construir(SmtpCompleto);

        await IniciarHostedServicesAsync(provedor);

        logs.Linhas.Should().NotContain(l => l.Nivel >= LogLevel.Warning);
    }

    [Fact]
    public async Task OsLogsDaSubidaNaoTemSenhaNemEnderecoDeCaixa()
    {
        var (provedor, logs) = Construir(SmtpCompleto);

        await IniciarHostedServicesAsync(provedor);

        logs.Linhas.Should().NotBeEmpty("a subida registra qual provider ficou ativo");
        logs.Linhas.Should().NotContain(l => l.Texto.Contains("segredo") || l.Texto.Contains('@'));
    }

    private sealed class ColetaDeLogs : ILoggerProvider
    {
        private readonly List<(LogLevel Nivel, string Texto)> _linhas = [];
        private readonly object _trava = new();

        public IReadOnlyList<(LogLevel Nivel, string Texto)> Linhas
        {
            get
            {
                lock (_trava)
                    return _linhas.ToArray();
            }
        }

        public ILogger CreateLogger(string categoryName) => new Registro(this);

        public void Dispose()
        {
        }

        private sealed class Registro(ColetaDeLogs dono) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (dono._trava)
                    dono._linhas.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
