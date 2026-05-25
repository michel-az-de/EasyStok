using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Faq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Controllers
{
    /// <summary>
    /// FAQ publico — sem multi-tenant. Acesso anonimo.
    /// </summary>
    [ApiController]
    [Route("api/faq")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    public class FaqController(
        BuscarFaqUseCase buscarUseCase,
        ListarCategoriasFaqUseCase listarCategoriasUseCase,
        ObterFaqItemUseCase obterUseCase,
        RegistrarFeedbackFaqUseCase feedbackUseCase) : EasyStockControllerBase
    {
        [HttpGet("categorias")]
        public async Task<IActionResult> ListarCategorias(CancellationToken ct)
        {
            var categorias = await listarCategoriasUseCase.ExecuteAsync(ct);
            return DataOk(categorias);
        }

        [HttpGet]
        public async Task<IActionResult> Buscar(
            [FromQuery] string? termo = null,
            [FromQuery] Guid? categoriaId = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            CancellationToken ct = default)
        {
            var result = await buscarUseCase.ExecuteAsync(new BuscarFaqQuery(termo, categoriaId, page, pageSize), ct);
            return DataOk(result);
        }

        [HttpGet("{categoriaSlug}/{itemSlug}")]
        public async Task<IActionResult> Obter(string categoriaSlug, string itemSlug, CancellationToken ct)
        {
            try
            {
                var item = await obterUseCase.ExecuteAsync(
                    new ObterFaqItemQuery(
                        categoriaSlug,
                        itemSlug,
                        ObterIp(),
                        Termo: null,
                        Origem: HttpContext.Request.Headers.UserAgent.ToString(),
                        RegistrarVisualizacao: true),
                    ct);
                return DataOk(item);
            }
            catch (UseCaseValidationException ex)
            {
                return ex.Message.Contains("nao encontrado", StringComparison.OrdinalIgnoreCase)
                    ? DataNotFound(ex.Message)
                    : DataBadRequest(ex.Message);
            }
        }

        [HttpPost("{itemId:guid}/feedback")]
        [EnableRateLimiting("public-post")]
        public async Task<IActionResult> Feedback(Guid itemId, [FromBody] FeedbackRequest req, CancellationToken ct)
        {
            try
            {
                await feedbackUseCase.ExecuteAsync(
                    new RegistrarFeedbackFaqCommand(itemId, req.Util, req.Comentario, ObterIp()),
                    ct);
                return NoContent();
            }
            catch (UseCaseValidationException ex)
            {
                return DataBadRequest(ex.Message);
            }
        }

        public sealed record FeedbackRequest(bool Util, string? Comentario);

        private string? ObterIp()
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            // Se atras de proxy, X-Forwarded-For tem prioridade
            if (HttpContext.Request.Headers.TryGetValue("X-Forwarded-For", out var fwd) && !string.IsNullOrWhiteSpace(fwd.ToString()))
            {
                ip = fwd.ToString().Split(',')[0].Trim();
            }
            return ip;
        }
    }
}
