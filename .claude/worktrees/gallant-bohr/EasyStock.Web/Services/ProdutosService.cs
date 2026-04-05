using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Produtos;

namespace EasyStock.Web.Services;

public class ProdutosService(ApiClient api)
{
    public Task<ApiResult<PagedResult<Produto>>> ListarAsync(
        int page = 1, int limit = 20,
        string? categoria = null, string? status = null, string? search = null)
    {
        var qs = $"produtos?page={page}&pageSize={limit}";
        if (!string.IsNullOrEmpty(search)) qs += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrEmpty(categoria)) qs += $"&categoria={Uri.EscapeDataString(categoria)}";
        if (!string.IsNullOrEmpty(status)) qs += $"&status={Uri.EscapeDataString(status)}";
        return api.GetAsync<PagedResult<Produto>>(qs);
    }

    public Task<ApiResult<Produto>> ObterAsync(string id) =>
        api.GetAsync<Produto>($"produtos/{id}");

    public Task<ApiResult<Produto>> CriarAsync(ProdutoFormViewModel vm) =>
        api.PostAsync<Produto>("produtos", new
        {
            nome = vm.Nome,
            sku = vm.Sku,
            categoria = vm.Categoria,
            subcategoria = vm.Subcategoria,
            preco = vm.Preco,
            custo = vm.Custo,
            peso = vm.Peso,
            descricao = vm.Descricao,
            emoji = vm.Emoji
        });

    public Task<ApiResult<object>> EditarAsync(string id, ProdutoFormViewModel vm) =>
        api.PatchAsync<object>($"produtos/{id}", new
        {
            produtoId = id,
            nome = vm.Nome,
            sku = vm.Sku,
            categoria = vm.Categoria,
            subcategoria = vm.Subcategoria,
            preco = vm.Preco,
            custo = vm.Custo,
            peso = vm.Peso,
            descricao = vm.Descricao,
            emoji = vm.Emoji
        });

    public Task<ApiResult<bool>> ExcluirAsync(string id) =>
        api.DeleteAsync($"produtos/{id}");

    public Task<ApiResult<object>> HistoricoAsync(string id) =>
        api.GetAsync<object>($"produtos/{id}/historico");

    public async Task<ApiResult<object>> UploadFotoAsync(string id, IFormFile foto)
    {
        using var form = new MultipartFormDataContent();
        using var stream = foto.OpenReadStream();
        form.Add(new StreamContent(stream), "file", foto.FileName);
        return await api.PostMultipartAsync<object>($"produtos/{id}/fotos", form);
    }

    public Task<ApiResult<object>> AdicionarVariacaoAsync(string id, string nome) =>
        api.PostAsync<object>($"produtos/{id}/variacoes", new { produtoId = id, nome });

    public Task<ApiResult<bool>> RemoverVariacaoAsync(string id, string vid) =>
        api.DeleteAsync($"produtos/{id}/variacoes/{vid}");
}
