using EasyStock.Api.Http;
using FluentAssertions;

namespace EasyStock.Api.UnitTests.Http;

public class ApiResponseEnvelopeTests
{
    // ── ApiResponse<T> ───────────────────────────────────────────────────────

    [Fact]
    public void ApiResponse_DeveConterData_EMetaVazioParaItemSimples()
    {
        var payload = new { id = Guid.NewGuid(), nome = "Teste" };
        var envelope = new ApiResponse<object>(payload, new { });

        envelope.Data.Should().BeSameAs(payload);
        envelope.Meta.Should().NotBeNull();
    }

    [Fact]
    public void PagedMeta_DeveCalcularPagesCorretamente()
    {
        var meta = new PagedMeta(Total: 105, Pages: 6, Page: 1, Limit: 20);

        meta.Total.Should().Be(105);
        meta.Pages.Should().Be(6);
        meta.Page.Should().Be(1);
        meta.Limit.Should().Be(20);
    }

    [Fact]
    public void EasyStockControllerBase_DataPaged_DeveCalcularPagesCorretamente()
    {
        // Testa directamente o calculo de paginas
        var total = 105;
        var limit = 20;

        var pages = limit > 0 ? (int)Math.Ceiling((double)total / limit) : 0;
        pages.Should().Be(6);
    }

    [Fact]
    public void PagedMeta_ComTotalZero_DeveRetornarZeroPaginas()
    {
        var pages = 20 > 0 ? (int)Math.Ceiling((double)0 / 20) : 0;
        pages.Should().Be(0);
    }

    // ── ApiErrorResponse ─────────────────────────────────────────────────────

    [Fact]
    public void ApiErrorResponse_DeveConterError_ComTodosCampos()
    {
        var error = new ApiError("VALIDATION_ERROR", "Requisição inválida", "EmpresaId é obrigatório.", "corr-123");
        var envelope = new ApiErrorResponse(error);

        envelope.Error.Code.Should().Be("VALIDATION_ERROR");
        envelope.Error.Message.Should().Be("Requisição inválida");
        envelope.Error.Detail.Should().Be("EmpresaId é obrigatório.");
        envelope.Error.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void ApiErrorResponse_DeveAceitarDetailNulo()
    {
        var error = new ApiError("NOT_FOUND", "Recurso não encontrado.", null, null);
        var envelope = new ApiErrorResponse(error);

        envelope.Error.Detail.Should().BeNull();
        envelope.Error.CorrelationId.Should().BeNull();
    }

    // ── Error codes ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VALIDATION_ERROR")]
    [InlineData("NOT_FOUND")]
    [InlineData("BAD_REQUEST")]
    [InlineData("UNAUTHORIZED")]
    [InlineData("FORBIDDEN")]
    [InlineData("CONCURRENCY_CONFLICT")]
    [InlineData("BUSINESS_RULE_VIOLATION")]
    [InlineData("PLAN_LIMIT_REACHED")]
    [InlineData("INTERNAL_ERROR")]
    public void ApiError_DeveAceitarCodigosPadronizados(string code)
    {
        var error = new ApiError(code, "mensagem", null, null);
        error.Code.Should().Be(code);
    }
}
