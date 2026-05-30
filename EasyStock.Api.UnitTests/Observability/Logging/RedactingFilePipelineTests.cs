using EasyStock.Api.Observability.Logging;
using FluentAssertions;
using Serilog;

namespace EasyStock.Api.UnitTests.Observability.Logging;

/// <summary>
/// End-to-end do pipeline Serilog + <see cref="RedactingTextFormatter"/> escrevendo
/// no arquivo (sink File com formatter custom). Valida que o **arquivo no disco**
/// — vetor que vai pro log shipper (Datadog/Sentry/backup) — nao contem segredos.
///
/// Sem WebApplicationFactory: o objetivo aqui e provar o pipeline Serilog
/// (LoggerConfiguration -> File sink -> RedactingTextFormatter -> arquivo). Subir o
/// app inteiro nao adiciona valor pra este teste — a regressao real seria no formatter
/// ou na escolha do sink, ambos exercitados aqui standalone.
///
/// [Trait Category=SecurityRegression]: regressao = vazamento de credencial no arquivo
/// de log de producao. [Fact] (nao SkippableFact) — falha em CI sem skip silencioso.
/// </summary>
[Trait("Category", "SecurityRegression")]
public class RedactingFilePipelineTests : IDisposable
{
    private readonly string _tempLogPath;

    public RedactingFilePipelineTests()
    {
        // Path unico por teste para evitar interferencia com Serilog file lock entre runs.
        _tempLogPath = Path.Combine(
            Path.GetTempPath(),
            $"easystock-redaction-test-{Guid.NewGuid():N}.log");
    }

    [Fact]
    public void Pipeline_DeveRedactar_ConnStringEmException_NoArquivoEscritoEmDisco()
    {
        // Arrange: pipeline Serilog identico ao de producao em escopo minimo.
        var formatter = new RedactingTextFormatter(
            "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        var logger = new LoggerConfiguration()
            .WriteTo.File(formatter, _tempLogPath, shared: false)
            .CreateLogger();

        var ex = new InvalidOperationException(
            "Failed to connect to database: Host=db.prod;Port=5432;Database=app;Username=u;Password=ProdSecretXYZ");

        // Act: simula o caminho real — logger.Error(exception, "ctx")
        logger.Error(ex, "Database operation failed");
        logger.Dispose(); // CloseAndFlush garante write to disk

        // Assert: arquivo no disco nao contem o segredo
        File.Exists(_tempLogPath).Should().BeTrue();
        var contents = File.ReadAllText(_tempLogPath);

        contents.Should().NotContain("ProdSecretXYZ", "credencial nao pode chegar ao arquivo");
        contents.Should().Contain("Password=[REDACTED]");
        contents.Should().Contain("Host=db.prod", "host preservado para diagnostico");
        contents.Should().Contain("Database operation failed", "mensagem de log preservada");
    }

    [Fact]
    public void Pipeline_DeveRedactar_PasswordEmMessageTemplate_NoArquivoEscritoEmDisco()
    {
        var formatter = new RedactingTextFormatter(
            "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        var logger = new LoggerConfiguration()
            .WriteTo.File(formatter, _tempLogPath, shared: false)
            .CreateLogger();

        // Pattern: cliente loga payload literal com chave=valor (anti-pattern conhecido).
        // Redaction at-rest cobre mesmo nesses casos.
        logger.Warning("Auth request body: password=topSecret123 from {Ip}", "1.2.3.4");
        logger.Dispose();

        var contents = File.ReadAllText(_tempLogPath);

        contents.Should().NotContain("topSecret123");
        contents.Should().Contain("password=[REDACTED]");
        contents.Should().Contain("1.2.3.4"); // IP preservado
    }

    public void Dispose()
    {
        try { File.Delete(_tempLogPath); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }
}
