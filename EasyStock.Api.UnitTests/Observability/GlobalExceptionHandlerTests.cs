using System.Text.Json;
using EasyStock.Api.Http;
using EasyStock.Api.Observability;
using FluentAssertions;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Api.UnitTests.Observability;

public class GlobalExceptionHandlerTests
{
    // ── Estrutura do envelope de erro ────────────────────────────────────────

    [Fact]
    public async Task Deve_retornar_400_com_envelope_error_para_erro_de_validacao()
    {
        var handler = new GlobalExceptionHandler();
        var context = CriarHttpContext("/api/estoque/entrada", "corr-123");

        var handled = await handler.TryHandleAsync(
            context,
            new UseCaseValidationException("EmpresaId e obrigatorio."),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        var payload = await LerRespostaAsync(context);
        var error = payload.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("Requisicao invalida", error.GetProperty("message").GetString());
        Assert.Equal("EmpresaId e obrigatorio.", error.GetProperty("detail").GetString());
        Assert.Equal("corr-123", error.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Deve_retornar_409_para_conflito_de_concorrencia()
    {
        var handler = new GlobalExceptionHandler();
        var context = CriarHttpContext("/api/estoque/saida", "corr-409");

        var handled = await handler.TryHandleAsync(
            context,
            new DbUpdateConcurrencyException("conflito"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);

        var payload = await LerRespostaAsync(context);
        var error = payload.RootElement.GetProperty("error");
        Assert.Equal("CONCURRENCY_CONFLICT", error.GetProperty("code").GetString());
        Assert.Equal("corr-409", error.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Deve_retornar_401_para_credenciais_invalidas()
    {
        var handler = new GlobalExceptionHandler();
        var context = CriarHttpContext("/api/auth/login", "corr-401");

        var handled = await handler.TryHandleAsync(
            context,
            new CredenciaisInvalidasException(),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);

        var payload = await LerRespostaAsync(context);
        var error = payload.RootElement.GetProperty("error");
        Assert.Equal("UNAUTHORIZED", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deve_retornar_402_para_plano_limite_atingido()
    {
        var handler = new GlobalExceptionHandler();
        var context = CriarHttpContext("/api/ia/anuncio", "corr-402");

        var handled = await handler.TryHandleAsync(
            context,
            new PlanoLimiteAtingidoException("geracoes_ia"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status402PaymentRequired, context.Response.StatusCode);

        var payload = await LerRespostaAsync(context);
        var error = payload.RootElement.GetProperty("error");
        Assert.Equal("PLAN_LIMIT_REACHED", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deve_retornar_500_para_excecao_desconhecida()
    {
        var handler = new GlobalExceptionHandler();
        var context = CriarHttpContext("/api/qualquer", "corr-500");

        var handled = await handler.TryHandleAsync(
            context,
            new Exception("erro inesperado"),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

        var payload = await LerRespostaAsync(context);
        var error = payload.RootElement.GetProperty("error");
        Assert.Equal("INTERNAL_ERROR", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Envelope_NaoDeveTerPropriedadesLegadas_TitleDetailStatus()
    {
        var handler = new GlobalExceptionHandler();
        var context = CriarHttpContext("/api/teste", "corr-x");

        await handler.TryHandleAsync(
            context,
            new UseCaseValidationException("teste"),
            CancellationToken.None);

        var payload = await LerRespostaAsync(context);
        // Deve ter "error" na raiz, nao "title" / "detail" / "status" (ProblemDetails legado)
        payload.RootElement.TryGetProperty("error", out _).Should().BeTrue();
        payload.RootElement.TryGetProperty("title", out _).Should().BeFalse();
        payload.RootElement.TryGetProperty("status", out _).Should().BeFalse();
    }

    private static DefaultHttpContext CriarHttpContext(string path, string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.Items["CorrelationId"] = correlationId;
        return context;
    }

    private static async Task<JsonDocument> LerRespostaAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
