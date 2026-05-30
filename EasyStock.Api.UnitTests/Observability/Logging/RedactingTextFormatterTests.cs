using EasyStock.Api.Observability.Logging;
using FluentAssertions;
using Serilog.Events;
using Serilog.Parsing;

namespace EasyStock.Api.UnitTests.Observability.Logging;

/// <summary>
/// Unit tests do formatter Serilog que aplica <see cref="SensitivePatterns.Redact"/>
/// sobre o output renderizado. Diferente do <see cref="SensitivePatternsTests"/>,
/// aqui validamos a integracao com LogEvent + template (incluindo Exception).
///
/// Em CI sem Docker, [Fact] (nao SkippableFact) — falha de build, nao skip silencioso.
/// </summary>
[Trait("Category", "SecurityRegression")]
public class RedactingTextFormatterTests
{
    private const string TestTemplate = "[{Level:u3}] {Message:lj}{NewLine}{Exception}";

    [Fact]
    public void Format_DeveMascarar_PasswordNoMessageTemplate()
    {
        var formatter = new RedactingTextFormatter(TestTemplate);
        var logEvent = MakeEvent("Login com password=segredo123 falhou", exception: null);

        var output = Render(formatter, logEvent);

        output.Should().NotContain("segredo123");
        output.Should().Contain("password=[REDACTED]");
    }

    [Fact]
    public void Format_DeveMascarar_ConnStringNoExceptionToString()
    {
        // Vetor critico — DbUpdateException coloca conn string no .Message da exception.
        // Sink File serializa via {Exception} placeholder; sem redaction, vai pro disco em texto plano.
        var ex = new InvalidOperationException(
            "Failed to connect: Host=db.prod;Port=5432;Database=app;Username=u;Password=ProdSecret999");
        var formatter = new RedactingTextFormatter(TestTemplate);
        var logEvent = MakeEvent("Database error", exception: ex);

        var output = Render(formatter, logEvent);

        output.Should().NotContain("ProdSecret999");
        output.Should().Contain("Password=[REDACTED]");
        output.Should().Contain("Host=db.prod"); // Host preservado para diagnostico
        output.Should().Contain("Database error"); // message intacto
    }

    [Fact]
    public void Format_DeveMascarar_JwtNoStackTrace()
    {
        // StackTrace tambem entra no {Exception} — qualquer JWT vazado em mensagem propagada
        // de excecao acaba aqui.
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4ifQ.AbCdEfGhIjKlMnOpQrStUvWxYz0123456789";
        var ex = new UnauthorizedAccessException($"invalid token: {jwt}");
        var formatter = new RedactingTextFormatter(TestTemplate);
        var logEvent = MakeEvent("Auth failed", exception: ex);

        var output = Render(formatter, logEvent);

        output.Should().NotContain(jwt);
        output.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void Format_NaoDeveAlterar_LogSemSegredo()
    {
        var formatter = new RedactingTextFormatter(TestTemplate);
        var logEvent = MakeEvent("Pedido 42 criado com sucesso", exception: null);

        var output = Render(formatter, logEvent);

        output.Should().Contain("Pedido 42 criado com sucesso");
        output.Should().NotContain("[REDACTED]");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static LogEvent MakeEvent(string messageTemplate, Exception? exception)
    {
        var parser = new MessageTemplateParser();
        var template = parser.Parse(messageTemplate);
        return new LogEvent(
            timestamp: DateTimeOffset.UtcNow,
            level: LogEventLevel.Error,
            exception: exception,
            messageTemplate: template,
            properties: []);
    }

    private static string Render(RedactingTextFormatter formatter, LogEvent logEvent)
    {
        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);
        return writer.ToString();
    }
}
