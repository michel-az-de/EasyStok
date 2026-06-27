using System.Text.Json;
using EasyStock.Api.Observability;
using EasyStock.Application.Ports.Output.Observability;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Observability;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Deve_retornar_400_com_envelope_error_para_erro_de_validacao()
    {
        var handler = CriarHandler();
        var context = CriarHttpContext("/api/estoque/entrada", "corr-123");

        var handled = await handler.TryHandleAsync(
            context,
            new UseCaseValidationException("EmpresaId é obrigatório."),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        var payload = await LerRespostaAsync(context);
        var error = payload.RootElement.GetProperty("error");
        Assert.Equal("VALIDATION_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("Requisição inválida", error.GetProperty("message").GetString());
        Assert.Equal("EmpresaId é obrigatório.", error.GetProperty("detail").GetString());
        Assert.Equal("corr-123", error.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Deve_remover_sufixo_Parameter_de_ArgumentException()
    {
        var handler = CriarHandler();
        var context = CriarHttpContext("/api/produtos", "corr-arg");

        var handled = await handler.TryHandleAsync(
            context,
            new ArgumentOutOfRangeException("valor", "Valor monetário não pode ser negativo."),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        var payload = await LerRespostaAsync(context);
        var detail = payload.RootElement.GetProperty("error").GetProperty("detail").GetString();
        detail.Should().Be("Valor monetário não pode ser negativo.");
        detail.Should().NotContain("Parameter");
    }

    [Fact]
    public async Task Deve_retornar_409_para_conflito_de_concorrencia()
    {
        var handler = CriarHandler();
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
        var handler = CriarHandler();
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
    public async Task Deve_retornar_403_para_acesso_nao_autorizado()
    {
        var handler = CriarHandler();
        var context = CriarHttpContext("/api/reports/nfce.livro-saidas/runs", "corr-403");

        var handled = await handler.TryHandleAsync(
            context,
            new UnauthorizedAccessException("Sem permissão para o relatório 'nfce.livro-saidas'."),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        var payload = await LerRespostaAsync(context);
        var error = payload.RootElement.GetProperty("error");
        Assert.Equal("FORBIDDEN", error.GetProperty("code").GetString());
        Assert.Equal("Sem permissão para o relatório 'nfce.livro-saidas'.",
            error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Deve_retornar_402_para_plano_limite_atingido()
    {
        var handler = CriarHandler();
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
        var handler = CriarHandler();
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
        var handler = CriarHandler();
        var context = CriarHttpContext("/api/teste", "corr-x");

        await handler.TryHandleAsync(
            context,
            new UseCaseValidationException("teste"),
            CancellationToken.None);

        var payload = await LerRespostaAsync(context);
        payload.RootElement.TryGetProperty("error", out _).Should().BeTrue();
        payload.RootElement.TryGetProperty("title", out _).Should().BeFalse();
        payload.RootElement.TryGetProperty("status", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Deve_incrementar_falhas_operacao_com_code_INTERNAL_ERROR_em_5xx()
    {
        var metrics = Substitute.For<IOperationalMetrics>();
        var handler = CriarHandler(metrics);
        var context = CriarHttpContext("/api/qualquer", "corr-metric-500");

        await handler.TryHandleAsync(context, new Exception("falhou"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        metrics.Received(1).IncrementFalhasOperacao("INTERNAL_ERROR");
    }

    [Fact]
    public async Task Deve_incrementar_falhas_operacao_com_code_NOT_SUPPORTED_em_501()
    {
        var metrics = Substitute.For<IOperationalMetrics>();
        var handler = CriarHandler(metrics);
        var context = CriarHttpContext("/api/qualquer", "corr-metric-501");

        await handler.TryHandleAsync(context, new NotSupportedException("indisponível"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        metrics.Received(1).IncrementFalhasOperacao("NOT_SUPPORTED");
    }

    [Fact]
    public async Task Nao_deve_incrementar_falhas_operacao_em_erro_4xx()
    {
        var metrics = Substitute.For<IOperationalMetrics>();
        var handler = CriarHandler(metrics);
        var context = CriarHttpContext("/api/qualquer", "corr-metric-400");

        await handler.TryHandleAsync(context, new UseCaseValidationException("inválido"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        metrics.DidNotReceive().IncrementFalhasOperacao(Arg.Any<string>());
    }

    private static GlobalExceptionHandler CriarHandler(IOperationalMetrics? metrics = null) =>
        new(NullLogger<GlobalExceptionHandler>.Instance, metrics ?? Substitute.For<IOperationalMetrics>());

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
