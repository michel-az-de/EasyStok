using EasyStock.Web.Models.Api;

namespace EasyStock.Web.Services;

/// <summary>
/// BFF da gestão de cardápio do tenant. Fala com /api/minha-vitrine/cardapio
/// (TenantVitrineCardapioController, ADR-0031). O storefront é resolvido pela empresa do
/// token no backend — aqui NÃO passamos empresaId; o TokenRefreshHandler injeta o JWT.
/// Espelha o padrão de LojasService (ApiClient + ApiResult).
/// </summary>
public class CardapioService(ApiClient api)
{
    public Task<ApiResult<VitrineResumoApi>> ObterVitrineAsync() =>
        api.GetAsync<VitrineResumoApi>("minha-vitrine");

    public Task<ApiResult<CardapioListaApi>> ListarAsync() =>
        api.GetAsync<CardapioListaApi>("minha-vitrine/cardapio");

    public Task<ApiResult<object>> CriarAvulsoAsync(
        string nome, decimal preco, string? categoria, string? fotoUrl, string? descricao, bool publicar) =>
        api.PostAsync<object>("minha-vitrine/cardapio", new
        {
            produtoId = (Guid?)null,
            nomePublico = nome,
            categoriaTexto = categoria,
            ordemExibicao = 0.0,
            visivel = publicar,
            descricaoPublica = descricao,
            fotoUrl,
            precoStorefront = preco,
        });

    public Task<ApiResult<object>> CriarVinculadoAsync(
        Guid produtoId, decimal? precoOverride, string? categoria, bool publicar) =>
        api.PostAsync<object>("minha-vitrine/cardapio", new
        {
            produtoId,
            nomePublico = (string?)null,
            categoriaTexto = categoria,
            ordemExibicao = 0.0,
            visivel = publicar,
            precoStorefront = precoOverride,
        });

    public Task<ApiResult<object>> EditarAsync(
        Guid itemId, string? nome, decimal? preco, string? categoria, string? fotoUrl, string? descricao) =>
        api.PutAsync<object>($"minha-vitrine/cardapio/{itemId}", new
        {
            nomePublico = nome,
            categoriaTexto = categoria,
            descricaoPublica = descricao,
            fotoUrl,
            precoStorefront = preco,
        });

    public Task<ApiResult<object>> TogglePublicarAsync(Guid itemId) =>
        api.PostAsync<object>($"minha-vitrine/cardapio/{itemId}/toggle-visivel", new { });

    public Task<ApiResult<object>> ToggleDisponivelAsync(Guid itemId) =>
        api.PostAsync<object>($"minha-vitrine/cardapio/{itemId}/toggle-disponivel", new { });

    public Task<ApiResult<object>> ReordenarAsync(Guid itemId, double novaOrdem) =>
        api.PostAsync<object>($"minha-vitrine/cardapio/{itemId}/reordenar", new { novaOrdem });
}
