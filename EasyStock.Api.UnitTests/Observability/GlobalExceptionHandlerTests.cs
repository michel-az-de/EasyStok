using System.Text.Json;
using EasyStock.Api.Observability;
using EasyStock.Application.UseCases.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyStock.Api.UnitTests.Observability;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Deve_retornar_400_para_erro_de_validacao()
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
        Assert.Equal("Requisicao invalida", payload.RootElement.GetProperty("title").GetString());
        Assert.Equal("EmpresaId e obrigatorio.", payload.RootElement.GetProperty("detail").GetString());
        Assert.Equal("corr-123", payload.RootElement.GetProperty("correlationId").GetString());
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
        Assert.Equal("Conflito de concorrencia", payload.RootElement.GetProperty("title").GetString());
        Assert.Equal("corr-409", payload.RootElement.GetProperty("correlationId").GetString());
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
