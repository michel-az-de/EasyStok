using System.Text.Json;
using EasyStock.Api.Http;
using EasyStock.Api.Observability;
using EasyStock.Application.Ports.Output.Observability;
using EasyStock.Application.UseCases.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Http;

/// <summary>
/// #2018 (estrutura-20): a mesma <see cref="UseCaseValidationException"/> responde igual na rota que a pega no controller
/// (<c>catch { return DataBadRequest(ex); }</c>, 93 pontos) e na que deixa subir até o <c>GlobalExceptionHandler</c>. Antes,
/// o controller devolvia <c>BAD_REQUEST</c> com o texto em <c>message</c> e o handler <c>VALIDATION_ERROR</c> com
/// "Requisição inválida" em <c>message</c> e o texto em <c>detail</c>: a tela lia o motivo num campo ou noutro conforme a rota.
/// </summary>
public class ErroDeValidacaoIgualEmTodasAsRotasTests
{
    private sealed class ControllerDeTeste : EasyStockControllerBase
    {
        public IActionResult Capturado(UseCaseValidationException ex) => DataBadRequest(ex);
    }

    private static JsonElement Serializar(object corpo) =>
        JsonSerializer.SerializeToElement(corpo, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<JsonElement> ViaHandlerAsync(UseCaseValidationException ex)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance, Substitute.For<IOperationalMetrics>());
        var contexto = new DefaultHttpContext();
        contexto.Request.Path = "/api/qualquer";
        contexto.Response.Body = new MemoryStream();
        contexto.Items["CorrelationId"] = "corr-1";

        await handler.TryHandleAsync(contexto, ex, CancellationToken.None);

        contexto.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        contexto.Response.Body.Position = 0;
        return (await JsonDocument.ParseAsync(contexto.Response.Body)).RootElement.Clone();
    }

    private static JsonElement ViaController(UseCaseValidationException ex)
    {
        var resultado = new ControllerDeTeste().Capturado(ex).Should().BeOfType<BadRequestObjectResult>().Subject;
        return Serializar(resultado.Value!);
    }

    public static TheoryData<UseCaseValidationException> Excecoes => new()
    {
        new UseCaseValidationException("Informe o telefone."),
        new UseCaseValidationException("CYCLE_DETECTED", "Ciclo detectado.", new { itens = new[] { "a", "b" } }),
    };

    [Theory]
    [MemberData(nameof(Excecoes))]
    public async Task ControllerEHandler_RespondemComOMesmoCodeMessageEDetails(UseCaseValidationException ex)
    {
        var deHandler = (await ViaHandlerAsync(ex)).GetProperty("error");
        var deController = ViaController(ex).GetProperty("error");

        deHandler.GetProperty("code").GetString().Should().Be(deController.GetProperty("code").GetString());
        deHandler.GetProperty("message").GetString().Should().Be(deController.GetProperty("message").GetString());
        deHandler.GetProperty("message").GetString().Should().Be(ex.Message, "o texto do motivo vai em message em todas as rotas");
        deHandler.TryGetProperty("details", out var detalhesHandler).Should().Be(deController.TryGetProperty("details", out var detalhesController));
        if (ex.Details is not null) detalhesHandler.ToString().Should().Be(detalhesController.ToString());
    }

    [Fact]
    public async Task SemCodigoProprio_OCodigoEBadRequest_ComCodigoProprioElePrevalece()
    {
        (await ViaHandlerAsync(new UseCaseValidationException("x"))).GetProperty("error").GetProperty("code").GetString()
            .Should().Be("BAD_REQUEST");
        (await ViaHandlerAsync(new UseCaseValidationException("CYCLE_DETECTED", "x"))).GetProperty("error").GetProperty("code").GetString()
            .Should().Be("CYCLE_DETECTED");
    }

    [Fact]
    public async Task Handler_MantemOTextoTambemEmDetail_ParaQuemLiaOCampoAntigo()
    {
        var erro = (await ViaHandlerAsync(new UseCaseValidationException("Informe o telefone."))).GetProperty("error");

        erro.GetProperty("detail").GetString().Should().Be("Informe o telefone.");
        erro.GetProperty("correlationId").GetString().Should().Be("corr-1");
    }
}
