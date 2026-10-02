using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Notifications.Email;
using EasyStock.Infra.Notifications.Options;
using EasyStock.Infra.Notifications.Sms;
using EasyStock.Infra.Notifications.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #1292 (LGPD): e-mail e telefone do destinatário não vão para o log dos provedores. O rastro do
/// envio é o <c>OutboxId</c>, que leva à mensagem no banco para quem tem acesso. N2: o mesmo vale para os
/// stubs, o console de e-mail e os canais de SMS e WhatsApp, que logavam telefone, corpo e e-mail.
/// </summary>
public class LogsDeEnvioSemDadoPessoalTests
{
    private const string Telefone = "+5511999990001";
    private const string Email = "maria.souza@example.com";
    private const string CorpoComSegredo = "Seu código de acesso é 482913";

    private static MensagemPronta Mensagem(string destinatario, CanalNotificacao canal) =>
        new(Guid.NewGuid(), Guid.NewGuid(), destinatario, "Assunto", "Corpo", canal, CategoriaConteudoNotificacao.Transacional);

    private static MensagemPronta MensagemComCorpo(string destinatario, CanalNotificacao canal) =>
        new(Guid.NewGuid(), Guid.NewGuid(), destinatario, "Assunto", CorpoComSegredo, canal, CategoriaConteudoNotificacao.Transacional);

    private static IHttpClientFactory FabricaQueFalha()
    {
        var fabrica = Substitute.For<IHttpClientFactory>();
        fabrica.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new HandlerQueFalha()));
        return fabrica;
    }

    [Fact]
    public async Task TwilioSmsNaoLogaTelefone()
    {
        var logger = new LoggerQueGuarda<TwilioSmsProvider>();
        var provider = new TwilioSmsProvider(FabricaQueFalha(), Options.Create(new TwilioSmsOptions()), logger);
        var mensagem = Mensagem(Telefone, CanalNotificacao.Sms);

        await provider.EnviarAsync(mensagem);

        logger.Assert(Telefone, mensagem.OutboxId);
    }

    [Fact]
    public async Task ZenviaSmsNaoLogaTelefone()
    {
        var logger = new LoggerQueGuarda<ZenviaSmsProvider>();
        var provider = new ZenviaSmsProvider(FabricaQueFalha(), Options.Create(new ZenviaSmsOptions()), logger);
        var mensagem = Mensagem(Telefone, CanalNotificacao.Sms);

        await provider.EnviarAsync(mensagem);

        logger.Assert(Telefone, mensagem.OutboxId);
    }

    [Fact]
    public async Task TwilioWhatsAppNaoLogaTelefone()
    {
        var logger = new LoggerQueGuarda<TwilioWhatsAppProvider>();
        var provider = new TwilioWhatsAppProvider(FabricaQueFalha(), Options.Create(new TwilioWhatsAppOptions()), logger);
        var mensagem = Mensagem(Telefone, CanalNotificacao.WhatsApp);

        await provider.EnviarAsync(mensagem);

        logger.Assert(Telefone, mensagem.OutboxId);
    }

    [Theory]
    [InlineData("enviado")]
    [InlineData("falha")]
    [InlineData("excecao")]
    public async Task SmtpNaoLogaEmail(string cenario)
    {
        // N3 (#1351): o canal chama EnviarAsync. A excecao inesperada leva o endereco na mensagem de proposito:
        // o log do canal registra so o tipo dela.
        var email = Substitute.For<IEmailService>();
        email.EnviarAsync(Arg.Any<MensagemEmail>(), Arg.Any<CancellationToken>()).Returns(cenario switch
        {
            "falha" => Task.FromResult(new ResultadoEnvio(false, "smtp", "SMTP 550: recusado", FalhaPermanente: true)),
            "excecao" => Task.FromException<ResultadoEnvio>(new InvalidOperationException($"recusado para {Email}")),
            _ => Task.FromResult(new ResultadoEnvio(true, "smtp")),
        });
        var logger = new LoggerQueGuarda<SmtpEmailCanal>();
        var canal = new SmtpEmailCanal(email, logger);
        var mensagem = Mensagem(Email, CanalNotificacao.Email);

        await canal.EnviarAsync(mensagem);

        logger.Assert(Email, mensagem.OutboxId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StubWhatsAppNaoLogaTelefoneNemCorpo(bool falha)
    {
        var logger = new LoggerQueGuarda<StubWhatsAppProvider>();
        var stub = new StubWhatsAppProvider(logger) { SimularFalha = falha };
        var mensagem = MensagemComCorpo(Telefone, CanalNotificacao.WhatsApp);

        await stub.EnviarAsync(mensagem);

        logger.Assert([Telefone, CorpoComSegredo, "482913"], mensagem.OutboxId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StubSmsNaoLogaTelefone(bool falha)
    {
        var logger = new LoggerQueGuarda<StubSmsProvider>();
        var stub = new StubSmsProvider(logger) { SimularFalha = falha };
        var mensagem = MensagemComCorpo(Telefone, CanalNotificacao.Sms);

        await stub.EnviarAsync(mensagem);

        logger.Assert([Telefone, CorpoComSegredo, "482913"], mensagem.OutboxId);
    }

    [Fact]
    public async Task SmsCanalNaoLogaTelefone()
    {
        var provedor = Substitute.For<IProvedorSms>();
        provedor.Nome.Returns("stub");
        provedor.EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>()).Returns(ResultadoEnvio.Simulado("stub"));
        var logger = new LoggerQueGuarda<SmsCanal>();
        var mensagem = Mensagem(Telefone, CanalNotificacao.Sms);

        await new SmsCanal(provedor, logger).EnviarAsync(mensagem);

        logger.Assert(Telefone, mensagem.OutboxId);
    }

    [Fact]
    public async Task WhatsAppCanalNaoLogaTelefone()
    {
        var provedor = Substitute.For<IProvedorWhatsApp>();
        provedor.Nome.Returns("stub");
        provedor.EnviarAsync(Arg.Any<MensagemPronta>(), Arg.Any<CancellationToken>()).Returns(ResultadoEnvio.Simulado("stub"));
        var logger = new LoggerQueGuarda<WhatsAppCanal>();
        var mensagem = Mensagem(Telefone, CanalNotificacao.WhatsApp);

        await new WhatsAppCanal(provedor, new ServiceCollection().BuildServiceProvider(), logger).EnviarAsync(mensagem);

        logger.Assert(Telefone, mensagem.OutboxId);
    }

    [Fact]
    public async Task ConsoleEmailNaoLogaDestinatario()
    {
        var loggerConsole = new LoggerQueGuarda<ConsoleEmailService>();
        var loggerCanal = new LoggerQueGuarda<SmtpEmailCanal>();
        var console = new ConsoleEmailService(loggerConsole);
        var mensagem = Mensagem(Email, CanalNotificacao.Email);

        // Pelo motor: o canal reconhece o simulador, e a única linha de log é a do canal, com o OutboxId.
        await new SmtpEmailCanal(console, loggerCanal).EnviarAsync(mensagem);

        // Uso direto (cadastro, redefinição de senha, relatório): sem OutboxId, mas também sem e-mail.
        await console.SendAsync(Email, "Assunto", "Corpo");
        await console.SendAsync(Email, "Assunto", "Corpo", [new EmailAttachment("a.pdf", [1], "application/pdf")]);
        await console.SendAsync([Email], "Assunto", "Corpo");
        await console.SendTemplateAsync(Email, "Assunto", "modelo", new { Nome = "Maria" });

        loggerCanal.Assert(Email, mensagem.OutboxId);
        loggerConsole.AssertSemDadoPessoal([Email, "Corpo", "Maria"]);
    }

    private sealed class HandlerQueFalha : HttpMessageHandler
    {
        // Falha qualquer, sem resposta HTTP: o provider de envio único não repete, então o teste não espera backoff.
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("provedor fora do ar");
    }

    private sealed class LoggerQueGuarda<T> : ILogger<T>
    {
        private readonly List<string> _linhas = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var valores = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? string.Join(";", pares.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            _linhas.Add(formatter(state, exception) + "|" + valores);
        }

        public void Assert(string dadoPessoal, Guid outboxId) => Assert([dadoPessoal], outboxId);

        public void Assert(string[] dadosPessoais, Guid outboxId)
        {
            AssertSemDadoPessoal(dadosPessoais);
            _linhas.Should().OnlyContain(l => l.Contains(outboxId.ToString()), "o OutboxId é o rastro do envio");
        }

        public void AssertSemDadoPessoal(string[] dadosPessoais)
        {
            _linhas.Should().NotBeEmpty();
            foreach (var dado in dadosPessoais)
                _linhas.Should().NotContain(l => l.Contains(dado), "destinatário e corpo são dado pessoal (LGPD)");
        }
    }
}
