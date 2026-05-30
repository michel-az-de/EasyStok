using EasyStock.Api.Observability.Logging;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Observability.Logging;

/// <summary>
/// Categoria SecurityRegression: regressao aqui = vazamento de credencial.
/// Em CI sem Docker, estes sao [Fact] (nao SkippableFact) — falha de build, nao skip silencioso.
/// </summary>
[Trait("Category", "SecurityRegression")]
public class SensitivePatternsTests
{
    // ── Padrao 1: chave=valor ────────────────────────────────────────────────

    [Fact]
    public void Redact_DeveMascarar_PasswordIgualValor()
    {
        var input = "Login attempt: password=segredo123 from 1.2.3.4";

        var output = SensitivePatterns.Redact(input);

        output.Should().NotContain("segredo123");
        output.Should().Contain("password=[REDACTED]");
        output.Should().Contain("from 1.2.3.4"); // resto intacto
    }

    [Fact]
    public void Redact_DeveMascarar_SenhaPortugues()
    {
        var output = SensitivePatterns.Redact("user logged with senha:minhaSenh@123");

        output.Should().NotContain("minhaSenh@123");
        output.Should().Contain("senha:[REDACTED]");
    }

    [Fact]
    public void Redact_DeveMascarar_BearerToken()
    {
        var output = SensitivePatterns.Redact("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.abc.xyz");

        output.Should().NotContain("eyJhbGciOiJIUzI1NiJ9");
        output.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void Redact_DeveMascarar_ApiKeyVariantes()
    {
        var input = "headers: apikey=KEY1 api_key=KEY2 api-key=KEY3";

        var output = SensitivePatterns.Redact(input);

        output.Should().NotContain("KEY1");
        output.Should().NotContain("KEY2");
        output.Should().NotContain("KEY3");
    }

    // ── Padrao 2: connection string completa ────────────────────────────────

    [Fact]
    public void Redact_DeveMascarar_ConnStringPostgres_PreservandoHost()
    {
        var input = "DbUpdateException: failed connecting to Host=prod-db;Port=5432;Database=easystock;Username=app;Password=topSecret123";

        var output = SensitivePatterns.Redact(input);

        output.Should().NotContain("topSecret123");
        output.Should().Contain("Password=[REDACTED]");
        output.Should().Contain("Host=prod-db"); // Host preservado para debugging
    }

    // ── Padrao 3: JWT cru sem prefixo ────────────────────────────────────────

    [Fact]
    public void Redact_DeveMascarar_JwtCruEmTextoLivre()
    {
        var jwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4ifQ.AbCdEfGhIjKlMnOpQrStUvWxYz0123456789";
        var input = $"token persisted: {jwt} for user 42";

        var output = SensitivePatterns.Redact(input);

        output.Should().NotContain(jwt);
        output.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void Redact_NaoDevePiorar_GuidComum()
    {
        // Guard contra false positive — UUIDs nao podem virar [REDACTED]
        // (3 segmentos base64url <20 chars cada — abaixo do threshold do JWT regex)
        var guid = "550e8400-e29b-41d4-a716-446655440000";

        var output = SensitivePatterns.Redact($"user {guid} logged in");

        output.Should().Contain(guid);
    }

    // ── Caminho neutro ───────────────────────────────────────────────────────

    [Fact]
    public void Redact_NaoDeveAlterar_LogSemSegredo()
    {
        var input = "[INF] Pedido 42 criado em 2026-05-30 12:00:00 by usuario abc";

        var output = SensitivePatterns.Redact(input);

        output.Should().Be(input);
    }

    [Fact]
    public void Redact_DeveSerSeguro_ParaInputNuloOuVazio()
    {
        SensitivePatterns.Redact("").Should().BeEmpty();
        SensitivePatterns.Redact(null!).Should().BeNull();
    }
}
