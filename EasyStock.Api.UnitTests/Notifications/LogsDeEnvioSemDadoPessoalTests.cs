using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Notifications.Email;
using EasyStock.Infra.Notifications.Options;
using EasyStock.Infra.Notifications.Sms;
using EasyStock.Infra.Notifications.WhatsApp;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Notifications;

/// <summary>
/// #1292 (LGPD): e-mail e telefone do destinatário não vão para o log dos provedores. O rastro do
/// envio é o <c>OutboxId</c>, que leva à mensagem no banco para quem tem acesso.
/// </summary>
public class LogsDeEnvioSemDadoPessoalTests
{
    private const string Telefone = "+5511999990001";
    private const string Email = "maria.souza@example.com";

    private static MensagemPronta Mensagem(string destinatario, CanalNotificacao canal) =>
        new(Guid.NewGuid(), Guid.NewGuid(), destinatario, "Assunto", "Corpo", canal, CategoriaConteudoNotificacao.Transacional);

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
    [InlineData(true)]
    [InlineData(false)]
    public async Task SmtpNaoLogaEmail(bool falha)
    {
        var email = Substitute.For<IEmailService>();
        if (falha)
            email.SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
                .Returns(Task.FromException(new InvalidOperationException("recusado")));
        var logger = new LoggerQueGuarda<SmtpEmailCanal>();
        var canal = new SmtpEmailCanal(email, logger);
        var mensagem = Mensagem(Email, CanalNotificacao.Email);

        await canal.EnviarAsync(mensagem);

        logger.Assert(Email, mensagem.OutboxId);
    }

    private sealed class HandlerQueFalha : HttpMessageHandler
    {
        // Exceção que nenhum pipeline repete: o teste não espera backoff.
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

        public void Assert(string dadoPessoal, Guid outboxId)
        {
            _linhas.Should().NotBeEmpty();
            _linhas.Should().NotContain(l => l.Contains(dadoPessoal), "destinatário é dado pessoal (LGPD)");
            _linhas.Should().OnlyContain(l => l.Contains(outboxId.ToString()), "o OutboxId é o rastro do envio");
        }
    }
}
