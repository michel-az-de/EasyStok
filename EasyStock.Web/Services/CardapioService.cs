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

    /// <summary>Cria a vitrine do tenant (self-service). Slug = endereço público único.</summary>
    public Task<ApiResult<object>> CriarVitrineAsync(string titulo, string slug, decimal? pedidoMinimo) =>
        api.PostAsync<object>("minha-vitrine", new
        {
            tituloPublico = titulo,
            slug,
            pedidoMinimoEntrega = pedidoMinimo ?? 0m,
        });

    public Task<ApiResult<CardapioListaApi>> ListarAsync() =>
        api.GetAsync<CardapioListaApi>("minha-vitrine/cardapio");

    /// <summary>Detalhe completo de um item — prefill do formulário de edição (GET-by-id).</summary>
    public Task<ApiResult<CardapioItemDetalheApi>> ObterItemAsync(Guid itemId) =>
        api.GetAsync<CardapioItemDetalheApi>($"minha-vitrine/cardapio/{itemId}");

    /// <summary>
    /// Cria item avulso (ProdutoId null) ou vinculado. A foto sobe DEPOIS, via
    /// <see cref="UploadFotoAsync"/> (o item precisa existir primeiro). Para avulso o
    /// NomePublico é obrigatório (validado no formulário antes do POST).
    /// </summary>
    public Task<ApiResult<CardapioCriarResultApi>> CriarAsync(CardapioItemFormApi f) =>
        api.PostAsync<CardapioCriarResultApi>("minha-vitrine/cardapio", new
        {
            produtoId = f.ProdutoId,
            opcoes = MapOpcoes(f.Opcoes),
            nomePublico = f.NomePublico,
            categoriaTexto = f.CategoriaTexto,
            ordemExibicao = 0.0,
            visivel = f.Visivel,
            descricaoPublica = f.DescricaoPublica,
            ingredientes = f.Ingredientes,
            alergenos = f.Alergenos,
            sugestaoMolho = f.SugestaoMolho,
            tempoPreparo = f.TempoPreparo,
            fotoUrl = (string?)null,        // foto sobe via endpoint dedicado após criar
            precoStorefront = f.PrecoStorefront,
            tag = f.Tag,
            pesoExibicao = f.PesoExibicao,
            filtrosJson = (string?)null,
        });

    /// <summary>
    /// Edição completa (PUT). Contrato do backend: <c>""</c> limpa o campo, <c>null</c> mantém.
    /// FotoUrl segue null aqui (a foto só muda via <see cref="UploadFotoAsync"/>); FiltrosJson
    /// também null pois não há controle no formulário v1 (null preserva o valor existente).
    /// </summary>
    public Task<ApiResult<object>> EditarAsync(Guid itemId, CardapioItemFormApi f) =>
        api.PutAsync<object>($"minha-vitrine/cardapio/{itemId}", new
        {
            opcoes = MapOpcoes(f.Opcoes),
            nomePublico = f.NomePublico,
            categoriaTexto = f.CategoriaTexto,
            descricaoPublica = f.DescricaoPublica,
            ingredientes = f.Ingredientes,
            alergenos = f.Alergenos,
            sugestaoMolho = f.SugestaoMolho,
            tempoPreparo = f.TempoPreparo,
            fotoUrl = (string?)null,
            precoStorefront = f.PrecoStorefront,
            tag = f.Tag,
            pesoExibicao = f.PesoExibicao,
            filtrosJson = (string?)null,
        });

    public Task<ApiResult<ToggleVisivelResultApi>> TogglePublicarAsync(Guid itemId) =>
        api.PostAsync<ToggleVisivelResultApi>($"minha-vitrine/cardapio/{itemId}/toggle-visivel", new { });

    public Task<ApiResult<ToggleDisponivelResultApi>> ToggleDisponivelAsync(Guid itemId) =>
        api.PostAsync<ToggleDisponivelResultApi>($"minha-vitrine/cardapio/{itemId}/toggle-disponivel", new { });

    public Task<ApiResult<object>> ReordenarAsync(Guid itemId, double novaOrdem) =>
        api.PostAsync<object>($"minha-vitrine/cardapio/{itemId}/reordenar", new { novaOrdem });

    public Task<ApiResult<bool>> RemoverAsync(Guid itemId) =>
        api.DeleteAsync($"minha-vitrine/cardapio/{itemId}");

    /// <summary>
    /// Sobe a foto do item (multipart). O backend resolve a vitrine pelo token e fecha IDOR;
    /// retorna a URL pública. Não força Content-Type do form — o boundary é do HttpClient.
    /// </summary>
    public async Task<ApiResult<CardapioFotoResultApi>> UploadFotoAsync(Guid itemId, IFormFile foto)
    {
        using var form = new MultipartFormDataContent();
        await using var stream = foto.OpenReadStream();
        var sc = new StreamContent(stream);
        sc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            foto.ContentType ?? "application/octet-stream");
        form.Add(sc, "file", foto.FileName);
        return await api.PostMultipartAsync<CardapioFotoResultApi>($"uploads/cardapio-item/{itemId}/foto", form);
    }

    // ADR-0035: mapeia as opções p/ o corpo JSON (camelCase) que o backend liga a CardapioItemVariacaoInput.
    // null = não toca nas opções; [] = reconcilia para zero (remove todas).
    private static object? MapOpcoes(IReadOnlyList<CardapioOpcaoApi>? opcoes) =>
        opcoes?.Select(o => new
        {
            id = o.Id,
            rotulo = o.Rotulo,
            precoStorefront = o.PrecoStorefront,
            disponivel = o.Disponivel,
            ehPadrao = o.EhPadrao,
            pesoExibicao = o.PesoExibicao,
            sku = o.Sku,
            ordemExibicao = o.OrdemExibicao,
        }).ToList();
}
