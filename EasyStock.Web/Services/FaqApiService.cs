using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Faq;

namespace EasyStock.Web.Services;

/// <summary>
/// Wrapper sobre <see cref="ApiClient"/> para consumir o FAQ publico
/// (sem multi-tenant). Endpoints: GET /api/faq/categorias, GET /api/faq,
/// GET /api/faq/{categoriaSlug}/{itemSlug}, POST /api/faq/{itemId}/feedback.
/// </summary>
public sealed class FaqApiService(ApiClient api)
{
    public Task<ApiResult<List<FaqCategoriaDto>>> ListarCategoriasAsync() =>
        api.GetAsync<List<FaqCategoriaDto>>("faq/categorias");

    public Task<ApiResult<BuscarFaqResultadoDto>> BuscarAsync(
        string? termo, Guid? categoriaId, int page = 1, int pageSize = 10)
    {
        var qs = $"faq?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(termo)) qs += $"&termo={Uri.EscapeDataString(termo)}";
        if (categoriaId.HasValue) qs += $"&categoriaId={categoriaId.Value}";
        return api.GetAsync<BuscarFaqResultadoDto>(qs);
    }

    public Task<ApiResult<FaqItemDetalheDto>> ObterAsync(string categoriaSlug, string itemSlug) =>
        api.GetAsync<FaqItemDetalheDto>($"faq/{Uri.EscapeDataString(categoriaSlug)}/{Uri.EscapeDataString(itemSlug)}");

    public Task<ApiResult<object>> EnviarFeedbackAsync(Guid itemId, bool util, string? comentario) =>
        api.PostAsync<object>($"faq/{itemId}/feedback", new { util, comentario });
}
