using System.Security.Cryptography;
using System.Text;
using EasyStock.Infra.Async.Pagamentos.Webhooks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Api.UnitTests.Pagamentos;

public class EfiPixSignatureValidatorTests
{
    private const string Secret = "topsecret-test-key-32-chars-min!!";

    private static EfiPixSignatureValidator BuildValidator(
        Dictionary<string, string?>? cfg = null,
        string environmentName = "Development",
        ILogger<EfiPixSignatureValidator>? logger = null)
    {
        cfg ??= new Dictionary<string, string?> { ["Efi:WebhookSecret"] = Secret };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(cfg).Build();
        return new EfiPixSignatureValidator(
            configuration,
            logger ?? NullLogger<EfiPixSignatureValidator>.Instance,
            new FakeHostEnvironment(environmentName));
    }

    private static string ComputeHmac(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public void Validar_SemHeaderSignature_RetornaFalse()
    {
        var v = BuildValidator();
        v.Validar("{}", new Dictionary<string, string?>()).Should().BeFalse();
    }

    [Fact]
    public void Validar_HmacCorretoSemTimestamp_RetornaTrue()
    {
        var v = BuildValidator();
        var body = "{\"pix\":[]}";
        var sig = ComputeHmac(Secret, body);
        var ok = v.Validar(body, new Dictionary<string, string?> { ["X-Efi-Signature"] = sig });
        ok.Should().BeTrue();
    }

    [Fact]
    public void Validar_HmacIncorreto_RetornaFalse()
    {
        var v = BuildValidator();
        var ok = v.Validar("{}", new Dictionary<string, string?> { ["X-Efi-Signature"] = "deadbeef" });
        ok.Should().BeFalse();
    }

    [Fact]
    public void Validar_TimestampValido_IncluiTsNaSign()
    {
        var v = BuildValidator();
        var body = "{}";
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var toSign = $"{ts}.{body}";
        var sig = ComputeHmac(Secret, toSign);

        var ok = v.Validar(body, new Dictionary<string, string?>
        {
            ["X-Efi-Timestamp"] = ts,
            ["X-Efi-Signature"] = sig
        });
        ok.Should().BeTrue();
    }

    [Fact]
    public void Validar_TimestampForaJanela_RetornaFalse()
    {
        var v = BuildValidator();
        var body = "{}";
        var tsAntigo = (DateTimeOffset.UtcNow.AddMinutes(-10)).ToUnixTimeMilliseconds().ToString();
        var sig = ComputeHmac(Secret, $"{tsAntigo}.{body}");

        var ok = v.Validar(body, new Dictionary<string, string?>
        {
            ["X-Efi-Timestamp"] = tsAntigo,
            ["X-Efi-Signature"] = sig
        });
        ok.Should().BeFalse();
    }

    [Fact]
    public void Validar_SemSecret_RecuasaSemAllowUnsigned()
    {
        var v = BuildValidator(new Dictionary<string, string?>());
        v.Validar("{}", new Dictionary<string, string?>()).Should().BeFalse();
    }

    [Fact]
    public void Validar_SemSecret_AceitaQuandoAllowUnsignedTrue()
    {
        var v = BuildValidator(new Dictionary<string, string?>
        {
            ["Efi:WebhookAllowUnsigned"] = "true"
        });
        v.Validar("{}", new Dictionary<string, string?>()).Should().BeTrue();
    }

    [Fact]
    public void Validar_SemSecret_AllowUnsigned_EmProduction_RecusaELogaErro()
    {
        // Mesma config do teste acima (AllowUnsigned=true, sem secret), mas em
        // Production o escape hatch e ignorado: recusa e loga Error (fail-secure).
        var logger = new CapturingLogger<EfiPixSignatureValidator>();
        var v = BuildValidator(
            new Dictionary<string, string?> { ["Efi:WebhookAllowUnsigned"] = "true" },
            environmentName: "Production",
            logger: logger);

        v.Validar("{}", new Dictionary<string, string?>()).Should().BeFalse();
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "EasyStock.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
