using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Produtos;

namespace EasyStock.Web.Services;

public class ProdutosService(ApiClient api, SessionService session)
{
    private Guid GetEmpresaId() =>
        Guid.TryParse(session.GetLojaId(), out var id) ? id : Guid.Empty;

    public Task<ApiResult<List<ProdutoResumo>>> ListarAsync(
        int page = 1, int limit = 20)
    {
        var qs = $"produtos?page={page}&pageSize={limit}";
        return api.GetAsync<List<ProdutoResumo>>(qs);
    }

    public Task<ApiResult<List<ProdutoResumo>>> BuscarAsync(string termo, int limite = 10) =>
        api.GetAsync<List<ProdutoResumo>>(
            $"produtos/search?termo={Uri.EscapeDataString(termo)}&limite={limite}");

    public Task<ApiResult<ProdutoDetalhe>> ObterAsync(string id) =>
        api.GetAsync<ProdutoDetalhe>($"produtos/{id}");

    public Task<ApiResult<List<CategoriaApi>>> ListarCategoriasAsync() =>
        api.GetAsync<List<CategoriaApi>>("categorias");

    public Task<ApiResult<CadastrarProdutoApiResult>> CriarAsync(ProdutoFormViewModel vm) =>
        api.PostAsync<CadastrarProdutoApiResult>("produtos", new
        {
            empresaId = GetEmpresaId(),
            categoriaId = vm.CategoriaId,
            nome = vm.Nome,
            descricaoBase = vm.DescricaoBase,
            marca = vm.Marca,
            tipo = vm.Tipo,
            skuBase = vm.SkuBase,
            codigoBarras = vm.CodigoBarras,
            controlaValidade = vm.ControlaValidade,
            custoReferencia = vm.CustoReferencia,
            precoReferencia = vm.PrecoReferencia,
            variacoes = vm.Variacoes
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => new { nome = v.Trim(), ativa = true })
                .ToArray()
        });

    public Task<ApiResult<object>> EditarAsync(string id, ProdutoFormViewModel vm) =>
        api.PatchAsync<object>($"produtos/{id}", new
        {
            empresaId = GetEmpresaId(),
            produtoId = Guid.TryParse(id, out var pid) ? pid : Guid.Empty,
            categoriaId = vm.CategoriaId,
            nome = vm.Nome,
            descricaoBase = vm.DescricaoBase,
            marca = vm.Marca,
            tipo = vm.Tipo,
            skuBase = vm.SkuBase,
            codigoBarras = vm.CodigoBarras,
            controlaValidade = vm.ControlaValidade,
            custoReferencia = vm.CustoReferencia,
            precoReferencia = vm.PrecoReferencia,
            status = vm.Status
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

    public Task<ApiResult<object>> AdicionarVariacaoAsync(string id, string nome, string? sku = null) =>
        api.PostAsync<object>($"produtos/{id}/variacoes", new
        {
            empresaId = GetEmpresaId(),
            produtoId = Guid.TryParse(id, out var pid) ? pid : Guid.Empty,
            nome,
            sku,
            ativa = true
        });

    public Task<ApiResult<bool>> RemoverVariacaoAsync(string id, string vid) =>
        api.DeleteAsync($"produtos/{id}/variacoes/{vid}");
}
